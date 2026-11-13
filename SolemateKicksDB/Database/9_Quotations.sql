USE SolemateKicksDB;
GO

/* =====================================================================
   Purchasing module - upgrade to follow section 3.3 (Purchasing)

   RUN THIS SCRIPT LAST (after 4_Purchasing.sql, 5_PurchasingRoles.sql and 8_Suppliers.sql).
   It replaces usp_PurCreateOrder, usp_PurReceiveOrder, usp_SupGetAll, usp_SupSave and
   usp_SupDelete, so running 4, 5 or 8 again afterwards would undo it. Safe to run more than once.

   What it does
   1. Roles: the Head (who is also the Owner and the purchasing officer) creates and sends POs,
      manages suppliers and records quotations. The Manager receives the delivery.
   2. Supplier quotations per approved requisition, compared by total; the lowest is the default.
      A PO can only be created from a supplier that has a quotation, using the QUOTED prices,
      and only when at least two quotations were collected.
   3. Payment terms are kept on the quotation and copied to the PO.
   4. A PO records when it was sent to the supplier (written PO instead of a phone order).
   5. Suppliers get a Country, and each supplier has an order history.

   Quotation prices are compared as entered, so enter every quotation in the same currency
   (PHP, the currency of the PO totals).
   ===================================================================== */

/* ---------------- PRODUCT LABEL VIEW ----------------
   The product name column is found automatically (ProductName, Name or Description). */
DECLARE @col sysname =
    CASE WHEN COL_LENGTH(N'dbo.Products', N'ProductName') IS NOT NULL THEN N'ProductName'
         WHEN COL_LENGTH(N'dbo.Products', N'Name')        IS NOT NULL THEN N'Name'
         WHEN COL_LENGTH(N'dbo.Products', N'Description') IS NOT NULL THEN N'Description'
    END;
DECLARE @expr NVARCHAR(200) = CASE WHEN @col IS NULL THEN N'CAST(ProductId AS NVARCHAR(20))' ELSE QUOTENAME(@col) END;
DECLARE @sql NVARCHAR(MAX) =
    N'CREATE OR ALTER VIEW dbo.vw_PurProducts AS SELECT ProductId, CAST(' + @expr + N' AS NVARCHAR(150)) AS ProductName FROM dbo.Products';
EXEC (@sql);
GO

/* ---------------- NEW COLUMNS ---------------- */
IF COL_LENGTH(N'dbo.Suppliers', N'Country') IS NULL
    ALTER TABLE dbo.Suppliers ADD Country NVARCHAR(50) NOT NULL CONSTRAINT DF_Suppliers_Country DEFAULT N'';
GO

IF COL_LENGTH(N'dbo.PurchaseOrders', N'PaymentTerms') IS NULL
    ALTER TABLE dbo.PurchaseOrders ADD PaymentTerms NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'QuotationId') IS NULL
    ALTER TABLE dbo.PurchaseOrders ADD QuotationId INT NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'DateSent') IS NULL
    ALTER TABLE dbo.PurchaseOrders ADD DateSent DATETIME NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'SentById') IS NULL
    ALTER TABLE dbo.PurchaseOrders ADD SentById INT NULL CONSTRAINT FK_PO_SentBy REFERENCES dbo.AppUsers(UserId);
GO

/* fill the country of the suppliers that already exist */
UPDATE dbo.Suppliers
SET Country = CASE
        WHEN [Address] LIKE N'%Vietnam%'        THEN N'Vietnam'
        WHEN [Address] LIKE N'%China%'          THEN N'China'
        WHEN [Address] LIKE N'%USA%'            THEN N'USA'
        WHEN [Address] LIKE N'%United Kingdom%' OR [Address] LIKE N'%, UK' THEN N'United Kingdom'
        ELSE N'' END
WHERE Country = N'';
GO

/* ---------------- QUOTATION TABLES ---------------- */
IF OBJECT_ID(N'dbo.Quotations') IS NULL
BEGIN
    CREATE TABLE dbo.Quotations (
        QuotationId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Quotations PRIMARY KEY,
        RequisitionId INT NOT NULL CONSTRAINT FK_Quo_Req REFERENCES dbo.Requisitions(RequisitionId),
        SupplierId    INT NOT NULL CONSTRAINT FK_Quo_Supplier REFERENCES dbo.Suppliers(SupplierId),
        PaymentTerms  NVARCHAR(100) NOT NULL CONSTRAINT DF_Quo_Terms DEFAULT N'',
        QuotationDate DATETIME NOT NULL CONSTRAINT DF_Quo_Date DEFAULT GETDATE(),
        CreatedById   INT NOT NULL CONSTRAINT FK_Quo_CreatedBy REFERENCES dbo.AppUsers(UserId),
        CONSTRAINT UQ_Quotations_ReqSupplier UNIQUE (RequisitionId, SupplierId)
    );
END
GO

IF OBJECT_ID(N'dbo.QuotationItems') IS NULL
BEGIN
    CREATE TABLE dbo.QuotationItems (
        QuotationId INT NOT NULL CONSTRAINT FK_QuoItems_Quo REFERENCES dbo.Quotations(QuotationId) ON DELETE CASCADE,
        ProductId   INT NOT NULL CONSTRAINT FK_QuoItems_Product REFERENCES dbo.Products(ProductId),
        UnitPrice   DECIMAL(10,2) NOT NULL CONSTRAINT CK_QuoItems_Price CHECK (UnitPrice > 0),
        CONSTRAINT PK_QuotationItems PRIMARY KEY (QuotationId, ProductId)
    );
END
GO

/* ---------------- QUOTATION PROCEDURES ---------------- */

/* Quotations of one requisition with their totals; IsLowest = 1 for the cheapest total */
CREATE OR ALTER PROCEDURE dbo.usp_QuoGetForRequisition
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;

    WITH t AS (
        SELECT q.QuotationId, q.SupplierId, q.PaymentTerms, q.QuotationDate,
               CAST(SUM(ri.Quantity * qi.UnitPrice) AS DECIMAL(12,2)) AS Total
        FROM dbo.Quotations q
        JOIN dbo.RequisitionItems ri ON ri.RequisitionId = q.RequisitionId
        JOIN dbo.QuotationItems qi   ON qi.QuotationId = q.QuotationId AND qi.ProductId = ri.ProductId
        WHERE q.RequisitionId = @RequisitionId
        GROUP BY q.QuotationId, q.SupplierId, q.PaymentTerms, q.QuotationDate
    )
    SELECT t.QuotationId, t.SupplierId, s.SupplierName, ISNULL(s.Country, N'') AS Country,
           t.PaymentTerms, t.QuotationDate, t.Total,
           CAST(CASE WHEN t.Total = (SELECT MIN(Total) FROM t) THEN 1 ELSE 0 END AS BIT) AS IsLowest
    FROM t
    JOIN dbo.Suppliers s ON s.SupplierId = t.SupplierId
    ORDER BY t.Total, s.SupplierName;
END
GO

/* Items of a requisition with the price this supplier already quoted (0 if none yet) */
CREATE OR ALTER PROCEDURE dbo.usp_QuoGetItems
    @RequisitionId INT,
    @SupplierId    INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ri.ProductId, p.ProductName, ri.Quantity,
           CAST(ISNULL(qi.UnitPrice, 0) AS DECIMAL(10,2)) AS UnitPrice
    FROM dbo.RequisitionItems ri
    JOIN dbo.vw_PurProducts p ON p.ProductId = ri.ProductId
    LEFT JOIN dbo.Quotations q      ON q.RequisitionId = ri.RequisitionId AND q.SupplierId = @SupplierId
    LEFT JOIN dbo.QuotationItems qi ON qi.QuotationId = q.QuotationId AND qi.ProductId = ri.ProductId
    WHERE ri.RequisitionId = @RequisitionId
    ORDER BY p.ProductName;
END
GO

/* Add or update the quotation of one supplier for one requisition. Head only.
   @Items is XML text: <items><i p="ProductId" u="UnitPrice"/>...</items>. Returns the QuotationId. */
CREATE OR ALTER PROCEDURE dbo.usp_QuoSave
    @RequisitionId INT,
    @SupplierId    INT,
    @PaymentTerms  NVARCHAR(100),
    @Items         NVARCHAR(MAX),
    @UserId        INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @PaymentTerms = LTRIM(RTRIM(ISNULL(@PaymentTerms, N'')));

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50020, N'Only the Head can record supplier quotations.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved')
        THROW 50021, N'Quotations can only be recorded for an approved requisition.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE RequisitionId = @RequisitionId AND [Status] <> N'Cancelled')
        THROW 50022, N'This requisition already has a purchase order, so its quotations can no longer be changed.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId AND IsActive = 1)
        THROW 50023, N'Please choose an active supplier.', 1;
    IF @PaymentTerms = N''
        THROW 50024, N'Payment terms are required (for example: 30 days, 50% advance).', 1;

    DECLARE @it TABLE (ProductId INT NOT NULL PRIMARY KEY, UnitPrice DECIMAL(10,2) NOT NULL);
    DECLARE @x XML = CAST(@Items AS XML);

    INSERT INTO @it (ProductId, UnitPrice)
    SELECT n.value('@p', 'int'), n.value('@u', 'decimal(10,2)')
    FROM @x.nodes('/items/i') AS t(n);

    IF EXISTS (SELECT 1 FROM dbo.RequisitionItems ri
               WHERE ri.RequisitionId = @RequisitionId
                 AND NOT EXISTS (SELECT 1 FROM @it i WHERE i.ProductId = ri.ProductId AND i.UnitPrice > 0))
        THROW 50025, N'Enter a price greater than zero for every item of the requisition.', 1;

    BEGIN TRAN;

    DECLARE @QuotationId INT =
        (SELECT QuotationId FROM dbo.Quotations WHERE RequisitionId = @RequisitionId AND SupplierId = @SupplierId);

    IF @QuotationId IS NULL
    BEGIN
        INSERT INTO dbo.Quotations (RequisitionId, SupplierId, PaymentTerms, CreatedById)
        VALUES (@RequisitionId, @SupplierId, @PaymentTerms, @UserId);
        SET @QuotationId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.Quotations
        SET PaymentTerms = @PaymentTerms, QuotationDate = GETDATE(), CreatedById = @UserId
        WHERE QuotationId = @QuotationId;

        DELETE FROM dbo.QuotationItems WHERE QuotationId = @QuotationId;
    END

    INSERT INTO dbo.QuotationItems (QuotationId, ProductId, UnitPrice)
    SELECT @QuotationId, i.ProductId, i.UnitPrice
    FROM @it i
    WHERE i.ProductId IN (SELECT ProductId FROM dbo.RequisitionItems WHERE RequisitionId = @RequisitionId);

    COMMIT;
    SELECT @QuotationId;
END
GO

/* ---------------- PURCHASE ORDER PROCEDURES ---------------- */

/* List of purchase orders (now with payment terms and the date sent) */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetOrders
AS
BEGIN
    SET NOCOUNT ON;
    SELECT po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status],
           CAST(ISNULL(SUM(i.Quantity * i.UnitCost), 0) AS DECIMAL(12,2)) AS Total,
           ISNULL(r.RefNo, N'') AS RequisitionRef,
           ISNULL(po.PaymentTerms, N'') AS PaymentTerms,
           po.DateSent
    FROM dbo.PurchaseOrders po
    LEFT JOIN dbo.PurchaseOrderItems i ON i.PurchaseOrderId = po.PurchaseOrderId
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = po.RequisitionId
    GROUP BY po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status], r.RefNo, po.PaymentTerms, po.DateSent
    ORDER BY po.DateCreated DESC, po.PurchaseOrderId DESC;
END
GO

/* Create a PO from an approved requisition and the chosen supplier's QUOTATION. Head only.
   Needs at least two quotations (the UK and Vietnam suppliers). Returns the new PurchaseOrderId. */
CREATE OR ALTER PROCEDURE dbo.usp_PurCreateOrder
    @RequisitionId INT,
    @SupplierId    INT,
    @UserId        INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Supplier NVARCHAR(100) =
        (SELECT SupplierName FROM dbo.Suppliers WHERE SupplierId = @SupplierId AND IsActive = 1);
    DECLARE @QuotationId INT =
        (SELECT QuotationId FROM dbo.Quotations WHERE RequisitionId = @RequisitionId AND SupplierId = @SupplierId);
    DECLARE @Terms NVARCHAR(100) =
        (SELECT PaymentTerms FROM dbo.Quotations WHERE QuotationId = @QuotationId);

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50006, N'Only the Head can create a purchase order.', 1;
    IF @Supplier IS NULL
        THROW 50001, N'Please choose an active supplier.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved')
        THROW 50002, N'Only approved requisitions can be turned into a purchase order.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE RequisitionId = @RequisitionId AND [Status] <> N'Cancelled')
        THROW 50003, N'This requisition already has a purchase order.', 1;
    IF (SELECT COUNT(*) FROM dbo.Quotations WHERE RequisitionId = @RequisitionId) < 2
        THROW 50026, N'Get quotations from at least two suppliers (the UK and Vietnam suppliers) before creating the purchase order.', 1;
    IF @QuotationId IS NULL
        THROW 50027, N'This supplier has no quotation for the requisition.', 1;

    BEGIN TRAN;

    DECLARE @seq INT = (SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE CAST(DateCreated AS DATE) = CAST(GETDATE() AS DATE)) + 1;
    DECLARE @PoNo NVARCHAR(30) = N'PO-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-' + RIGHT(N'0000' + CAST(@seq AS NVARCHAR(10)), 4);

    INSERT INTO dbo.PurchaseOrders (PoNo, RequisitionId, Supplier, SupplierId, CreatedById, PaymentTerms, QuotationId)
    VALUES (@PoNo, @RequisitionId, @Supplier, @SupplierId, @UserId, @Terms, @QuotationId);

    DECLARE @PoId INT = SCOPE_IDENTITY();

    INSERT INTO dbo.PurchaseOrderItems (PurchaseOrderId, ProductId, Quantity, UnitCost)
    SELECT @PoId, ri.ProductId, ri.Quantity, qi.UnitPrice
    FROM dbo.RequisitionItems ri
    JOIN dbo.QuotationItems qi ON qi.QuotationId = @QuotationId AND qi.ProductId = ri.ProductId
    WHERE ri.RequisitionId = @RequisitionId;

    COMMIT;
    SELECT @PoId;
END
GO

/* Header of one PO, for the written purchase order */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetOrderHeader
    @PurchaseOrderId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT po.PoNo, po.DateCreated, po.Supplier,
           ISNULL(s.[Address], N'') AS SupplierAddress,
           ISNULL(s.Email, N'')     AS SupplierEmail,
           ISNULL(s.Country, N'')   AS SupplierCountry,
           ISNULL(po.PaymentTerms, N'') AS PaymentTerms,
           ISNULL(r.RefNo, N'')     AS RequisitionRef,
           ISNULL(r.Branch, N'')    AS Branch,
           po.[Status],
           CAST(ISNULL((SELECT SUM(Quantity * UnitCost) FROM dbo.PurchaseOrderItems
                        WHERE PurchaseOrderId = po.PurchaseOrderId), 0) AS DECIMAL(12,2)) AS Total
    FROM dbo.PurchaseOrders po
    LEFT JOIN dbo.Suppliers s ON s.SupplierId = po.SupplierId
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = po.RequisitionId
    WHERE po.PurchaseOrderId = @PurchaseOrderId;
END
GO

/* Items of one PO, for the written purchase order */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetOrderItems
    @PurchaseOrderId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.ProductName, i.Quantity, i.UnitCost,
           CAST(i.Quantity * i.UnitCost AS DECIMAL(12,2)) AS LineTotal
    FROM dbo.PurchaseOrderItems i
    JOIN dbo.vw_PurProducts p ON p.ProductId = i.ProductId
    WHERE i.PurchaseOrderId = @PurchaseOrderId
    ORDER BY i.ItemId;
END
GO

/* Record that the written PO was sent to the supplier. Head only. */
CREATE OR ALTER PROCEDURE dbo.usp_PurMarkSent
    @PurchaseOrderId INT,
    @UserId          INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50028, N'Only the Head can send a purchase order to the supplier.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId AND [Status] = N'Ordered')
        THROW 50029, N'Only a purchase order with status Ordered can be sent.', 1;

    UPDATE dbo.PurchaseOrders
    SET DateSent = GETDATE(), SentById = @UserId
    WHERE PurchaseOrderId = @PurchaseOrderId;
END
GO

/* Receiving the goods is the Manager's job */
CREATE OR ALTER PROCEDURE dbo.usp_PurReceiveOrder
    @PurchaseOrderId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 50005, N'Only the Manager can mark a purchase order as received.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId AND [Status] = N'Ordered')
        THROW 50004, N'Only purchase orders with status Ordered can be received.', 1;

    BEGIN TRAN;

    DECLARE @PoNo NVARCHAR(30) = (SELECT PoNo FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId);
    DECLARE @Reason NVARCHAR(100) = N'Received ' + @PoNo;
    DECLARE @ItemId INT = 0, @ProductId INT, @Qty INT;

    WHILE 1 = 1
    BEGIN
        SELECT TOP 1 @ItemId = ItemId, @ProductId = ProductId, @Qty = Quantity
        FROM dbo.PurchaseOrderItems
        WHERE PurchaseOrderId = @PurchaseOrderId AND ItemId > @ItemId
        ORDER BY ItemId;

        IF @@ROWCOUNT = 0 BREAK;

        EXEC dbo.usp_InvAdjustStock @ProductId, @Qty, @Reason, @UserId;
    END

    UPDATE dbo.PurchaseOrders
    SET [Status] = N'Received', DateReceived = GETDATE(), ReceivedById = @UserId
    WHERE PurchaseOrderId = @PurchaseOrderId;

    COMMIT;
END
GO

/* ---------------- SUPPLIER PROCEDURES ---------------- */

CREATE OR ALTER PROCEDURE dbo.usp_SupGetAll
    @ActiveOnly INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SupplierId,
           SupplierName,
           ISNULL([Address], N'') AS [Address],
           ISNULL(Email, N'')     AS Email,
           ISNULL(Country, N'')   AS Country
    FROM dbo.Suppliers
    WHERE @ActiveOnly = 0 OR IsActive = 1
    ORDER BY SupplierName;
END
GO

/* Add (@SupplierId = 0) or edit a supplier. Head only. Returns the SupplierId. */
CREATE OR ALTER PROCEDURE dbo.usp_SupSave
    @SupplierId   INT,
    @SupplierName NVARCHAR(100),
    @Address      NVARCHAR(200),
    @Email        NVARCHAR(100),
    @Country      NVARCHAR(50),
    @UserId       INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @SupplierName = LTRIM(RTRIM(ISNULL(@SupplierName, N'')));
    SET @Address      = LTRIM(RTRIM(ISNULL(@Address, N'')));
    SET @Email        = LTRIM(RTRIM(ISNULL(@Email, N'')));
    SET @Country      = LTRIM(RTRIM(ISNULL(@Country, N'')));

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50010, N'Only the Head can add or edit suppliers.', 1;

    IF @SupplierName = N''
        THROW 50011, N'Supplier name is required.', 1;

    IF @Country = N''
        THROW 50016, N'Supplier country is required.', 1;

    IF EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierName = @SupplierName AND SupplierId <> @SupplierId)
        THROW 50012, N'A supplier with this name already exists.', 1;

    IF @SupplierId = 0
    BEGIN
        INSERT INTO dbo.Suppliers (SupplierName, [Address], Email, Country)
        VALUES (@SupplierName, @Address, NULLIF(@Email, N''), @Country);

        SET @SupplierId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
            THROW 50013, N'Supplier not found.', 1;

        UPDATE dbo.Suppliers
        SET SupplierName = @SupplierName, [Address] = @Address, Email = NULLIF(@Email, N''), Country = @Country
        WHERE SupplierId = @SupplierId;

        UPDATE dbo.PurchaseOrders SET Supplier = @SupplierName WHERE SupplierId = @SupplierId;

        IF OBJECT_ID(N'dbo.Deliveries') IS NOT NULL AND COL_LENGTH(N'dbo.Deliveries', N'SupplierId') IS NOT NULL
            EXEC sp_executesql
                N'UPDATE dbo.Deliveries SET SupplierName = @n WHERE SupplierId = @id',
                N'@n NVARCHAR(100), @id INT',
                @n = @SupplierName, @id = @SupplierId;
    END

    SELECT @SupplierId;
END
GO

/* Delete a supplier. Head only. Not allowed when quotations, purchase orders or deliveries use it. */
CREATE OR ALTER PROCEDURE dbo.usp_SupDelete
    @SupplierId INT,
    @UserId     INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50014, N'Only the Head can delete suppliers.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
        THROW 50013, N'Supplier not found.', 1;

    BEGIN TRY
        DELETE FROM dbo.Suppliers WHERE SupplierId = @SupplierId;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 547
            THROW 50015, N'This supplier is used by quotations, purchase orders or deliveries, so it cannot be deleted.', 1;
        THROW;
    END CATCH
END
GO

/* Order history of one supplier (kept for future price and supplier comparison) */
CREATE OR ALTER PROCEDURE dbo.usp_SupOrderHistory
    @SupplierId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Name NVARCHAR(100) = (SELECT SupplierName FROM dbo.Suppliers WHERE SupplierId = @SupplierId);

    SELECT po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status],
           CAST(ISNULL(SUM(i.Quantity * i.UnitCost), 0) AS DECIMAL(12,2)) AS Total,
           ISNULL(r.RefNo, N'') AS RequisitionRef,
           ISNULL(po.PaymentTerms, N'') AS PaymentTerms,
           po.DateSent
    FROM dbo.PurchaseOrders po
    LEFT JOIN dbo.PurchaseOrderItems i ON i.PurchaseOrderId = po.PurchaseOrderId
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = po.RequisitionId
    WHERE po.SupplierId = @SupplierId
       OR (po.SupplierId IS NULL AND po.Supplier = @Name)
    GROUP BY po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status], r.RefNo, po.PaymentTerms, po.DateSent
    ORDER BY po.DateCreated DESC, po.PurchaseOrderId DESC;
END
GO

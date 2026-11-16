-- =====================================================================
--  Solemate - formal supplier QUOTATIONS per purchase requisition   (run AFTER 12_PaperAlignment.sql)
--  Paper 3.3: "supplier quotations are obtained from the UK and Vietnam suppliers and evaluated,
--              following the client's current practice of choosing the lowest price".
--   * a quotation = one supplier's price for every item of the approved purchase requisition (the SHORTAGE only)
--     plus the payment terms offered for that order
--   * at least TWO quotations are needed before a purchase order can be created
--   * the PO is priced from the chosen quotation and uses its payment terms
--   * every saved quotation also updates the supplier's price list (QUOTATIONS tab) for future comparison
--   * no new accounts: the Manager (purchasing officer) records quotations and creates the PO,
--     the Head can also record quotations; the Head's budget approval of the purchase requisition stays as it is
--  Safe to run more than once.
-- =====================================================================
USE SolemateKicksDB;
GO

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

/* Quotations of one requisition with their totals (shortage quantities x quoted prices); IsLowest = 1 for the cheapest total */
CREATE OR ALTER PROCEDURE dbo.usp_QuoGetForRequisition
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;

    WITH t AS (
        SELECT q.QuotationId, q.SupplierId, q.PaymentTerms, q.QuotationDate,
               CAST(SUM(f.Shortage * qi.UnitPrice) AS DECIMAL(12,2)) AS Total
        FROM dbo.Quotations q
        JOIN dbo.fn_PurPrItems(@RequisitionId) f ON f.Shortage > 0
        JOIN dbo.QuotationItems qi ON qi.QuotationId = q.QuotationId AND qi.ProductId = f.ProductId
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

/* Items to buy (the shortage) with the price this supplier already quoted for this requisition.
   If there is no quotation yet, the supplier's price list (QUOTATIONS tab) is used as a suggestion; 0 when there is none. */
CREATE OR ALTER PROCEDURE dbo.usp_QuoGetItems
    @RequisitionId INT,
    @SupplierId    INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT f.ProductId,
           CAST(p.Sku + N'  ' + b.BrandName + N' ' + p.ProductName + N' (size ' + CAST(p.Size AS NVARCHAR(10)) + N')' AS NVARCHAR(150)) AS ProductName,
           f.Shortage AS Quantity,
           CAST(ISNULL(qi.UnitPrice, ISNULL(sp.UnitCost, 0)) AS DECIMAL(10,2)) AS UnitPrice
    FROM dbo.fn_PurPrItems(@RequisitionId) f
    JOIN dbo.Products p ON p.ProductId = f.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    LEFT JOIN dbo.Quotations q      ON q.RequisitionId = @RequisitionId AND q.SupplierId = @SupplierId
    LEFT JOIN dbo.QuotationItems qi ON qi.QuotationId = q.QuotationId AND qi.ProductId = f.ProductId
    LEFT JOIN dbo.SupplierPrices sp ON sp.SupplierId = @SupplierId AND sp.ProductId = f.ProductId
    WHERE f.Shortage > 0
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* Add or update the quotation of one supplier for one requisition. Manager (purchasing officer) or Head.
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

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Manager', N'Head'))
        THROW 50060, N'Only the Manager or the Head can record supplier quotations.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.fn_PurPrItems(@RequisitionId) WHERE Shortage > 0)
        THROW 50061, N'Quotations can only be recorded for a requisition whose purchase requisition was approved by the Head.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE RequisitionId = @RequisitionId
               AND [Status] IN (N'PendingApproval', N'Approved', N'Ordered', N'Received'))
        THROW 50062, N'This requisition already has a purchase order, so its quotations can no longer be changed.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId AND IsActive = 1)
        THROW 50063, N'Please choose an active supplier.', 1;
    IF @PaymentTerms = N''
        THROW 50064, N'Payment terms are required (for example: 30 days, 50% advance).', 1;

    DECLARE @it TABLE (ProductId INT NOT NULL PRIMARY KEY, UnitPrice DECIMAL(10,2) NOT NULL);
    DECLARE @x XML = CAST(@Items AS XML);

    INSERT INTO @it (ProductId, UnitPrice)
    SELECT n.value('@p', 'int'), n.value('@u', 'decimal(10,2)')
    FROM @x.nodes('/items/i') AS t(n);

    IF EXISTS (SELECT 1 FROM dbo.fn_PurPrItems(@RequisitionId) f
               WHERE f.Shortage > 0
                 AND NOT EXISTS (SELECT 1 FROM @it i WHERE i.ProductId = f.ProductId AND i.UnitPrice > 0))
        THROW 50065, N'Enter a price greater than zero for every item to buy.', 1;

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
        WHERE i.ProductId IN (SELECT ProductId FROM dbo.fn_PurPrItems(@RequisitionId) WHERE Shortage > 0);

        /* keep the supplier's price list up to date (prices are preserved for future supplier comparison) */
        UPDATE sp SET UnitCost = qi.UnitPrice, QuotedDate = GETDATE()
        FROM dbo.SupplierPrices sp
        JOIN dbo.QuotationItems qi ON qi.ProductId = sp.ProductId
        WHERE sp.SupplierId = @SupplierId AND qi.QuotationId = @QuotationId;

        INSERT INTO dbo.SupplierPrices (SupplierId, ProductId, UnitCost)
        SELECT @SupplierId, qi.ProductId, qi.UnitPrice
        FROM dbo.QuotationItems qi
        WHERE qi.QuotationId = @QuotationId
          AND NOT EXISTS (SELECT 1 FROM dbo.SupplierPrices x WHERE x.SupplierId = @SupplierId AND x.ProductId = qi.ProductId);

    COMMIT;
    SELECT @QuotationId;
END
GO

/* Manager: create the APPROVED purchase order from the budget-approved purchase requisition and the CHOSEN QUOTATION.
   Same flow as script 12, plus: at least two quotations are required; prices and payment terms come from the quotation. */
CREATE OR ALTER PROCEDURE dbo.usp_PurCreateOrder
    @RequisitionId INT,
    @SupplierId    INT,
    @UserId        INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Supplier NVARCHAR(100);
    SELECT @Supplier = SupplierName FROM dbo.Suppliers WHERE SupplierId = @SupplierId AND IsActive = 1;

    IF @Supplier IS NULL
        THROW 50001, N'Please choose an active supplier.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 50006, N'Only the manager can create a purchase order. The Head approves the budget first.', 1;

    DECLARE @PrId INT, @PrReviewer INT, @PrDate DATETIME;
    SELECT @PrId = PrId, @PrReviewer = ReviewedById, @PrDate = DateReviewed
    FROM dbo.PurchaseRequisitions
    WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved';

    IF @PrId IS NULL
        THROW 50053, N'This requisition has no purchase requisition approved by the Head yet.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE RequisitionId = @RequisitionId
               AND [Status] IN (N'PendingApproval', N'Approved', N'Ordered'))
        THROW 50003, N'This requisition already has an active purchase order.', 1;
    IF (SELECT COUNT(*) FROM dbo.Quotations WHERE RequisitionId = @RequisitionId) < 2
        THROW 50066, N'Get quotations from at least two suppliers (the UK and Vietnam suppliers) before creating the purchase order.', 1;

    DECLARE @QuotationId INT, @Terms NVARCHAR(100);
    SELECT @QuotationId = QuotationId, @Terms = PaymentTerms
    FROM dbo.Quotations WHERE RequisitionId = @RequisitionId AND SupplierId = @SupplierId;

    IF @QuotationId IS NULL
        THROW 50067, N'This supplier has no quotation for the requisition.', 1;

    IF EXISTS (SELECT 1 FROM dbo.PurchaseRequisitionItems i
               WHERE i.PrId = @PrId
                 AND NOT EXISTS (SELECT 1 FROM dbo.QuotationItems qi WHERE qi.QuotationId = @QuotationId AND qi.ProductId = i.ProductId))
        THROW 50068, N'This quotation does not cover every item to buy. Update the quotation first.', 1;

    BEGIN TRAN;

        DECLARE @Prefix NVARCHAR(30) = N'PO-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next INT = (SELECT COUNT(*) FROM dbo.PurchaseOrders WITH (UPDLOCK, HOLDLOCK) WHERE PoNo LIKE @Prefix + N'%') + 1;
        DECLARE @PoNo NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.PurchaseOrders (PoNo, RequisitionId, Supplier, SupplierId, CreatedById, [Status], PaymentTerms,
                                        PurchaseRequisitionId, ReviewedById, DateReviewed)
        VALUES (@PoNo, @RequisitionId, @Supplier, @SupplierId, @UserId, N'Approved', @Terms, @PrId, @PrReviewer, @PrDate);

        DECLARE @PoId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.PurchaseOrderItems (PurchaseOrderId, ProductId, Quantity, UnitCost)
        SELECT @PoId, i.ProductId, i.ShortQty, qi.UnitPrice
        FROM dbo.PurchaseRequisitionItems i
        JOIN dbo.QuotationItems qi ON qi.QuotationId = @QuotationId AND qi.ProductId = i.ProductId
        WHERE i.PrId = @PrId;

        UPDATE dbo.PurchaseRequisitions SET [Status] = N'Ordered' WHERE PrId = @PrId;

    COMMIT;
    SELECT @PoId;
END
GO

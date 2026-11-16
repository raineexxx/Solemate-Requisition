-- =====================================================================
--  Solemate Purchasing upgrade  (run AFTER 10_Issuance.sql)
--  Closes the gaps found in the system analysis review:
--   * only the three logins (cashier, manager, head): the Manager does the warehouse and purchasing work
--   * purchase only the SHORTAGE (not the whole requisition)
--   * supplier quotations / lowest-price comparison, country, payment terms, lead time
--   * PO flow: PendingApproval -> Approved -> Ordered (sent) -> Received (by Delivery)
--   * PO is linked to Delivery; receiving a PO without inspection is blocked
--   * delivery accepted -> stock is automatically reserved for the requisition
--   * in-app notifications (Head, Requester, Manager)
--   * approval stock check uses AVAILABLE stock (stock minus reserved)
--  Safe to run more than once.
-- =====================================================================
USE SolemateKicksDB;
GO

/* ---------------------------------------------------------------
   1. ROLES: the Head approves budgets (no separate Owner role).
      The Manager acts as warehouse / purchasing officer: creates and sends POs, receives deliveries.
   --------------------------------------------------------------- */

/* ---------------------------------------------------------------
   2. SUPPLIERS: country, payment terms, lead time + QUOTATIONS
   --------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.Suppliers', N'Country') IS NULL
    ALTER TABLE dbo.Suppliers ADD Country NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.Suppliers', N'PaymentTerms') IS NULL
    ALTER TABLE dbo.Suppliers ADD PaymentTerms NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.Suppliers', N'LeadTimeDays') IS NULL
    ALTER TABLE dbo.Suppliers ADD LeadTimeDays INT NULL;
GO

UPDATE dbo.Suppliers SET Country = N'USA',     PaymentTerms = N'Net 30',                       LeadTimeDays = 21 WHERE SupplierName = N'Olem Shoe Corp' AND Country IS NULL;
UPDATE dbo.Suppliers SET Country = N'Vietnam', PaymentTerms = N'50% advance, 50% on delivery', LeadTimeDays = 30 WHERE SupplierName LIKE N'Dong Hung%' AND Country IS NULL;
UPDATE dbo.Suppliers SET Country = N'China',   PaymentTerms = N'Net 45',                       LeadTimeDays = 35 WHERE SupplierName LIKE N'Clarco%' AND Country IS NULL;
GO

IF OBJECT_ID(N'dbo.SupplierPrices') IS NULL
BEGIN
    CREATE TABLE dbo.SupplierPrices (
        SupplierId  INT NOT NULL CONSTRAINT FK_SupPrices_Supplier REFERENCES dbo.Suppliers(SupplierId) ON DELETE CASCADE,
        ProductId   INT NOT NULL CONSTRAINT FK_SupPrices_Product  REFERENCES dbo.Products(ProductId),
        UnitCost    DECIMAL(10,2) NOT NULL CONSTRAINT CK_SupPrices_Cost CHECK (UnitCost > 0),
        QuotedDate  DATETIME NOT NULL CONSTRAINT DF_SupPrices_Date DEFAULT GETDATE(),
        CONSTRAINT PK_SupplierPrices PRIMARY KEY (SupplierId, ProductId)
    );
END
GO

/* sample quotations so the comparison can be demonstrated (each supplier quotes a different % of the selling price) */
INSERT INTO dbo.SupplierPrices (SupplierId, ProductId, UnitCost)
SELECT s.SupplierId, p.ProductId,
       CAST(ROUND(p.Price * CASE WHEN s.SupplierName = N'Olem Shoe Corp' THEN 0.58
                                 WHEN s.SupplierName LIKE N'Dong Hung%'   THEN 0.52
                                 ELSE 0.55 END, 2) AS DECIMAL(10,2))
FROM dbo.Suppliers s CROSS JOIN dbo.Products p
WHERE (s.SupplierName = N'Olem Shoe Corp' OR s.SupplierName LIKE N'Dong Hung%' OR s.SupplierName LIKE N'Clarco%')
  AND p.Price > 0
  AND NOT EXISTS (SELECT 1 FROM dbo.SupplierPrices x WHERE x.SupplierId = s.SupplierId AND x.ProductId = p.ProductId);
GO

CREATE OR ALTER PROCEDURE dbo.usp_SupGetAll
    @ActiveOnly INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SupplierId,
           SupplierName,
           ISNULL([Address], N'') AS [Address],
           ISNULL(Email, N'')     AS Email,
           ISNULL(Country, N'')   AS Country,
           ISNULL(PaymentTerms, N'') AS PaymentTerms,
           ISNULL(LeadTimeDays, 0)   AS LeadTimeDays
    FROM dbo.Suppliers
    WHERE @ActiveOnly = 0 OR IsActive = 1
    ORDER BY SupplierName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SupSaveTerms
    @SupplierId INT, @Country NVARCHAR(50), @PaymentTerms NVARCHAR(100), @LeadTimeDays INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50040, N'Only the Head or the Manager can edit supplier terms.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
        THROW 50013, N'Supplier not found.', 1;
    IF @LeadTimeDays < 0 OR @LeadTimeDays > 365
        THROW 50041, N'Lead time must be between 0 and 365 days.', 1;

    UPDATE dbo.Suppliers
    SET Country = NULLIF(LTRIM(RTRIM(@Country)), N''),
        PaymentTerms = NULLIF(LTRIM(RTRIM(@PaymentTerms)), N''),
        LeadTimeDays = NULLIF(@LeadTimeDays, 0)
    WHERE SupplierId = @SupplierId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SupPriceSave
    @SupplierId INT, @ProductId INT, @UnitCost DECIMAL(10,2), @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50042, N'Only the Head or the Manager can record supplier quotations.', 1;
    IF @UnitCost IS NULL OR @UnitCost <= 0
        THROW 50043, N'The quoted unit cost must be more than zero.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
        THROW 50013, N'Supplier not found.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE ProductId = @ProductId)
        THROW 50044, N'Product not found.', 1;

    UPDATE dbo.SupplierPrices SET UnitCost = @UnitCost, QuotedDate = GETDATE()
    WHERE SupplierId = @SupplierId AND ProductId = @ProductId;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.SupplierPrices (SupplierId, ProductId, UnitCost) VALUES (@SupplierId, @ProductId, @UnitCost);
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SupPriceDelete
    @SupplierId INT, @ProductId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50042, N'Only the Head or the Manager can record supplier quotations.', 1;
    DELETE FROM dbo.SupplierPrices WHERE SupplierId = @SupplierId AND ProductId = @ProductId;
END
GO

/* @SupplierId = 0 lists the quotations of every supplier */
CREATE OR ALTER PROCEDURE dbo.usp_SupPriceList
    @SupplierId INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT sp.SupplierId, s.SupplierName, sp.ProductId, p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size,
           p.Price AS SellingPrice, sp.UnitCost, sp.QuotedDate
    FROM dbo.SupplierPrices sp
    JOIN dbo.Suppliers s ON s.SupplierId = sp.SupplierId
    JOIN dbo.Products  p ON p.ProductId  = sp.ProductId
    JOIN dbo.Brands    b ON b.BrandId    = p.BrandId
    WHERE @SupplierId = 0 OR sp.SupplierId = @SupplierId
    ORDER BY s.SupplierName, b.BrandName, p.ProductName, p.Size;
END
GO

/* ---------------------------------------------------------------
   3. PURCHASE ORDER: new columns + status flow, link to Delivery
   --------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.PurchaseOrders', N'PaymentTerms') IS NULL ALTER TABLE dbo.PurchaseOrders ADD PaymentTerms NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'ExpectedDate') IS NULL ALTER TABLE dbo.PurchaseOrders ADD ExpectedDate DATE NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'ReviewedById') IS NULL ALTER TABLE dbo.PurchaseOrders ADD ReviewedById INT NULL CONSTRAINT FK_PO_ReviewedBy REFERENCES dbo.AppUsers(UserId);
IF COL_LENGTH(N'dbo.PurchaseOrders', N'DateReviewed') IS NULL ALTER TABLE dbo.PurchaseOrders ADD DateReviewed DATETIME NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'ReviewNote')   IS NULL ALTER TABLE dbo.PurchaseOrders ADD ReviewNote NVARCHAR(200) NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'SentOn')       IS NULL ALTER TABLE dbo.PurchaseOrders ADD SentOn DATETIME NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_PO_Status')
    ALTER TABLE dbo.PurchaseOrders DROP CONSTRAINT CK_PO_Status;
GO
ALTER TABLE dbo.PurchaseOrders ADD CONSTRAINT CK_PO_Status
    CHECK ([Status] IN (N'PendingApproval', N'Approved', N'Rejected', N'Ordered', N'Received', N'Cancelled'));
GO
IF COL_LENGTH(N'dbo.Deliveries', N'PurchaseOrderId') IS NULL
    ALTER TABLE dbo.Deliveries ADD PurchaseOrderId INT NULL CONSTRAINT FK_Deliveries_PO REFERENCES dbo.PurchaseOrders(PurchaseOrderId);
GO

/* ---------------------------------------------------------------
   4. SHORTAGE = what must really be bought for a requisition
      need  = requested - already issued - already reserved
      free  = stock - reserved (never below 0)
      shortage = need - free (never below 0)
   --------------------------------------------------------------- */
CREATE OR ALTER FUNCTION dbo.fn_PurShortage (@RequisitionId INT)
RETURNS TABLE
AS
RETURN
    SELECT i.ProductId, i.Quantity AS Requested, x.Free AS FreeStock,
           CASE WHEN x.Need > x.Free THEN x.Need - x.Free ELSE 0 END AS Shortage
    FROM dbo.RequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    CROSS APPLY (SELECT i.Quantity - i.IssuedQty - i.ReservedQty AS Need,
                        CASE WHEN p.StockQty - p.ReservedQty > 0 THEN p.StockQty - p.ReservedQty ELSE 0 END AS Free) x
    WHERE i.RequisitionId = @RequisitionId;
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurGetShortage
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.ProductId, p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size,
           s.Requested, s.FreeStock, s.Shortage
    FROM dbo.fn_PurShortage(@RequisitionId) s
    JOIN dbo.Products p ON p.ProductId = s.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE s.Shortage > 0
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* Quotation comparison for the shortage of a requisition: best (complete + cheapest) supplier first */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetQuotes
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT q.* FROM (
        SELECT s.SupplierId, s.SupplierName,
               ISNULL(s.Country, N'')      AS Country,
               ISNULL(s.PaymentTerms, N'') AS PaymentTerms,
               ISNULL(s.LeadTimeDays, 0)   AS LeadTimeDays,
               (SELECT COUNT(*) FROM dbo.fn_PurShortage(@RequisitionId) f
                JOIN dbo.SupplierPrices sp ON sp.ProductId = f.ProductId AND sp.SupplierId = s.SupplierId
                WHERE f.Shortage > 0) AS Quoted,
               (SELECT COUNT(*) FROM dbo.fn_PurShortage(@RequisitionId) f WHERE f.Shortage > 0) AS Needed,
               CAST(ISNULL((SELECT SUM(f.Shortage * sp.UnitCost) FROM dbo.fn_PurShortage(@RequisitionId) f
                            JOIN dbo.SupplierPrices sp ON sp.ProductId = f.ProductId AND sp.SupplierId = s.SupplierId
                            WHERE f.Shortage > 0), 0) AS DECIMAL(12,2)) AS Total
        FROM dbo.Suppliers s
        WHERE s.IsActive = 1
    ) q
    ORDER BY CASE WHEN q.Quoted = q.Needed AND q.Needed > 0 THEN 0 ELSE 1 END, q.Total, q.SupplierName;
END
GO

/* ---------------------------------------------------------------
   5. PURCHASE ORDER PROCEDURES
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetOrders
AS
BEGIN
    SET NOCOUNT ON;
    SELECT po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status],
           CAST(ISNULL(SUM(i.Quantity * i.UnitCost), 0) AS DECIMAL(12,2)) AS Total,
           ISNULL(r.RefNo, N'')          AS RequisitionRef,
           ISNULL(po.PaymentTerms, N'')  AS PaymentTerms,
           po.ExpectedDate,
           ISNULL(po.ReviewNote, N'')    AS ReviewNote,
           u.FullName                    AS CreatedBy
    FROM dbo.PurchaseOrders po
    JOIN dbo.AppUsers u ON u.UserId = po.CreatedById
    LEFT JOIN dbo.PurchaseOrderItems i ON i.PurchaseOrderId = po.PurchaseOrderId
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = po.RequisitionId
    GROUP BY po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status], r.RefNo,
             po.PaymentTerms, po.ExpectedDate, po.ReviewNote, u.FullName
    ORDER BY po.DateCreated DESC, po.PurchaseOrderId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurGetOrderItems
    @PurchaseOrderId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size,
           i.Quantity, i.UnitCost, CAST(i.Quantity * i.UnitCost AS DECIMAL(12,2)) AS LineTotal
    FROM dbo.PurchaseOrderItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE i.PurchaseOrderId = @PurchaseOrderId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* Header data for printing / e-mailing a PO */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetPrintHeader
    @PurchaseOrderId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT po.PoNo, po.DateCreated, po.[Status], po.Supplier,
           ISNULL(s.[Address], N'') AS SupplierAddress, ISNULL(s.Email, N'') AS SupplierEmail,
           ISNULL(s.Country, N'')   AS Country,
           ISNULL(po.PaymentTerms, N'') AS PaymentTerms,
           po.ExpectedDate,
           u.FullName AS CreatedBy,
           ISNULL(a.FullName, N'')  AS ApprovedBy,
           ISNULL(r.RefNo, N'')     AS RequisitionRef,
           ISNULL(r.Branch, N'')    AS DeliverTo,
           CAST(ISNULL((SELECT SUM(Quantity * UnitCost) FROM dbo.PurchaseOrderItems WHERE PurchaseOrderId = po.PurchaseOrderId), 0) AS DECIMAL(12,2)) AS Total
    FROM dbo.PurchaseOrders po
    JOIN dbo.AppUsers u ON u.UserId = po.CreatedById
    LEFT JOIN dbo.AppUsers a ON a.UserId = po.ReviewedById
    LEFT JOIN dbo.Suppliers s ON s.SupplierId = po.SupplierId
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = po.RequisitionId
    WHERE po.PurchaseOrderId = @PurchaseOrderId;
END
GO

/* Approved requisitions that still NEED something bought and have no active PO */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetApprovedRequisitions
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.RequisitionId, r.RefNo, r.Branch, r.DateCreated,
           (SELECT COUNT(*) FROM dbo.fn_PurShortage(r.RequisitionId) s WHERE s.Shortage > 0) AS ItemCount
    FROM dbo.Requisitions r
    WHERE r.[Status] = N'Approved'
      AND EXISTS (SELECT 1 FROM dbo.fn_PurShortage(r.RequisitionId) s WHERE s.Shortage > 0)
      AND NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrders po
                      WHERE po.RequisitionId = r.RequisitionId
                        AND po.[Status] IN (N'PendingApproval', N'Approved', N'Ordered'))
    ORDER BY r.DateCreated;
END
GO

/* Create a PO for the SHORTAGE only, priced from the supplier's quotation. Starts as PendingApproval (the Head approves the budget). */
CREATE OR ALTER PROCEDURE dbo.usp_PurCreateOrder
    @RequisitionId INT,
    @SupplierId    INT,
    @UserId        INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Supplier NVARCHAR(100), @Terms NVARCHAR(100);
    SELECT @Supplier = SupplierName, @Terms = PaymentTerms
    FROM dbo.Suppliers WHERE SupplierId = @SupplierId AND IsActive = 1;

    IF @Supplier IS NULL
        THROW 50001, N'Please choose an active supplier.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 50006, N'Only the manager can create a purchase order. The Head approves the budget first.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved')
        THROW 50002, N'Only approved requisitions can be turned into a purchase order.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE RequisitionId = @RequisitionId
               AND [Status] IN (N'PendingApproval', N'Approved', N'Ordered'))
        THROW 50003, N'This requisition already has an active purchase order.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.fn_PurShortage(@RequisitionId) WHERE Shortage > 0)
        THROW 50021, N'Nothing to buy: current stock already covers this requisition. Use ISSUANCE to reserve and issue it.', 1;

    DECLARE @NoQuote NVARCHAR(30) =
        (SELECT TOP 1 p.Sku
         FROM dbo.fn_PurShortage(@RequisitionId) s
         JOIN dbo.Products p ON p.ProductId = s.ProductId
         LEFT JOIN dbo.SupplierPrices sp ON sp.SupplierId = @SupplierId AND sp.ProductId = s.ProductId
         WHERE s.Shortage > 0 AND sp.ProductId IS NULL
         ORDER BY p.Sku);
    IF @NoQuote IS NOT NULL
    BEGIN
        DECLARE @msg NVARCHAR(200) = N'This supplier has no quotation for ' + @NoQuote + N'. Record it in the QUOTATIONS tab first.';
        THROW 50022, @msg, 1;
    END

    BEGIN TRAN;

        DECLARE @Prefix NVARCHAR(30) = N'PO-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next INT = (SELECT COUNT(*) FROM dbo.PurchaseOrders WITH (UPDLOCK, HOLDLOCK) WHERE PoNo LIKE @Prefix + N'%') + 1;
        DECLARE @PoNo NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.PurchaseOrders (PoNo, RequisitionId, Supplier, SupplierId, CreatedById, [Status], PaymentTerms)
        VALUES (@PoNo, @RequisitionId, @Supplier, @SupplierId, @UserId, N'PendingApproval', @Terms);

        DECLARE @PoId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.PurchaseOrderItems (PurchaseOrderId, ProductId, Quantity, UnitCost)
        SELECT @PoId, s.ProductId, s.Shortage, sp.UnitCost
        FROM dbo.fn_PurShortage(@RequisitionId) s
        JOIN dbo.SupplierPrices sp ON sp.SupplierId = @SupplierId AND sp.ProductId = s.ProductId
        WHERE s.Shortage > 0;

    COMMIT;
    SELECT @PoId;
END
GO

/* Head: approve (budget) or reject a purchase order */
CREATE OR ALTER PROCEDURE dbo.usp_PurApprove
    @PurchaseOrderId INT, @UserId INT, @Approve BIT, @Note NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50030, N'Only the Head can approve or reject the budget of a purchase order.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId AND [Status] = N'PendingApproval')
        THROW 50031, N'Only purchase orders that are waiting for approval can be approved or rejected.', 1;

    SET @Note = NULLIF(LTRIM(RTRIM(ISNULL(@Note, N''))), N'');
    IF @Approve = 0 AND @Note IS NULL
        THROW 50032, N'Enter a reason when rejecting a purchase order.', 1;

    UPDATE dbo.PurchaseOrders
    SET [Status] = CASE WHEN @Approve = 1 THEN N'Approved' ELSE N'Rejected' END,
        ReviewedById = @UserId, DateReviewed = GETDATE(), ReviewNote = @Note
    WHERE PurchaseOrderId = @PurchaseOrderId AND [Status] = N'PendingApproval';
END
GO

/* Send the approved PO to the supplier: PO becomes Ordered and an Expected delivery is created (linked to the PO) */
CREATE OR ALTER PROCEDURE dbo.usp_PurSend
    @PurchaseOrderId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 50034, N'Only the manager can send a purchase order to the supplier.', 1;

    DECLARE @PoNo NVARCHAR(30), @SupId INT, @SupName NVARCHAR(100), @ReqId INT, @Lead INT;
    SELECT @PoNo = po.PoNo, @SupId = po.SupplierId, @SupName = po.Supplier, @ReqId = po.RequisitionId,
           @Lead = ISNULL(s.LeadTimeDays, 14)
    FROM dbo.PurchaseOrders po
    LEFT JOIN dbo.Suppliers s ON s.SupplierId = po.SupplierId
    WHERE po.PurchaseOrderId = @PurchaseOrderId AND po.[Status] = N'Approved';

    IF @PoNo IS NULL
        THROW 50033, N'Only purchase orders approved by the Head can be sent to the supplier.', 1;

    BEGIN TRAN;

        DECLARE @Exp DATE = DATEADD(DAY, @Lead, CAST(GETDATE() AS DATE));
        DECLARE @Prefix NVARCHAR(30) = N'DEL-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next INT = (SELECT COUNT(*) FROM dbo.Deliveries WITH (UPDLOCK, HOLDLOCK) WHERE DeliveryRef LIKE @Prefix + N'%') + 1;
        DECLARE @Ref NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.Deliveries (DeliveryRef, PoRef, SupplierName, SupplierId, RequisitionId, ExpectedDate,
                                    [Status], DateCreated, CreatedById, Remarks, PurchaseOrderId)
        VALUES (@Ref, @PoNo, @SupName, @SupId, @ReqId, @Exp,
                N'Expected', GETDATE(), @UserId, N'Created from ' + @PoNo, @PurchaseOrderId);

        DECLARE @DelId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.DeliveryItems (DeliveryId, ProductId, OrderedQty, UnitCost)
        SELECT @DelId, ProductId, Quantity, UnitCost FROM dbo.PurchaseOrderItems WHERE PurchaseOrderId = @PurchaseOrderId;

        UPDATE dbo.PurchaseOrders
        SET [Status] = N'Ordered', SentOn = GETDATE(), ExpectedDate = @Exp
        WHERE PurchaseOrderId = @PurchaseOrderId;

    COMMIT;

    SELECT @Ref AS DeliveryRef, @Exp AS ExpectedDate;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurCancel
    @PurchaseOrderId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50035, N'Only the Head or the Manager can cancel a purchase order.', 1;

    UPDATE dbo.PurchaseOrders SET [Status] = N'Cancelled'
    WHERE PurchaseOrderId = @PurchaseOrderId AND [Status] IN (N'PendingApproval', N'Approved', N'Rejected');

    IF @@ROWCOUNT = 0
        THROW 50036, N'Only purchase orders that are not yet sent to the supplier can be cancelled.', 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurGetPendingCount
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE [Status] = N'PendingApproval';
END
GO

/* Goods are received ONLY through the Delivery module (inspection first). The old shortcut is blocked. */
CREATE OR ALTER PROCEDURE dbo.usp_PurReceiveOrder
    @PurchaseOrderId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50020, N'Purchase orders are received through the DELIVERY module, where the goods are inspected first.', 1;
END
GO

/* ---------------------------------------------------------------
   6. NOTIFICATIONS
   --------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Notifications') IS NULL
BEGIN
    CREATE TABLE dbo.Notifications (
        NotificationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
        ToRole    NVARCHAR(20) NULL,                                                     -- everyone with this role ...
        ToUserId  INT NULL CONSTRAINT FK_Notif_User REFERENCES dbo.AppUsers(UserId),     -- ... or one person
        [Message] NVARCHAR(300) NOT NULL,
        CreatedOn DATETIME NOT NULL CONSTRAINT DF_Notif_Date DEFAULT GETDATE()
    );
END
GO
IF OBJECT_ID(N'dbo.NotificationReads') IS NULL
BEGIN
    CREATE TABLE dbo.NotificationReads (
        NotificationId INT NOT NULL CONSTRAINT FK_NotifReads_Notif REFERENCES dbo.Notifications(NotificationId) ON DELETE CASCADE,
        UserId         INT NOT NULL CONSTRAINT FK_NotifReads_User  REFERENCES dbo.AppUsers(UserId),
        CONSTRAINT PK_NotificationReads PRIMARY KEY (NotificationId, UserId)
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_NotGetMine
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (100) n.NotificationId, n.[Message], n.CreatedOn,
           CAST(CASE WHEN r.UserId IS NULL THEN 0 ELSE 1 END AS BIT) AS IsRead
    FROM dbo.Notifications n
    JOIN dbo.AppUsers u ON u.UserId = @UserId
    LEFT JOIN dbo.NotificationReads r ON r.NotificationId = n.NotificationId AND r.UserId = @UserId
    WHERE n.ToUserId = @UserId OR (n.ToUserId IS NULL AND n.ToRole = u.[Role])
    ORDER BY n.NotificationId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_NotCountUnread
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*)
    FROM dbo.Notifications n
    JOIN dbo.AppUsers u ON u.UserId = @UserId
    WHERE (n.ToUserId = @UserId OR (n.ToUserId IS NULL AND n.ToRole = u.[Role]))
      AND NOT EXISTS (SELECT 1 FROM dbo.NotificationReads r WHERE r.NotificationId = n.NotificationId AND r.UserId = @UserId);
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_NotMarkAllRead
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.NotificationReads (NotificationId, UserId)
    SELECT n.NotificationId, @UserId
    FROM dbo.Notifications n
    JOIN dbo.AppUsers u ON u.UserId = @UserId
    WHERE (n.ToUserId = @UserId OR (n.ToUserId IS NULL AND n.ToRole = u.[Role]))
      AND NOT EXISTS (SELECT 1 FROM dbo.NotificationReads r WHERE r.NotificationId = n.NotificationId AND r.UserId = @UserId);
END
GO

/* ---------------------------------------------------------------
   7. TRIGGERS (keep the existing procedures untouched)
   --------------------------------------------------------------- */
CREATE OR ALTER TRIGGER dbo.trg_Requisitions_Notify ON dbo.Requisitions AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- new / re-submitted requisition -> the Head
    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT N'Head', NULL, N'Requisition ' + i.RefNo + N' from ' + u.FullName + N' is waiting for your approval.'
    FROM inserted i
    JOIN dbo.AppUsers u ON u.UserId = i.RequestedById
    LEFT JOIN deleted d ON d.RequisitionId = i.RequisitionId
    WHERE i.[Status] = N'Pending' AND (d.RequisitionId IS NULL OR d.[Status] <> N'Pending');

    -- decision -> the requester
    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT NULL, i.RequestedById,
           N'Your requisition ' + i.RefNo + CASE WHEN i.[Status] = N'Approved' THEN N' was approved.'
               ELSE N' was rejected. Reason: ' + ISNULL(i.RejectReason, N'-') END
    FROM inserted i JOIN deleted d ON d.RequisitionId = i.RequisitionId
    WHERE i.[Status] IN (N'Approved', N'Rejected') AND d.[Status] <> i.[Status];

    -- approved -> the Manager prepares it
    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT r.ToRole, NULL, N'Requisition ' + i.RefNo + N' (' + i.Branch + N') was approved. Available stock is reserved - prepare and issue it.'
    FROM inserted i JOIN deleted d ON d.RequisitionId = i.RequisitionId
    CROSS JOIN (VALUES (N'Manager')) r (ToRole)
    WHERE i.[Status] = N'Approved' AND d.[Status] <> N'Approved';
END
GO

CREATE OR ALTER TRIGGER dbo.trg_Products_LowStock ON dbo.Products AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(StockQty) RETURN;

    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT r.ToRole, NULL,
           N'Low stock: ' + i.Sku + N' has ' + CAST(i.StockQty AS NVARCHAR(10)) + N' left (reorder level ' + CAST(i.ReorderLevel AS NVARCHAR(10)) + N').'
    FROM inserted i
    JOIN deleted d ON d.ProductId = i.ProductId
    CROSS JOIN (VALUES (N'Manager'), (N'Head')) r (ToRole)
    WHERE i.StockQty <= i.ReorderLevel AND d.StockQty > d.ReorderLevel;
END
GO

CREATE OR ALTER TRIGGER dbo.trg_Issuances_Notify ON dbo.Issuances AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT NULL, r.RequestedById, N'Stock for requisition ' + r.RefNo + N' was issued on slip ' + i.IssueNo + N'. Please acknowledge receipt.'
    FROM inserted i JOIN dbo.Requisitions r ON r.RequisitionId = i.RequisitionId;
END
GO

/* Deliveries entered by hand with a PO number are linked to that PO automatically */
CREATE OR ALTER TRIGGER dbo.trg_Deliveries_LinkPo ON dbo.Deliveries AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE d SET d.PurchaseOrderId = po.PurchaseOrderId
    FROM dbo.Deliveries d
    JOIN inserted i ON i.DeliveryId = d.DeliveryId
    JOIN dbo.PurchaseOrders po ON po.PoNo = d.PoRef
    WHERE d.PurchaseOrderId IS NULL;
END
GO

/* trg_PurchaseOrders_Notify and trg_Deliveries_Received are created by 12_PaperAlignment.sql */

/* ---------------------------------------------------------------
   8. APPROVAL STOCK CHECK uses AVAILABLE stock (stock minus reserved)
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ApproveRequisition
    @RequisitionId INT,
    @HeadId        INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @HeadId AND [Role] = N'Head')
        THROW 50020, 'Only the head can approve or reject requisitions.', 1;

    DECLARE @Status NVARCHAR(20), @RefNo NVARCHAR(30);
    SELECT @Status = [Status], @RefNo = RefNo FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId;

    IF @RefNo IS NULL
        THROW 50010, 'Requisition not found.', 1;
    IF @Status <> N'Pending'
        THROW 50021, 'This requisition is no longer pending.', 1;

    -- automatic stock check
    DECLARE @Short INT =
        (SELECT COUNT(*)
         FROM dbo.RequisitionItems i
         JOIN dbo.Products p ON p.ProductId = i.ProductId
         WHERE i.RequisitionId = @RequisitionId AND (p.StockQty - p.ReservedQty) < i.Quantity);

    DECLARE @Check NVARCHAR(200) =
        CASE WHEN @Short = 0 THEN N'Stock sufficient for all items'
             ELSE CAST(@Short AS NVARCHAR(10)) + N' item(s) short on stock - purchase requisition needed' END;

    UPDATE dbo.Requisitions
    SET [Status] = N'Approved', ReviewedById = @HeadId, DateReviewed = GETDATE(), StockCheck = @Check
    WHERE RequisitionId = @RequisitionId AND [Status] = N'Pending';

    IF @@ROWCOUNT = 0
        THROW 50021, 'This requisition is no longer pending.', 1;

    SELECT @RefNo AS RefNo,
           @Check AS StockCheck,
           p.Sku,
           p.ProductName,
           p.Size,
           i.Quantity AS Requested,
           p.StockQty - p.ReservedQty AS Available
    FROM dbo.RequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    WHERE i.RequisitionId = @RequisitionId
    ORDER BY p.Sku;
END
GO

/* ---------------------------------------------------------------
   9. CLEAN-UP: earlier version of this script created an Owner role. The Head is the owner now.
   --------------------------------------------------------------- */
DECLARE @OwnerId INT = (SELECT UserId FROM dbo.AppUsers WHERE Username = N'owner' AND [Role] = N'Owner');
DECLARE @HeadId  INT = (SELECT TOP 1 UserId FROM dbo.AppUsers WHERE [Role] = N'Head' ORDER BY UserId);
IF @OwnerId IS NOT NULL
BEGIN
    UPDATE dbo.PurchaseOrders SET ReviewedById = @HeadId WHERE ReviewedById = @OwnerId;
    DELETE FROM dbo.NotificationReads WHERE UserId = @OwnerId;
    DELETE FROM dbo.Notifications WHERE ToUserId = @OwnerId;
    DELETE FROM dbo.AppUsers WHERE UserId = @OwnerId;
END
DELETE FROM dbo.Notifications WHERE ToRole = N'Owner';
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_AppUsers_Role')
    ALTER TABLE dbo.AppUsers DROP CONSTRAINT CK_AppUsers_Role;
GO
-- (a version of this script created 'warehouse' and 'purchasing' users: they are removed, the system has 3 logins only)
DECLARE @Extra TABLE (UserId INT);
INSERT INTO @Extra SELECT UserId FROM dbo.AppUsers WHERE [Role] IN (N'Warehouse', N'Purchasing');
IF EXISTS (SELECT 1 FROM @Extra)
BEGIN
    DELETE FROM dbo.NotificationReads WHERE UserId IN (SELECT UserId FROM @Extra);
    DELETE FROM dbo.Notifications WHERE ToUserId IN (SELECT UserId FROM @Extra) OR ToRole IN (N'Warehouse', N'Purchasing');
    BEGIN TRY
        DELETE FROM dbo.AppUsers WHERE UserId IN (SELECT UserId FROM @Extra);
    END TRY
    BEGIN CATCH
        -- the user already created records: keep the history but make the account a normal Manager
        UPDATE dbo.AppUsers SET [Role] = N'Manager' WHERE UserId IN (SELECT UserId FROM @Extra);
    END CATCH
END
GO
ALTER TABLE dbo.AppUsers ADD CONSTRAINT CK_AppUsers_Role CHECK ([Role] IN (N'Cashier', N'Head', N'Manager'));
GO

/* ---------------------------------------------------------------
   10. SALTED PASSWORDS. The column becomes wide enough for "PBKDF2$iterations$salt$hash".
       Old SHA-256 passwords keep working and are upgraded automatically at the next login.
   --------------------------------------------------------------- */
ALTER TABLE dbo.AppUsers ALTER COLUMN PasswordHash NVARCHAR(200) NOT NULL;
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetUserForLogin
    @Username NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, Username, PasswordHash, FullName, [Role], Branch
    FROM dbo.AppUsers
    WHERE Username = @Username;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SetPasswordHash
    @UserId INT, @PasswordHash NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.AppUsers SET PasswordHash = @PasswordHash WHERE UserId = @UserId;
END
GO

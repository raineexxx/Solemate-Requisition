USE SolemateKicksDB;
GO

DECLARE @missing NVARCHAR(500) = N'';

IF OBJECT_ID(N'dbo.PurchaseOrders', N'U') IS NULL SET @missing = @missing + N' [PurchaseOrders table]';
IF OBJECT_ID(N'dbo.PurchaseOrderItems', N'U') IS NULL SET @missing = @missing + N' [PurchaseOrderItems table]';
IF OBJECT_ID(N'dbo.Deliveries', N'U') IS NULL SET @missing = @missing + N' [Deliveries table]';
IF OBJECT_ID(N'dbo.DeliveryItems', N'U') IS NULL SET @missing = @missing + N' [DeliveryItems table]';
IF OBJECT_ID(N'dbo.Suppliers', N'U') IS NULL SET @missing = @missing + N' [Suppliers table]';
IF COL_LENGTH(N'dbo.RequisitionItems', N'ItemStatus') IS NULL SET @missing = @missing + N' [RequisitionItems.ItemStatus]';
IF COL_LENGTH(N'dbo.RequisitionItems', N'IssuedQty') IS NULL SET @missing = @missing + N' [RequisitionItems.IssuedQty]';
IF COL_LENGTH(N'dbo.RequisitionItems', N'ReservedQty') IS NULL SET @missing = @missing + N' [RequisitionItems.ReservedQty]';
IF COL_LENGTH(N'dbo.Products', N'ReservedQty') IS NULL SET @missing = @missing + N' [Products.ReservedQty]';
IF COL_LENGTH(N'dbo.PurchaseOrders', N'SupplierId') IS NULL SET @missing = @missing + N' [PurchaseOrders.SupplierId]';
IF COL_LENGTH(N'dbo.PurchaseOrderItems', N'RequisitionItemId') IS NULL SET @missing = @missing + N' [PurchaseOrderItems.RequisitionItemId]';

IF @missing <> N''
BEGIN
    RAISERROR(N'Run the earlier scripts first. Missing:%s. Nothing was changed.', 16, 1, @missing);
    SET NOEXEC ON;  
END
GO

IF COL_LENGTH(N'dbo.Suppliers', N'Country')      IS NULL ALTER TABLE dbo.Suppliers ADD Country NVARCHAR(50) NULL;
IF COL_LENGTH(N'dbo.Suppliers', N'PaymentTerms') IS NULL ALTER TABLE dbo.Suppliers ADD PaymentTerms NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.Suppliers', N'LeadTimeDays') IS NULL ALTER TABLE dbo.Suppliers ADD LeadTimeDays INT NOT NULL CONSTRAINT DF_Suppliers_Lead DEFAULT 0;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'PaymentTerms') IS NULL ALTER TABLE dbo.PurchaseOrders ADD PaymentTerms NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'DateSent')     IS NULL ALTER TABLE dbo.PurchaseOrders ADD DateSent DATETIME NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'ExpectedDate') IS NULL ALTER TABLE dbo.PurchaseOrders ADD ExpectedDate DATE NULL;
IF COL_LENGTH(N'dbo.PurchaseOrders', N'PrId')         IS NULL ALTER TABLE dbo.PurchaseOrders ADD PrId INT NULL;
GO


IF OBJECT_ID(N'dbo.SupplierPrices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SupplierPrices', N'QuotedById') IS NULL
    ALTER TABLE dbo.SupplierPrices ADD QuotedById INT NULL;
GO

IF OBJECT_ID(N'dbo.PurchaseRequisitions', N'U') IS NOT NULL
   AND (COL_LENGTH(N'dbo.PurchaseRequisitions', N'PrId') IS NULL
        OR COL_LENGTH(N'dbo.PurchaseRequisitions', N'PrNo') IS NULL
        OR COL_LENGTH(N'dbo.PurchaseRequisitions', N'RequisitionId') IS NULL
        OR COL_LENGTH(N'dbo.PurchaseRequisitions', N'Status') IS NULL
        OR COL_LENGTH(N'dbo.PurchaseRequisitions', N'ReviewedById') IS NULL
        OR COL_LENGTH(N'dbo.PurchaseRequisitions', N'DateReviewed') IS NULL
        OR COL_LENGTH(N'dbo.PurchaseRequisitions', N'ReviewNote') IS NULL)
BEGIN
    RAISERROR(N'The table PurchaseRequisitions has an old layout. Nothing more was changed. Send the column list to your helper.', 16, 1);
    SET NOEXEC ON;
END
GO

IF OBJECT_ID(N'dbo.PurchaseRequisitionItems', N'U') IS NOT NULL
   AND (COL_LENGTH(N'dbo.PurchaseRequisitionItems', N'Id') IS NULL
        OR COL_LENGTH(N'dbo.PurchaseRequisitionItems', N'RequisitionItemId') IS NULL)
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.PurchaseRequisitionItems)
    BEGIN
        RAISERROR(N'The table PurchaseRequisitionItems has an old layout and contains rows, so it was not rebuilt. Nothing more was changed.', 16, 1);
        SET NOEXEC ON;
    END
    ELSE
        DROP TABLE dbo.PurchaseRequisitionItems;
END
GO

IF OBJECT_ID(N'dbo.PurchaseRequisitions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseRequisitions
    (
        PrId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseRequisitions PRIMARY KEY,
        PrNo          NVARCHAR(30) NOT NULL CONSTRAINT UQ_PurchaseRequisitions_No UNIQUE,   -- PR-yyyymmdd-0001
        RequisitionId INT NOT NULL CONSTRAINT FK_PR_Requisition REFERENCES dbo.Requisitions(RequisitionId),
        [Status]      NVARCHAR(20) NOT NULL CONSTRAINT DF_PR_Status DEFAULT N'Pending'
                      CONSTRAINT CK_PR_Status CHECK ([Status] IN (N'Pending', N'Approved', N'Held', N'Ordered', N'Cancelled')),
        DateCreated   DATETIME NOT NULL CONSTRAINT DF_PR_Date DEFAULT (GETDATE()),
        ReviewedById  INT NULL CONSTRAINT FK_PR_Reviewer REFERENCES dbo.AppUsers(UserId),
        DateReviewed  DATETIME NULL,
        ReviewNote    NVARCHAR(200) NULL
    );
END
GO

IF OBJECT_ID(N'dbo.PurchaseRequisitionItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseRequisitionItems
    (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseRequisitionItems PRIMARY KEY,
        PrId              INT NOT NULL CONSTRAINT FK_PRItems_PR REFERENCES dbo.PurchaseRequisitions(PrId) ON DELETE CASCADE,
        RequisitionItemId INT NOT NULL,
        ProductId         INT NOT NULL CONSTRAINT FK_PRItems_Product REFERENCES dbo.Products(ProductId),
        RequestedQty      INT NOT NULL,
        FreeQty           INT NOT NULL,   -- already reserved for this request from stock
        ShortQty          INT NOT NULL CONSTRAINT CK_PRItems_Short CHECK (ShortQty >= 1)
    );
    CREATE INDEX IX_PRItems_PR ON dbo.PurchaseRequisitionItems(PrId);
END
GO

IF OBJECT_ID(N'dbo.SupplierPrices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupplierPrices
    (
        SupplierId  INT NOT NULL CONSTRAINT FK_SupPrices_Supplier REFERENCES dbo.Suppliers(SupplierId),
        ProductId   INT NOT NULL CONSTRAINT FK_SupPrices_Product  REFERENCES dbo.Products(ProductId),
        UnitCost    DECIMAL(12,2) NOT NULL CONSTRAINT CK_SupPrices_Cost CHECK (UnitCost > 0),
        QuotedDate  DATETIME NOT NULL CONSTRAINT DF_SupPrices_Date DEFAULT (GETDATE()),
        QuotedById  INT NULL,
        CONSTRAINT PK_SupplierPrices PRIMARY KEY (SupplierId, ProductId)
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SupPriceList
    @SupplierId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT sp.SupplierId, s.SupplierName, sp.ProductId, p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size,
           CAST(p.Price AS DECIMAL(12,2)) AS SellingPrice, sp.UnitCost, sp.QuotedDate
    FROM dbo.SupplierPrices sp
    JOIN dbo.Suppliers s ON s.SupplierId = sp.SupplierId
    JOIN dbo.Products p  ON p.ProductId  = sp.ProductId
    JOIN dbo.Brands b    ON b.BrandId    = p.BrandId
    WHERE sp.SupplierId = @SupplierId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SupPriceSave
    @SupplierId INT, @ProductId INT, @UnitCost DECIMAL(12,2), @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50040, N'Only the Head can change supplier prices.', 1;
    IF @UnitCost IS NULL OR @UnitCost <= 0
        THROW 50041, N'The supplier price must be more than 0.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
        THROW 50042, N'Supplier not found.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE ProductId = @ProductId)
        THROW 50043, N'Product not found.', 1;

    UPDATE dbo.SupplierPrices SET UnitCost = @UnitCost, QuotedDate = GETDATE(), QuotedById = @UserId
    WHERE SupplierId = @SupplierId AND ProductId = @ProductId;
    IF @@ROWCOUNT = 0
        INSERT INTO dbo.SupplierPrices (SupplierId, ProductId, UnitCost, QuotedById)
        VALUES (@SupplierId, @ProductId, @UnitCost, @UserId);
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SupPriceDelete
    @SupplierId INT, @ProductId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50040, N'Only the Head can change supplier prices.', 1;
    DELETE FROM dbo.SupplierPrices WHERE SupplierId = @SupplierId AND ProductId = @ProductId;
END
GO

CREATE OR ALTER VIEW dbo.vw_PurPrQuote
AS
SELECT pr.PrId, s.SupplierId,
       COUNT(DISTINCT sp.ProductId) AS Quoted,
       COUNT(DISTINCT pi.Id)        AS Needed,
       CAST(ISNULL(SUM(sp.UnitCost * pi.ShortQty), 0) AS DECIMAL(12,2)) AS Total
FROM dbo.PurchaseRequisitions pr
CROSS JOIN dbo.Suppliers s
LEFT JOIN dbo.PurchaseRequisitionItems pi ON pi.PrId = pr.PrId
LEFT JOIN dbo.SupplierPrices sp ON sp.SupplierId = s.SupplierId AND sp.ProductId = pi.ProductId
WHERE s.IsActive = 1
GROUP BY pr.PrId, s.SupplierId;
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurEnsurePR
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @S TABLE (RequisitionItemId INT PRIMARY KEY, ProductId INT NOT NULL, Requested INT NOT NULL, Reserved INT NOT NULL, Short INT NOT NULL);

    INSERT INTO @S (RequisitionItemId, ProductId, Requested, Reserved, Short)
    SELECT i.ItemId, i.ProductId, i.Quantity, i.ReservedQty + i.IssuedQty,
           i.Quantity - i.IssuedQty - i.ReservedQty
             - ISNULL((SELECT SUM(x.Quantity)
                       FROM dbo.PurchaseOrderItems x
                       JOIN dbo.PurchaseOrders po ON po.PurchaseOrderId = x.PurchaseOrderId
                       WHERE x.RequisitionItemId = i.ItemId AND po.[Status] = N'Ordered'), 0)
    FROM dbo.RequisitionItems i
    JOIN dbo.Requisitions r ON r.RequisitionId = i.RequisitionId AND r.[Status] = N'Approved'
    WHERE i.RequisitionId = @RequisitionId AND i.ItemStatus = N'Approved';

    DELETE FROM @S WHERE Short <= 0;

    DECLARE @PrId INT = (SELECT TOP 1 PrId FROM dbo.PurchaseRequisitions
                         WHERE RequisitionId = @RequisitionId AND [Status] IN (N'Pending', N'Approved', N'Held')
                         ORDER BY PrId DESC);

    BEGIN TRAN;

        IF NOT EXISTS (SELECT 1 FROM @S)
        BEGIN
            IF @PrId IS NOT NULL
                UPDATE dbo.PurchaseRequisitions SET [Status] = N'Cancelled', ReviewNote = N'No shortage left'
                WHERE PrId = @PrId;
        END
        ELSE
        BEGIN
            IF @PrId IS NULL
            BEGIN
                DECLARE @Prefix NVARCHAR(30) = N'PR-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
                DECLARE @Next INT = (SELECT COUNT(*) FROM dbo.PurchaseRequisitions WITH (UPDLOCK, HOLDLOCK) WHERE PrNo LIKE @Prefix + N'%') + 1;
                INSERT INTO dbo.PurchaseRequisitions (PrNo, RequisitionId)
                VALUES (@Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4), @RequisitionId);
                SET @PrId = SCOPE_IDENTITY();
            END
            ELSE
                DELETE FROM dbo.PurchaseRequisitionItems WHERE PrId = @PrId;

            INSERT INTO dbo.PurchaseRequisitionItems (PrId, RequisitionItemId, ProductId, RequestedQty, FreeQty, ShortQty)
            SELECT @PrId, RequisitionItemId, ProductId, Requested, Reserved, Short FROM @S;
        END

    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_IssReserve
    @RequisitionId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 52001, 'Only the manager can reserve and issue stock.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved')
        THROW 52002, 'Only approved requisitions can be reserved.', 1;

    DECLARE @T TABLE (ItemId INT PRIMARY KEY, ProductId INT NOT NULL, Take INT NOT NULL);

    BEGIN TRAN;

        INSERT INTO @T (ItemId, ProductId, Take)
        SELECT i.ItemId, i.ProductId,
               CASE WHEN i.Quantity - i.IssuedQty - i.ReservedQty <= 0 OR p.StockQty - p.ReservedQty <= 0 THEN 0
                    WHEN i.Quantity - i.IssuedQty - i.ReservedQty < p.StockQty - p.ReservedQty
                         THEN i.Quantity - i.IssuedQty - i.ReservedQty
                    ELSE p.StockQty - p.ReservedQty END
        FROM dbo.RequisitionItems i
        JOIN dbo.Products p WITH (UPDLOCK, ROWLOCK) ON p.ProductId = i.ProductId
        WHERE i.RequisitionId = @RequisitionId AND i.ItemStatus = N'Approved';

        UPDATE p SET p.ReservedQty = p.ReservedQty + t.Take
        FROM dbo.Products p JOIN @T t ON t.ProductId = p.ProductId WHERE t.Take > 0;

        UPDATE i SET i.ReservedQty = i.ReservedQty + t.Take
        FROM dbo.RequisitionItems i JOIN @T t ON t.ItemId = i.ItemId WHERE t.Take > 0;

    COMMIT;

    EXEC dbo.usp_PurEnsurePR @RequisitionId;

    SELECT ISNULL((SELECT SUM(Take) FROM @T), 0) AS Reserved,
           (SELECT COUNT(*) FROM dbo.RequisitionItems
            WHERE RequisitionId = @RequisitionId AND ItemStatus = N'Approved'
              AND Quantity - IssuedQty - ReservedQty > 0) AS ShortLines;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurGetPRs
AS
BEGIN
    SET NOCOUNT ON;
    SELECT pr.PrId, pr.PrNo, pr.RequisitionId, r.RefNo AS RequisitionRef, r.Branch, pr.DateCreated, pr.[Status],
           (SELECT COUNT(*) FROM dbo.PurchaseRequisitionItems x WHERE x.PrId = pr.PrId) AS Items,
           ISNULL((SELECT SUM(x.ShortQty) FROM dbo.PurchaseRequisitionItems x WHERE x.PrId = pr.PrId), 0) AS Pairs,
           (SELECT MIN(q.Total) FROM dbo.vw_PurPrQuote q WHERE q.PrId = pr.PrId AND q.Needed > 0 AND q.Quoted = q.Needed) AS EstimatedBudget,
           ISNULL(pr.ReviewNote, N'') AS ReviewNote,
           ISNULL(u.FullName, N'')    AS ReviewedBy
    FROM dbo.PurchaseRequisitions pr
    JOIN dbo.Requisitions r ON r.RequisitionId = pr.RequisitionId
    LEFT JOIN dbo.AppUsers u ON u.UserId = pr.ReviewedById
    ORDER BY CASE pr.[Status] WHEN N'Pending' THEN 0 WHEN N'Approved' THEN 1 WHEN N'Held' THEN 2 ELSE 3 END,
             pr.DateCreated DESC, pr.PrId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurGetPRItems
    @PrId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size,
           pi.RequestedQty, pi.FreeQty, pi.ShortQty
    FROM dbo.PurchaseRequisitionItems pi
    JOIN dbo.Products p ON p.ProductId = pi.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE pi.PrId = @PrId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurGetPendingCount
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) FROM dbo.PurchaseRequisitions WHERE [Status] = N'Pending';
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurDecidePR
    @PrId INT, @UserId INT, @Approve INT, @Note NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Note = LTRIM(RTRIM(ISNULL(@Note, N'')));

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50050, N'Only the Head can approve or hold a purchase requisition.', 1;

    DECLARE @ReqId INT;
    SELECT @ReqId = RequisitionId FROM dbo.PurchaseRequisitions WHERE PrId = @PrId AND [Status] IN (N'Pending', N'Held');
    IF @ReqId IS NULL
        THROW 50051, N'Only a pending or held purchase requisition can be decided.', 1;

    IF @Approve = 0 AND @Note = N''
        THROW 50052, N'Enter the reason for holding the purchase.', 1;

    BEGIN TRAN;

        UPDATE dbo.PurchaseRequisitions
        SET [Status] = CASE WHEN @Approve = 1 THEN N'Approved' ELSE N'Held' END,
            ReviewedById = @UserId, DateReviewed = GETDATE(), ReviewNote = NULLIF(@Note, N'')
        WHERE PrId = @PrId;

        IF @Approve = 0
            UPDATE dbo.Requisitions SET StockCheck = LEFT(N'Purchase on hold: ' + @Note, 120) WHERE RequisitionId = @ReqId;

    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurGetQuotes
    @PrId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.SupplierId, s.SupplierName,
           ISNULL(s.Country, N'')      AS Country,
           ISNULL(s.PaymentTerms, N'') AS PaymentTerms,
           ISNULL(s.LeadTimeDays, 0)   AS LeadTimeDays,
           q.Quoted, q.Needed, q.Total
    FROM dbo.vw_PurPrQuote q
    JOIN dbo.Suppliers s ON s.SupplierId = q.SupplierId
    WHERE q.PrId = @PrId
    ORDER BY CASE WHEN q.Needed > 0 AND q.Quoted = q.Needed THEN 0 ELSE 1 END,
             CASE WHEN q.Quoted = q.Needed THEN q.Total END,
             q.Quoted DESC, s.SupplierName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurCreateOrderFromPr
    @PrId INT, @SupplierId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50006, N'Only the Head can create a purchase order.', 1;

    DECLARE @ReqId INT;
    SELECT @ReqId = RequisitionId FROM dbo.PurchaseRequisitions WHERE PrId = @PrId AND [Status] = N'Approved';
    IF @ReqId IS NULL
        THROW 50060, N'The purchase requisition must be approved (budget) before a purchase order is created.', 1;

    DECLARE @Supplier NVARCHAR(100), @Terms NVARCHAR(100), @Lead INT;
    SELECT @Supplier = SupplierName, @Terms = NULLIF(PaymentTerms, N''), @Lead = ISNULL(LeadTimeDays, 0)
    FROM dbo.Suppliers WHERE SupplierId = @SupplierId AND IsActive = 1;
    IF @Supplier IS NULL
        THROW 50001, N'Please choose an active supplier.', 1;

    DECLARE @Missing INT =
        (SELECT COUNT(*) FROM dbo.PurchaseRequisitionItems pi
         WHERE pi.PrId = @PrId
           AND NOT EXISTS (SELECT 1 FROM dbo.SupplierPrices sp WHERE sp.SupplierId = @SupplierId AND sp.ProductId = pi.ProductId));
    IF @Missing > 0
        THROW 50061, N'This supplier has no price for some of the items. Add the prices in the SUPPLIERS tab or choose another supplier.', 1;

    BEGIN TRAN;

        DECLARE @seq INT = (SELECT COUNT(*) FROM dbo.PurchaseOrders WITH (UPDLOCK, HOLDLOCK)
                            WHERE CAST(DateCreated AS DATE) = CAST(GETDATE() AS DATE)) + 1;
        DECLARE @PoNo NVARCHAR(30) = N'PO-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-' + RIGHT(N'0000' + CAST(@seq AS NVARCHAR(10)), 4);

        INSERT INTO dbo.PurchaseOrders (PoNo, RequisitionId, Supplier, SupplierId, CreatedById, PaymentTerms, ExpectedDate, PrId)
        VALUES (@PoNo, @ReqId, @Supplier, @SupplierId, @UserId, @Terms, CAST(DATEADD(DAY, @Lead, GETDATE()) AS DATE), @PrId);

        DECLARE @PoId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.PurchaseOrderItems (PurchaseOrderId, ProductId, Quantity, UnitCost, RequisitionItemId)
        SELECT @PoId, pi.ProductId, pi.ShortQty, sp.UnitCost, pi.RequisitionItemId
        FROM dbo.PurchaseRequisitionItems pi
        JOIN dbo.SupplierPrices sp ON sp.SupplierId = @SupplierId AND sp.ProductId = pi.ProductId
        WHERE pi.PrId = @PrId;

        UPDATE dbo.PurchaseRequisitions SET [Status] = N'Ordered' WHERE PrId = @PrId;

    COMMIT;

    SELECT @PoId;
END
GO

/* ---------- purchase orders ---------- */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetOrders
AS
BEGIN
    SET NOCOUNT ON;
    SELECT po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status],
           CAST(ISNULL((SELECT SUM(i.Quantity * i.UnitCost) FROM dbo.PurchaseOrderItems i WHERE i.PurchaseOrderId = po.PurchaseOrderId), 0) AS DECIMAL(12,2)) AS Total,
           ISNULL(r.RefNo, N'')          AS RequisitionRef,
           ISNULL(po.PaymentTerms, N'')  AS PaymentTerms,
           po.ExpectedDate,
           ISNULL(pr.ReviewNote, N'')    AS ReviewNote,
           ISNULL(u.FullName, N'')       AS CreatedBy,
           ISNULL(pr.PrNo, N'')          AS PrNo,
           po.DateSent
    FROM dbo.PurchaseOrders po
    LEFT JOIN dbo.PurchaseRequisitions pr ON pr.PrId = po.PrId
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = ISNULL(pr.RequisitionId, po.RequisitionId)
    LEFT JOIN dbo.AppUsers u ON u.UserId = po.CreatedById
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

CREATE OR ALTER PROCEDURE dbo.usp_PurGetPrintHeader
    @PurchaseOrderId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT po.PoNo, po.DateCreated, po.[Status], po.Supplier,
           ISNULL(s.[Address], N'')      AS SupplierAddress,
           ISNULL(s.Email, N'')          AS SupplierEmail,
           ISNULL(s.Country, N'')        AS Country,
           ISNULL(po.PaymentTerms, N'')  AS PaymentTerms,
           po.ExpectedDate,
           ISNULL(u.FullName, N'')       AS CreatedBy,
           ISNULL(a.FullName, N'')       AS ApprovedBy,
           ISNULL(r.RefNo, N'')          AS RequisitionRef,
           ISNULL(r.Branch, N'')         AS DeliverTo,
           CAST(ISNULL((SELECT SUM(i.Quantity * i.UnitCost) FROM dbo.PurchaseOrderItems i WHERE i.PurchaseOrderId = po.PurchaseOrderId), 0) AS DECIMAL(12,2)) AS Total
    FROM dbo.PurchaseOrders po
    LEFT JOIN dbo.Suppliers s ON s.SupplierId = po.SupplierId
    LEFT JOIN dbo.PurchaseRequisitions pr ON pr.PrId = po.PrId
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = ISNULL(pr.RequisitionId, po.RequisitionId)
    LEFT JOIN dbo.AppUsers u ON u.UserId = po.CreatedById
    LEFT JOIN dbo.AppUsers a ON a.UserId = pr.ReviewedById
    WHERE po.PurchaseOrderId = @PurchaseOrderId;
END
GO

/* Head only: the written PO was sent to the supplier. Records the date and creates the EXPECTED DELIVERY
   (linked to the PO) that the Manager receives and inspects in the Delivery module. */
CREATE OR ALTER PROCEDURE dbo.usp_PurSend
    @PurchaseOrderId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50070, N'Only the Head can send a purchase order to the supplier.', 1;

    DECLARE @PoNo NVARCHAR(30), @Supplier NVARCHAR(100), @SupplierId INT, @ReqId INT, @Sent DATETIME;
    SELECT @PoNo = PoNo, @Supplier = Supplier, @SupplierId = SupplierId, @ReqId = RequisitionId, @Sent = DateSent
    FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId AND [Status] = N'Ordered';

    IF @PoNo IS NULL THROW 50071, N'Only a purchase order with status Ordered can be sent.', 1;
    IF @Sent IS NOT NULL THROW 50072, N'This purchase order was already sent to the supplier.', 1;

    DECLARE @Lead INT = ISNULL((SELECT LeadTimeDays FROM dbo.Suppliers WHERE SupplierId = @SupplierId), 0);
    DECLARE @Expected DATE = CAST(DATEADD(DAY, @Lead, GETDATE()) AS DATE);
    DECLARE @DelRef NVARCHAR(30), @DelId INT;

    BEGIN TRAN;

        UPDATE dbo.PurchaseOrders SET DateSent = GETDATE(), ExpectedDate = @Expected WHERE PurchaseOrderId = @PurchaseOrderId;

        DECLARE @Prefix NVARCHAR(30) = N'DEL-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next INT = (SELECT COUNT(*) FROM dbo.Deliveries WITH (UPDLOCK, HOLDLOCK) WHERE DeliveryRef LIKE @Prefix + N'%') + 1;
        SET @DelRef = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.Deliveries (DeliveryRef, PoRef, SupplierName, RequisitionId, CreatedById)
        VALUES (@DelRef, @PoNo, @Supplier, @ReqId, @UserId);
        SET @DelId = SCOPE_IDENTITY();

        INSERT INTO dbo.DeliveryItems (DeliveryId, ProductId, OrderedQty)
        SELECT @DelId, ProductId, SUM(Quantity)
        FROM dbo.PurchaseOrderItems WHERE PurchaseOrderId = @PurchaseOrderId
        GROUP BY ProductId;

        -- optional columns that a newer Delivery script may have added
        IF COL_LENGTH(N'dbo.Deliveries', N'PurchaseOrderId') IS NOT NULL
            EXEC sp_executesql N'UPDATE dbo.Deliveries SET PurchaseOrderId = @p WHERE DeliveryId = @d',
                 N'@p INT, @d INT', @p = @PurchaseOrderId, @d = @DelId;
        IF COL_LENGTH(N'dbo.Deliveries', N'ExpectedDate') IS NOT NULL
            EXEC sp_executesql N'UPDATE dbo.Deliveries SET ExpectedDate = @e WHERE DeliveryId = @d',
                 N'@e DATE, @d INT', @e = @Expected, @d = @DelId;

    COMMIT;

    SELECT @DelRef AS DeliveryRef, CAST(@Expected AS DATETIME) AS ExpectedDate;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurCancel
    @PurchaseOrderId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50080, N'Only the Head can cancel a purchase order.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId AND [Status] = N'Ordered')
        THROW 50081, N'Only a purchase order with status Ordered can be cancelled.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId AND DateSent IS NOT NULL)
        THROW 50082, N'This purchase order was already sent to the supplier and cannot be cancelled here.', 1;

    BEGIN TRAN;
        UPDATE dbo.PurchaseOrders SET [Status] = N'Cancelled' WHERE PurchaseOrderId = @PurchaseOrderId;
        UPDATE dbo.PurchaseRequisitions SET [Status] = N'Approved'
        WHERE PrId = (SELECT PrId FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId) AND [Status] = N'Ordered';
    COMMIT;
END
GO

SET NOEXEC OFF;
GO

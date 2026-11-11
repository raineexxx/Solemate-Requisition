-- =====================================================================
--  Solemate Delivery module
--  Run AFTER 1_Database.sql, 2_StoredProcedures.sql and 3_Inventory.sql
--  (accepted goods are added to Products.StockQty and logged in StockMovements).
--  Adds 5 tables + 9 stored procedures (usp_Del...). Does not change any existing
--  table or procedure. Safe to run more than once.
--
--  Flow (system analysis 3.4):
--   1. Manager records an EXPECTED delivery (supplier, PO reference, items ordered).
--   2. Goods arrive -> manager inspects: accepted / rejected quantities per item.
--   3. Accepted  -> stock goes up automatically + a RECEIVING REPORT is created.
--      Rejected  -> logged as a discrepancy, NOT added to stock (unavailable for issuance).
--      Missing   -> stays OUTSTANDING for follow-up; can be closed as SHORT (logged).
-- =====================================================================
USE SolemateKicksDB;
GO

IF OBJECT_ID('dbo.StockMovements', 'U') IS NULL
BEGIN
    RAISERROR('Run 3_Inventory.sql first: the StockMovements table was not found.', 16, 1);
    SET NOEXEC ON;   -- skips the rest of this script
END
GO

-- ---------------------------------------------------------------------
--  TABLES
-- ---------------------------------------------------------------------
IF OBJECT_ID('dbo.Deliveries', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Deliveries
    (
        DeliveryId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Deliveries PRIMARY KEY,
        DeliveryRef   NVARCHAR(30)  NOT NULL CONSTRAINT UQ_Deliveries_Ref UNIQUE,      -- DEL-yyyyMMdd-0001
        PoRef         NVARCHAR(30)  NULL,                                              -- purchase order reference (Purchasing module)
        SupplierName  NVARCHAR(100) NOT NULL,
        RequisitionId INT NULL CONSTRAINT FK_Deliveries_Req REFERENCES dbo.Requisitions(RequisitionId),
        [Status]      NVARCHAR(20)  NOT NULL CONSTRAINT DF_Deliveries_Status DEFAULT N'Expected'
                      CONSTRAINT CK_Deliveries_Status CHECK ([Status] IN (N'Expected', N'Partial', N'Complete', N'Short')),
        DateCreated   DATETIME      NOT NULL CONSTRAINT DF_Deliveries_Date DEFAULT (GETDATE()),
        CreatedById   INT           NOT NULL CONSTRAINT FK_Deliveries_CreatedBy REFERENCES dbo.AppUsers(UserId),
        DateReceived  DATETIME      NULL,
        Remarks       NVARCHAR(200) NULL
    );
END
GO

IF OBJECT_ID('dbo.DeliveryItems', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DeliveryItems
    (
        ItemId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeliveryItems PRIMARY KEY,
        DeliveryId  INT NOT NULL CONSTRAINT FK_DelItems_Delivery REFERENCES dbo.Deliveries(DeliveryId) ON DELETE CASCADE,
        ProductId   INT NOT NULL CONSTRAINT FK_DelItems_Product  REFERENCES dbo.Products(ProductId),
        OrderedQty  INT NOT NULL CONSTRAINT CK_DelItems_Ordered CHECK (OrderedQty >= 1),
        AcceptedQty INT NOT NULL CONSTRAINT DF_DelItems_Accepted DEFAULT 0 CONSTRAINT CK_DelItems_Accepted CHECK (AcceptedQty >= 0),
        RejectedQty INT NOT NULL CONSTRAINT DF_DelItems_Rejected DEFAULT 0 CONSTRAINT CK_DelItems_Rejected CHECK (RejectedQty >= 0),
        Note        NVARCHAR(200) NULL
    );
    CREATE INDEX IX_DelItems_Delivery ON dbo.DeliveryItems(DeliveryId);
END
GO

IF OBJECT_ID('dbo.ReceivingReports', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReceivingReports
    (
        ReportId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReceivingReports PRIMARY KEY,
        ReportRef     NVARCHAR(30) NOT NULL CONSTRAINT UQ_ReceivingReports_Ref UNIQUE,   -- RR-yyyyMMdd-0001
        DeliveryId    INT NOT NULL CONSTRAINT FK_RR_Delivery REFERENCES dbo.Deliveries(DeliveryId),
        DateReceived  DATETIME NOT NULL CONSTRAINT DF_RR_Date DEFAULT (GETDATE()),
        ReceivedById  INT NOT NULL CONSTRAINT FK_RR_User REFERENCES dbo.AppUsers(UserId),
        TotalAccepted INT NOT NULL,
        TotalRejected INT NOT NULL,
        Remarks       NVARCHAR(200) NULL
    );
END
GO

IF OBJECT_ID('dbo.ReceivingReportItems', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReceivingReportItems
    (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReceivingReportItems PRIMARY KEY,
        ReportId    INT NOT NULL CONSTRAINT FK_RRItems_Report REFERENCES dbo.ReceivingReports(ReportId) ON DELETE CASCADE,
        ItemId      INT NOT NULL,
        ProductId   INT NOT NULL CONSTRAINT FK_RRItems_Product REFERENCES dbo.Products(ProductId),
        AcceptedQty INT NOT NULL,
        RejectedQty INT NOT NULL,
        Note        NVARCHAR(200) NULL
    );
END
GO

IF OBJECT_ID('dbo.DeliveryDiscrepancies', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DeliveryDiscrepancies
    (
        DiscrepancyId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeliveryDiscrepancies PRIMARY KEY,
        DeliveryId      INT NOT NULL CONSTRAINT FK_Disc_Delivery REFERENCES dbo.Deliveries(DeliveryId),
        ItemId          INT NOT NULL,
        ProductId       INT NOT NULL CONSTRAINT FK_Disc_Product REFERENCES dbo.Products(ProductId),
        DiscrepancyType NVARCHAR(20) NOT NULL CONSTRAINT CK_Disc_Type CHECK (DiscrepancyType IN (N'Rejected', N'Short')),
        Qty             INT NOT NULL,
        Note            NVARCHAR(200) NULL,
        DateLogged      DATETIME NOT NULL CONSTRAINT DF_Disc_Date DEFAULT (GETDATE()),
        LoggedById      INT NULL CONSTRAINT FK_Disc_User REFERENCES dbo.AppUsers(UserId)
    );
END
GO

-- ---------------------------------------------------------------------
--  Pick lists for the "New delivery" screen
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_DelGetProducts
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.ProductId,
           p.Sku,
           p.Sku + N' - ' + b.BrandName + N' ' + p.ProductName + N' (US ' + CAST(p.Size AS NVARCHAR(10)) + N')' AS Display,
           p.StockQty AS Stock
    FROM dbo.Products p
    JOIN dbo.Brands b ON b.BrandId = p.BrandId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DelGetApprovedRequisitions
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.RequisitionId,
           r.RefNo,
           r.RefNo + N' - ' + u.FullName + N' (' + r.Branch + N')'
             + CASE WHEN EXISTS (SELECT 1 FROM dbo.RequisitionItems i
                                 JOIN dbo.Products p ON p.ProductId = i.ProductId
                                 WHERE i.RequisitionId = r.RequisitionId AND p.StockQty < i.Quantity)
                    THEN N'  [stock short]' ELSE N'' END AS Display,
           CASE WHEN EXISTS (SELECT 1 FROM dbo.RequisitionItems i
                             JOIN dbo.Products p ON p.ProductId = i.ProductId
                             WHERE i.RequisitionId = r.RequisitionId AND p.StockQty < i.Quantity)
                THEN 1 ELSE 0 END AS HasShortage
    FROM dbo.Requisitions r
    JOIN dbo.AppUsers u ON u.UserId = r.RequestedById
    WHERE r.[Status] = N'Approved'
    ORDER BY r.DateCreated DESC, r.RequisitionId DESC;
END
GO

-- Items of an approved requisition that stock cannot cover (requested - on hand)
CREATE OR ALTER PROCEDURE dbo.usp_DelGetShortage
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.ProductId,
           p.Sku,
           p.Sku + N' - ' + b.BrandName + N' ' + p.ProductName + N' (US ' + CAST(p.Size AS NVARCHAR(10)) + N')' AS Display,
           i.Quantity - p.StockQty AS Shortage,
           p.StockQty AS Stock
    FROM dbo.RequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE i.RequisitionId = @RequisitionId AND p.StockQty < i.Quantity
    ORDER BY p.Sku;
END
GO

-- ---------------------------------------------------------------------
--  Record an expected delivery (manager only).
--  @ItemsXml: <Items><Item ProductId="5" Quantity="10"/>...</Items>
--  @RequisitionId = 0 means "not linked to a requisition".
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_DelCreate
    @UserId        INT,
    @SupplierName  NVARCHAR(100),
    @PoRef         NVARCHAR(30),
    @RequisitionId INT,
    @Remarks       NVARCHAR(200),
    @ItemsXml      NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 51001, 'Only the manager can record deliveries.', 1;

    SET @SupplierName = LTRIM(RTRIM(ISNULL(@SupplierName, N'')));
    IF @SupplierName = N''
        THROW 51002, 'Supplier is required.', 1;

    IF @RequisitionId <> 0 AND NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved')
        THROW 51003, 'The linked requisition must be an approved requisition.', 1;

    DECLARE @x XML = CAST(@ItemsXml AS XML);
    DECLARE @Items TABLE (ProductId INT NOT NULL PRIMARY KEY, Quantity INT NOT NULL);

    INSERT INTO @Items (ProductId, Quantity)
    SELECT ProductId, SUM(Quantity)
    FROM (SELECT t.i.value('@ProductId', 'int') AS ProductId,
                 t.i.value('@Quantity',  'int') AS Quantity
          FROM @x.nodes('/Items/Item') AS t(i)) s
    GROUP BY ProductId;

    IF NOT EXISTS (SELECT 1 FROM @Items)
        THROW 51004, 'Add at least one item to the delivery.', 1;
    IF EXISTS (SELECT 1 FROM @Items WHERE Quantity < 1)
        THROW 51005, 'Every ordered quantity must be at least 1.', 1;
    IF EXISTS (SELECT 1 FROM @Items i LEFT JOIN dbo.Products p ON p.ProductId = i.ProductId WHERE p.ProductId IS NULL)
        THROW 51006, 'One of the selected products no longer exists.', 1;

    BEGIN TRAN;

        DECLARE @Prefix NVARCHAR(30) = N'DEL-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next   INT = (SELECT COUNT(*) FROM dbo.Deliveries WITH (UPDLOCK, HOLDLOCK)
                               WHERE DeliveryRef LIKE @Prefix + N'%') + 1;
        DECLARE @Ref    NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.Deliveries (DeliveryRef, PoRef, SupplierName, RequisitionId, [Status], DateCreated, CreatedById, Remarks)
        VALUES (@Ref, NULLIF(LTRIM(RTRIM(@PoRef)), N''), @SupplierName, NULLIF(@RequisitionId, 0),
                N'Expected', GETDATE(), @UserId, NULLIF(LTRIM(RTRIM(@Remarks)), N''));

        DECLARE @Id INT = SCOPE_IDENTITY();

        INSERT INTO dbo.DeliveryItems (DeliveryId, ProductId, OrderedQty)
        SELECT @Id, ProductId, Quantity FROM @Items;

    COMMIT;

    SELECT @Ref AS DeliveryRef;
END
GO

-- ---------------------------------------------------------------------
--  List of deliveries (screen filters by status in the app)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_DelGetList
    @Status NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT d.DeliveryId,
           d.DeliveryRef,
           ISNULL(d.PoRef, N'')   AS PoRef,
           d.SupplierName,
           ISNULL(r.RefNo, N'')   AS RequisitionRef,
           d.[Status],
           d.DateCreated,
           d.DateReceived,
           ISNULL(t.Lines, 0)     AS Lines,
           ISNULL(t.Ordered, 0)   AS OrderedQty,
           ISNULL(t.Accepted, 0)  AS AcceptedQty,
           ISNULL(t.Rejected, 0)  AS RejectedQty,
           ISNULL(t.Ordered, 0) - ISNULL(t.Accepted, 0) AS OutstandingQty,
           u.FullName             AS CreatedBy,
           d.Remarks
    FROM dbo.Deliveries d
    JOIN dbo.AppUsers u ON u.UserId = d.CreatedById
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = d.RequisitionId
    LEFT JOIN (SELECT DeliveryId, COUNT(*) AS Lines, SUM(OrderedQty) AS Ordered,
                      SUM(AcceptedQty) AS Accepted, SUM(RejectedQty) AS Rejected
               FROM dbo.DeliveryItems GROUP BY DeliveryId) t ON t.DeliveryId = d.DeliveryId
    WHERE @Status IS NULL OR @Status = N'All' OR d.[Status] = @Status
    ORDER BY d.DateCreated DESC, d.DeliveryId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DelGetItems
    @DeliveryId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT di.ItemId,
           di.ProductId,
           p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           di.OrderedQty,
           di.AcceptedQty,
           di.RejectedQty,
           di.OrderedQty - di.AcceptedQty AS OutstandingQty,
           ISNULL(di.Note, N'') AS Note
    FROM dbo.DeliveryItems di
    JOIN dbo.Products p ON p.ProductId = di.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE di.DeliveryId = @DeliveryId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

-- ---------------------------------------------------------------------
--  Receive and inspect (manager only)
--  @LinesXml: <Lines><Line ItemId="3" Accepted="8" Rejected="2" Note="damaged box"/>...</Lines>
--  @CloseShort = 1 : nothing more is coming, close the delivery as SHORT.
--  Accepted qty -> stock + StockMovements + receiving report.
--  Rejected qty -> discrepancy log only (never added to stock).
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_DelReceive
    @DeliveryId INT,
    @UserId     INT,
    @LinesXml   NVARCHAR(MAX),
    @CloseShort INT,
    @Remarks    NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 51001, 'Only the manager can record deliveries.', 1;

    DECLARE @Status NVARCHAR(20), @DelRef NVARCHAR(30), @ReqId INT;
    SELECT @Status = [Status], @DelRef = DeliveryRef, @ReqId = RequisitionId
    FROM dbo.Deliveries WHERE DeliveryId = @DeliveryId;

    IF @DelRef IS NULL
        THROW 51011, 'The delivery was not found.', 1;
    IF @Status NOT IN (N'Expected', N'Partial')
        THROW 51012, 'This delivery is already closed.', 1;

    DECLARE @x XML = CAST(@LinesXml AS XML);
    DECLARE @Lines TABLE (ItemId INT NOT NULL PRIMARY KEY, Accepted INT NOT NULL, Rejected INT NOT NULL, Note NVARCHAR(200) NULL);

    INSERT INTO @Lines (ItemId, Accepted, Rejected, Note)
    SELECT t.i.value('@ItemId',   'int'),
           t.i.value('@Accepted', 'int'),
           t.i.value('@Rejected', 'int'),
           NULLIF(LTRIM(RTRIM(t.i.value('@Note', 'nvarchar(200)'))), N'')
    FROM @x.nodes('/Lines/Line') AS t(i);

    IF EXISTS (SELECT 1 FROM @Lines WHERE Accepted < 0 OR Rejected < 0)
        THROW 51014, 'Quantities cannot be negative.', 1;
    IF EXISTS (SELECT 1 FROM @Lines l
               LEFT JOIN dbo.DeliveryItems di ON di.ItemId = l.ItemId AND di.DeliveryId = @DeliveryId
               WHERE di.ItemId IS NULL)
        THROW 51015, 'A line does not belong to this delivery.', 1;
    IF EXISTS (SELECT 1 FROM @Lines l
               JOIN dbo.DeliveryItems di ON di.ItemId = l.ItemId
               WHERE l.Accepted > di.OrderedQty - di.AcceptedQty)
        THROW 51016, 'Accepted quantity is more than the outstanding quantity.', 1;
    IF EXISTS (SELECT 1 FROM @Lines WHERE Rejected > 0 AND Note IS NULL)
        THROW 51017, 'Add a note (condition / reason) for every rejected item.', 1;

    DECLARE @TotalAccepted INT = (SELECT ISNULL(SUM(Accepted), 0) FROM @Lines);
    DECLARE @TotalRejected INT = (SELECT ISNULL(SUM(Rejected), 0) FROM @Lines);

    IF @TotalAccepted = 0 AND @TotalRejected = 0 AND @CloseShort = 0
        THROW 51013, 'Enter an accepted or rejected quantity for at least one item (or close the delivery as short).', 1;

    DECLARE @ReportRef NVARCHAR(30) = N'';
    DECLARE @Outstanding INT, @NewStatus NVARCHAR(20);

    BEGIN TRAN;

        -- 1) per-item totals
        UPDATE di
        SET di.AcceptedQty = di.AcceptedQty + l.Accepted,
            di.RejectedQty = di.RejectedQty + l.Rejected,
            di.Note        = ISNULL(l.Note, di.Note)
        FROM dbo.DeliveryItems di
        JOIN @Lines l ON l.ItemId = di.ItemId
        WHERE di.DeliveryId = @DeliveryId;

        -- 2) accepted goods go into stock (and are logged for the inventory history)
        UPDATE p
        SET p.StockQty = p.StockQty + l.Accepted
        FROM dbo.Products p
        JOIN dbo.DeliveryItems di ON di.ProductId = p.ProductId
        JOIN @Lines l ON l.ItemId = di.ItemId
        WHERE l.Accepted > 0 AND di.DeliveryId = @DeliveryId;

        INSERT INTO dbo.StockMovements (ProductId, QtyChange, StockAfter, Reason, CreatedById)
        SELECT di.ProductId, l.Accepted, p.StockQty, N'Delivery ' + @DelRef, @UserId
        FROM @Lines l
        JOIN dbo.DeliveryItems di ON di.ItemId = l.ItemId
        JOIN dbo.Products p ON p.ProductId = di.ProductId
        WHERE l.Accepted > 0;

        -- 3) rejected goods: logged, NOT added to stock
        INSERT INTO dbo.DeliveryDiscrepancies (DeliveryId, ItemId, ProductId, DiscrepancyType, Qty, Note, LoggedById)
        SELECT @DeliveryId, l.ItemId, di.ProductId, N'Rejected', l.Rejected, l.Note, @UserId
        FROM @Lines l
        JOIN dbo.DeliveryItems di ON di.ItemId = l.ItemId
        WHERE l.Rejected > 0;

        -- 4) receiving report (only when something was accepted)
        IF @TotalAccepted > 0
        BEGIN
            DECLARE @Prefix NVARCHAR(30) = N'RR-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
            DECLARE @Next   INT = (SELECT COUNT(*) FROM dbo.ReceivingReports WITH (UPDLOCK, HOLDLOCK)
                                   WHERE ReportRef LIKE @Prefix + N'%') + 1;
            SET @ReportRef = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

            INSERT INTO dbo.ReceivingReports (ReportRef, DeliveryId, DateReceived, ReceivedById, TotalAccepted, TotalRejected, Remarks)
            VALUES (@ReportRef, @DeliveryId, GETDATE(), @UserId, @TotalAccepted, @TotalRejected, NULLIF(LTRIM(RTRIM(@Remarks)), N''));

            DECLARE @ReportId INT = SCOPE_IDENTITY();

            INSERT INTO dbo.ReceivingReportItems (ReportId, ItemId, ProductId, AcceptedQty, RejectedQty, Note)
            SELECT @ReportId, l.ItemId, di.ProductId, l.Accepted, l.Rejected, l.Note
            FROM @Lines l
            JOIN dbo.DeliveryItems di ON di.ItemId = l.ItemId
            WHERE l.Accepted > 0 OR l.Rejected > 0;
        END

        -- 5) outstanding quantity and new status
        SET @Outstanding = (SELECT ISNULL(SUM(OrderedQty - AcceptedQty), 0) FROM dbo.DeliveryItems WHERE DeliveryId = @DeliveryId);
        SET @NewStatus = CASE WHEN @Outstanding = 0 THEN N'Complete'
                              WHEN @CloseShort = 1  THEN N'Short'
                              ELSE N'Partial' END;

        IF @NewStatus = N'Short'
            INSERT INTO dbo.DeliveryDiscrepancies (DeliveryId, ItemId, ProductId, DiscrepancyType, Qty, Note, LoggedById)
            SELECT @DeliveryId, di.ItemId, di.ProductId, N'Short', di.OrderedQty - di.AcceptedQty,
                   N'Supplier did not deliver the full quantity', @UserId
            FROM dbo.DeliveryItems di
            WHERE di.DeliveryId = @DeliveryId AND di.OrderedQty > di.AcceptedQty;

        UPDATE dbo.Deliveries
        SET [Status]     = @NewStatus,
            DateReceived = GETDATE(),
            Remarks      = ISNULL(NULLIF(LTRIM(RTRIM(@Remarks)), N''), Remarks)
        WHERE DeliveryId = @DeliveryId;

    COMMIT;

    SELECT @ReportRef      AS ReportRef,
           @TotalAccepted  AS TotalAccepted,
           @TotalRejected  AS TotalRejected,
           @Outstanding    AS Outstanding,
           @NewStatus      AS [Status],
           ISNULL((SELECT RefNo FROM dbo.Requisitions WHERE RequisitionId = @ReqId), N'') AS RequisitionRef,
           CASE WHEN @ReqId IS NOT NULL
                 AND NOT EXISTS (SELECT 1 FROM dbo.RequisitionItems i
                                 JOIN dbo.Products p ON p.ProductId = i.ProductId
                                 WHERE i.RequisitionId = @ReqId AND p.StockQty < i.Quantity)
                THEN 1 ELSE 0 END AS RequisitionReady;
END
GO

-- ---------------------------------------------------------------------
--  Receiving reports and discrepancy log of one delivery
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_DelGetReports
    @DeliveryId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT rr.ReportRef,
           rr.DateReceived,
           u.FullName AS ReceivedBy,
           rr.TotalAccepted,
           rr.TotalRejected
    FROM dbo.ReceivingReports rr
    JOIN dbo.AppUsers u ON u.UserId = rr.ReceivedById
    WHERE rr.DeliveryId = @DeliveryId
    ORDER BY rr.ReportId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DelGetDiscrepancies
    @DeliveryId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT d.DiscrepancyType,
           p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           d.Qty,
           ISNULL(d.Note, N'')     AS Note,
           d.DateLogged,
           ISNULL(u.FullName, N'') AS ByUser
    FROM dbo.DeliveryDiscrepancies d
    JOIN dbo.Products p ON p.ProductId = d.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    LEFT JOIN dbo.AppUsers u ON u.UserId = d.LoggedById
    WHERE d.DeliveryId = @DeliveryId
    ORDER BY d.DiscrepancyId DESC;
END
GO

-- ---------------------------------------------------------------------
--  One sample expected delivery so the screen is not empty (only if there are none yet)
-- ---------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.Deliveries)
   AND EXISTS (SELECT 1 FROM dbo.AppUsers WHERE Username = N'manager')
BEGIN
    DECLARE @MgrId INT = (SELECT UserId FROM dbo.AppUsers WHERE Username = N'manager');
    DECLARE @SampleId INT;

    INSERT INTO dbo.Deliveries (DeliveryRef, PoRef, SupplierName, [Status], CreatedById, Remarks)
    VALUES (N'DEL-SAMPLE-0001', N'PO-SAMPLE-0001', N'United Kingdom supplier', N'Expected', @MgrId, N'Sample delivery');

    SET @SampleId = SCOPE_IDENTITY();

    INSERT INTO dbo.DeliveryItems (DeliveryId, ProductId, OrderedQty)
    SELECT TOP 3 @SampleId, ProductId, 10 FROM dbo.Products WHERE StockQty <= ReorderLevel ORDER BY ProductId;
END
GO

SET NOEXEC OFF;
GO

-- ---------------------------------------------------------------------
--  CHECK (should show 5 tables and 9 procedures)
-- ---------------------------------------------------------------------
SELECT COUNT(*) AS DeliveryTables FROM sys.tables WHERE name IN (N'Deliveries', N'DeliveryItems', N'ReceivingReports', N'ReceivingReportItems', N'DeliveryDiscrepancies');
SELECT COUNT(*) AS DeliveryProcs  FROM sys.procedures WHERE name LIKE 'usp[_]Del%';
GO

-- =====================================================================
--  Solemate - alignment with the system analysis paper  (run AFTER 11_PurchasingUpgrade.sql)
--   * three logins only: the Manager does the warehouse and purchasing work, the Head (owner) approves
--   * approving a requisition RESERVES the available stock automatically (paper 3.2)
--   * the shortage becomes a PURCHASE REQUISITION; the Head checks the budget (paper 3.3)
--   * supplier quotations are compared; an approved purchase order is created and sent
--   * a delivery closed short creates a new purchase requisition for what is still missing
--   * notifications for every step
--  Safe to run more than once.
-- =====================================================================
USE SolemateKicksDB;
GO

/* ---------------------------------------------------------------
   1. EXISTING PROCEDURES (Manager does the warehouse and purchasing work)
      (restores the original Manager-only checks if an earlier version of this script widened them)
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_DelCreate
    @UserId        INT,
    @SupplierName  NVARCHAR(100),
    @PoRef         NVARCHAR(30),
    @RequisitionId INT,
    @Remarks       NVARCHAR(200),
    @ItemsXml      NVARCHAR(MAX),
    @ExpectedDate  DATE = NULL,
    @SupplierId    INT  = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 51001, 'Only the manager can record deliveries.', 1;

    IF @SupplierId <> 0
    BEGIN
        SELECT @SupplierName = SupplierName FROM dbo.Suppliers WHERE SupplierId = @SupplierId AND IsActive = 1;
        IF @SupplierName IS NULL
            THROW 51007, 'The selected supplier was not found or is inactive.', 1;
    END

    SET @SupplierName = LTRIM(RTRIM(ISNULL(@SupplierName, N'')));
    IF @SupplierName = N''
        THROW 51002, 'Supplier is required.', 1;

    IF @ExpectedDate IS NOT NULL AND @ExpectedDate < CAST(GETDATE() AS DATE)
        THROW 51008, 'The expected delivery date cannot be in the past.', 1;

    IF @RequisitionId <> 0 AND NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved')
        THROW 51003, 'The linked requisition must be an approved requisition.', 1;

    DECLARE @x XML = CAST(@ItemsXml AS XML);
    DECLARE @Items TABLE (ProductId INT NOT NULL PRIMARY KEY, Quantity INT NOT NULL, UnitCost DECIMAL(10,2) NULL);

    INSERT INTO @Items (ProductId, Quantity, UnitCost)
    SELECT ProductId, SUM(Quantity), MAX(UnitCost)
    FROM (SELECT t.i.value('@ProductId', 'int')           AS ProductId,
                 t.i.value('@Quantity',  'int')           AS Quantity,
                 t.i.value('@UnitCost',  'decimal(10,2)') AS UnitCost
          FROM @x.nodes('/Items/Item') AS t(i)) s
    GROUP BY ProductId;

    IF NOT EXISTS (SELECT 1 FROM @Items)
        THROW 51004, 'Add at least one item to the delivery.', 1;
    IF EXISTS (SELECT 1 FROM @Items WHERE Quantity < 1)
        THROW 51005, 'Every ordered quantity must be at least 1.', 1;
    IF EXISTS (SELECT 1 FROM @Items WHERE UnitCost < 0)
        THROW 51009, 'Unit cost cannot be negative.', 1;
    IF EXISTS (SELECT 1 FROM @Items i LEFT JOIN dbo.Products p ON p.ProductId = i.ProductId WHERE p.ProductId IS NULL)
        THROW 51006, 'One of the selected products no longer exists.', 1;

    BEGIN TRAN;

        -- free-text supplier: reuse the existing one with that name, or create it
        IF @SupplierId = 0
        BEGIN
            SELECT @SupplierId = SupplierId FROM dbo.Suppliers WHERE SupplierName = @SupplierName;
            IF @SupplierId IS NULL OR @SupplierId = 0
            BEGIN
                INSERT INTO dbo.Suppliers (SupplierName) VALUES (@SupplierName);
                SET @SupplierId = SCOPE_IDENTITY();
            END
        END

        DECLARE @Prefix NVARCHAR(30) = N'DEL-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next   INT = (SELECT COUNT(*) FROM dbo.Deliveries WITH (UPDLOCK, HOLDLOCK)
                               WHERE DeliveryRef LIKE @Prefix + N'%') + 1;
        DECLARE @Ref    NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.Deliveries (DeliveryRef, PoRef, SupplierName, SupplierId, RequisitionId, ExpectedDate,
                                    [Status], DateCreated, CreatedById, Remarks)
        VALUES (@Ref, NULLIF(LTRIM(RTRIM(@PoRef)), N''), @SupplierName, @SupplierId, NULLIF(@RequisitionId, 0), @ExpectedDate,
                N'Expected', GETDATE(), @UserId, NULLIF(LTRIM(RTRIM(@Remarks)), N''));

        DECLARE @Id INT = SCOPE_IDENTITY();

        INSERT INTO dbo.DeliveryItems (DeliveryId, ProductId, OrderedQty, UnitCost)
        SELECT @Id, ProductId, Quantity, UnitCost FROM @Items;

    COMMIT;

    SELECT @Ref AS DeliveryRef;
END
GO

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

CREATE OR ALTER PROCEDURE dbo.usp_DelSaveSupplier
    @UserId        INT,
    @SupplierId    INT,
    @SupplierName  NVARCHAR(100),
    @ContactPerson NVARCHAR(100) = NULL,
    @Phone         NVARCHAR(30)  = NULL,
    @Email         NVARCHAR(100) = NULL,
    @IsActive      INT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 51021, 'Only the manager can manage suppliers.', 1;

    SET @SupplierName = LTRIM(RTRIM(ISNULL(@SupplierName, N'')));
    IF @SupplierName = N''
        THROW 51022, 'Supplier name is required.', 1;

    IF EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierName = @SupplierName AND SupplierId <> @SupplierId)
        THROW 51023, 'A supplier with this name already exists.', 1;

    IF @SupplierId = 0
    BEGIN
        INSERT INTO dbo.Suppliers (SupplierName, ContactPerson, Phone, Email, IsActive)
        VALUES (@SupplierName, NULLIF(LTRIM(RTRIM(@ContactPerson)), N''), NULLIF(LTRIM(RTRIM(@Phone)), N''),
                NULLIF(LTRIM(RTRIM(@Email)), N''), CASE WHEN @IsActive = 0 THEN 0 ELSE 1 END);
        SET @SupplierId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
            THROW 51024, 'The supplier was not found.', 1;

        UPDATE dbo.Suppliers
        SET SupplierName  = @SupplierName,
            ContactPerson = NULLIF(LTRIM(RTRIM(@ContactPerson)), N''),
            Phone         = NULLIF(LTRIM(RTRIM(@Phone)), N''),
            Email         = NULLIF(LTRIM(RTRIM(@Email)), N''),
            IsActive      = CASE WHEN @IsActive = 0 THEN 0 ELSE 1 END
        WHERE SupplierId = @SupplierId;

        -- keep the name on existing deliveries in sync
        UPDATE dbo.Deliveries SET SupplierName = @SupplierName WHERE SupplierId = @SupplierId;
    END

    SELECT @SupplierId AS SupplierId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SupSave
    @SupplierId   INT,
    @SupplierName NVARCHAR(100),
    @Address      NVARCHAR(200),
    @Email        NVARCHAR(100),
    @UserId       INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @SupplierName = LTRIM(RTRIM(ISNULL(@SupplierName, N'')));
    SET @Address      = LTRIM(RTRIM(ISNULL(@Address, N'')));
    SET @Email        = LTRIM(RTRIM(ISNULL(@Email, N'')));

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50010, N'Only the Head or a Manager can add or edit suppliers.', 1;

    IF @SupplierName = N''
        THROW 50011, N'Supplier name is required.', 1;

    IF EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierName = @SupplierName AND SupplierId <> @SupplierId)
        THROW 50012, N'A supplier with this name already exists.', 1;

    IF @SupplierId = 0
    BEGIN
        INSERT INTO dbo.Suppliers (SupplierName, [Address], Email)
        VALUES (@SupplierName, @Address, NULLIF(@Email, N''));

        SET @SupplierId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
            THROW 50013, N'Supplier not found.', 1;

        UPDATE dbo.Suppliers
        SET SupplierName = @SupplierName, [Address] = @Address, Email = NULLIF(@Email, N'')
        WHERE SupplierId = @SupplierId;

        -- keep the supplier name on existing purchase orders up to date
        UPDATE dbo.PurchaseOrders SET Supplier = @SupplierName WHERE SupplierId = @SupplierId;

        -- ...and on existing deliveries, when the Delivery module is installed
        IF OBJECT_ID(N'dbo.Deliveries') IS NOT NULL AND COL_LENGTH(N'dbo.Deliveries', N'SupplierId') IS NOT NULL
            EXEC sp_executesql
                N'UPDATE dbo.Deliveries SET SupplierName = @n WHERE SupplierId = @id',
                N'@n NVARCHAR(100), @id INT',
                @n = @SupplierName, @id = @SupplierId;
    END

    SELECT @SupplierId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SupDelete
    @SupplierId INT,
    @UserId     INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50014, N'Only the Head or a Manager can delete suppliers.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
        THROW 50013, N'Supplier not found.', 1;

    BEGIN TRY
        DELETE FROM dbo.Suppliers WHERE SupplierId = @SupplierId;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 547   -- foreign key: purchase orders or deliveries use this supplier
            THROW 50015, N'This supplier is used by purchase orders or deliveries, so it cannot be deleted.', 1;
        THROW;
    END CATCH
END
GO

/* ---------------------------------------------------------------
   2. PURCHASE REQUISITION (created by the system only for the shortage)
   --------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.PurchaseRequisitions') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseRequisitions (
        PrId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseRequisitions PRIMARY KEY,
        PrNo          NVARCHAR(30) NOT NULL CONSTRAINT UQ_PurchaseRequisitions_No UNIQUE,     -- PR-yyyyMMdd-0001
        RequisitionId INT NOT NULL CONSTRAINT FK_PR_Req REFERENCES dbo.Requisitions(RequisitionId),
        [Status]      NVARCHAR(20) NOT NULL CONSTRAINT DF_PR_Status DEFAULT N'Pending'
                      CONSTRAINT CK_PR_Status CHECK ([Status] IN (N'Pending', N'Approved', N'Held', N'Ordered', N'Cancelled')),
        DateCreated   DATETIME NOT NULL CONSTRAINT DF_PR_Date DEFAULT GETDATE(),
        ReviewedById  INT NULL CONSTRAINT FK_PR_ReviewedBy REFERENCES dbo.AppUsers(UserId),
        DateReviewed  DATETIME NULL,
        ReviewNote    NVARCHAR(200) NULL
    );
END
GO
IF OBJECT_ID(N'dbo.PurchaseRequisitionItems') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseRequisitionItems (
        ItemId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PRItems PRIMARY KEY,
        PrId         INT NOT NULL CONSTRAINT FK_PRItems_PR REFERENCES dbo.PurchaseRequisitions(PrId) ON DELETE CASCADE,
        ProductId    INT NOT NULL CONSTRAINT FK_PRItems_Product REFERENCES dbo.Products(ProductId),
        RequestedQty INT NOT NULL,
        FreeQty      INT NOT NULL,
        ShortQty     INT NOT NULL CONSTRAINT CK_PRItems_Short CHECK (ShortQty >= 1)
    );
END
GO
IF COL_LENGTH(N'dbo.PurchaseOrders', N'PurchaseRequisitionId') IS NULL
    ALTER TABLE dbo.PurchaseOrders ADD PurchaseRequisitionId INT NULL
        CONSTRAINT FK_PO_PR REFERENCES dbo.PurchaseRequisitions(PrId);
GO

/* Creates the purchase requisition for the shortage of an approved requisition (no result set: also used by triggers) */
CREATE OR ALTER PROCEDURE dbo.usp_PurCreatePrCore
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved') RETURN;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseRequisitions WHERE RequisitionId = @RequisitionId
               AND [Status] IN (N'Pending', N'Approved', N'Held')) RETURN;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE RequisitionId = @RequisitionId
               AND [Status] IN (N'PendingApproval', N'Approved', N'Ordered')) RETURN;
    IF NOT EXISTS (SELECT 1 FROM dbo.fn_PurShortage(@RequisitionId) WHERE Shortage > 0) RETURN;

    BEGIN TRAN;

        DECLARE @Prefix NVARCHAR(30) = N'PR-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next INT = (SELECT COUNT(*) FROM dbo.PurchaseRequisitions WITH (UPDLOCK, HOLDLOCK) WHERE PrNo LIKE @Prefix + N'%') + 1;
        DECLARE @PrNo NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.PurchaseRequisitions (PrNo, RequisitionId) VALUES (@PrNo, @RequisitionId);
        DECLARE @PrId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.PurchaseRequisitionItems (PrId, ProductId, RequestedQty, FreeQty, ShortQty)
        SELECT @PrId, ProductId, Requested, FreeStock, Shortage
        FROM dbo.fn_PurShortage(@RequisitionId) WHERE Shortage > 0;

    COMMIT;
END
GO

/* Items of the APPROVED purchase requisition of a requisition (same columns as fn_PurShortage) */
CREATE OR ALTER FUNCTION dbo.fn_PurPrItems (@RequisitionId INT)
RETURNS TABLE
AS
RETURN
    SELECT i.ProductId, i.RequestedQty AS Requested, i.FreeQty AS FreeStock, i.ShortQty AS Shortage
    FROM dbo.PurchaseRequisitions pr
    JOIN dbo.PurchaseRequisitionItems i ON i.PrId = pr.PrId
    WHERE pr.RequisitionId = @RequisitionId AND pr.[Status] = N'Approved';
GO

/* list of purchase requisitions with the estimated budget (cheapest complete supplier quotation) */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetPRs
AS
BEGIN
    SET NOCOUNT ON;
    SELECT pr.PrId, pr.PrNo, r.RefNo AS RequisitionRef, r.Branch, pr.DateCreated, pr.[Status],
           (SELECT COUNT(*) FROM dbo.PurchaseRequisitionItems x WHERE x.PrId = pr.PrId) AS Items,
           (SELECT ISNULL(SUM(x.ShortQty), 0) FROM dbo.PurchaseRequisitionItems x WHERE x.PrId = pr.PrId) AS Pairs,
           est.Total AS EstimatedBudget,
           ISNULL(pr.ReviewNote, N'') AS ReviewNote,
           ISNULL(u.FullName, N'')    AS ReviewedBy
    FROM dbo.PurchaseRequisitions pr
    JOIN dbo.Requisitions r ON r.RequisitionId = pr.RequisitionId
    LEFT JOIN dbo.AppUsers u ON u.UserId = pr.ReviewedById
    OUTER APPLY (
        SELECT MIN(t.Total) AS Total
        FROM (SELECT SUM(i.ShortQty * sp.UnitCost) AS Total
              FROM dbo.PurchaseRequisitionItems i
              JOIN dbo.SupplierPrices sp ON sp.ProductId = i.ProductId
              JOIN dbo.Suppliers s ON s.SupplierId = sp.SupplierId AND s.IsActive = 1
              WHERE i.PrId = pr.PrId
              GROUP BY sp.SupplierId
              HAVING COUNT(*) = (SELECT COUNT(*) FROM dbo.PurchaseRequisitionItems x WHERE x.PrId = pr.PrId)) t
    ) est
    ORDER BY pr.DateCreated DESC, pr.PrId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurGetPRItems
    @PrId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size, i.RequestedQty, i.FreeQty, i.ShortQty
    FROM dbo.PurchaseRequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE i.PrId = @PrId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* Head (the owner): check the budget and approve the purchase requisition, or hold it (reason required) */
CREATE OR ALTER PROCEDURE dbo.usp_PurDecidePR
    @PrId INT, @UserId INT, @Approve BIT, @Note NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50050, N'Only the Head can check the budget and approve a purchase requisition.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.PurchaseRequisitions WHERE PrId = @PrId AND [Status] IN (N'Pending', N'Held'))
        THROW 50051, N'Only pending or held purchase requisitions can be approved or held.', 1;

    SET @Note = NULLIF(LTRIM(RTRIM(ISNULL(@Note, N''))), N'');
    IF @Approve = 0 AND @Note IS NULL
        THROW 50052, N'Enter the reason for holding the purchase requisition (for example: over budget).', 1;

    UPDATE dbo.PurchaseRequisitions
    SET [Status] = CASE WHEN @Approve = 1 THEN N'Approved' ELSE N'Held' END,
        ReviewedById = @UserId, DateReviewed = GETDATE(), ReviewNote = @Note
    WHERE PrId = @PrId AND [Status] IN (N'Pending', N'Held');
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurGetPendingCount
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) FROM dbo.PurchaseRequisitions WHERE [Status] = N'Pending';
END
GO

/* ---------------------------------------------------------------
   3. PURCHASING PROCEDURES now work from the APPROVED purchase requisition
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetShortage
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.ProductId, p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size,
           s.Requested, s.FreeStock, s.Shortage
    FROM dbo.fn_PurPrItems(@RequisitionId) s
    JOIN dbo.Products p ON p.ProductId = s.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE s.Shortage > 0
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

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
               (SELECT COUNT(*) FROM dbo.fn_PurPrItems(@RequisitionId) f
                JOIN dbo.SupplierPrices sp ON sp.ProductId = f.ProductId AND sp.SupplierId = s.SupplierId
                WHERE f.Shortage > 0) AS Quoted,
               (SELECT COUNT(*) FROM dbo.fn_PurPrItems(@RequisitionId) f WHERE f.Shortage > 0) AS Needed,
               CAST(ISNULL((SELECT SUM(f.Shortage * sp.UnitCost) FROM dbo.fn_PurPrItems(@RequisitionId) f
                            JOIN dbo.SupplierPrices sp ON sp.ProductId = f.ProductId AND sp.SupplierId = s.SupplierId
                            WHERE f.Shortage > 0), 0) AS DECIMAL(12,2)) AS Total
        FROM dbo.Suppliers s
        WHERE s.IsActive = 1
    ) q
    ORDER BY CASE WHEN q.Quoted = q.Needed AND q.Needed > 0 THEN 0 ELSE 1 END, q.Total, q.SupplierName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurGetApprovedRequisitions
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.RequisitionId, r.RefNo, r.Branch, r.DateCreated,
           (SELECT COUNT(*) FROM dbo.fn_PurPrItems(r.RequisitionId)) AS ItemCount
    FROM dbo.Requisitions r
    WHERE r.[Status] = N'Approved'
      AND EXISTS (SELECT 1 FROM dbo.PurchaseRequisitions pr WHERE pr.RequisitionId = r.RequisitionId AND pr.[Status] = N'Approved')
      AND NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrders po
                      WHERE po.RequisitionId = r.RequisitionId AND po.[Status] IN (N'PendingApproval', N'Approved', N'Ordered'))
    ORDER BY r.DateCreated;
END
GO

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
           u.FullName                    AS CreatedBy,
           ISNULL(pr.PrNo, N'')          AS PrNo
    FROM dbo.PurchaseOrders po
    JOIN dbo.AppUsers u ON u.UserId = po.CreatedById
    LEFT JOIN dbo.PurchaseOrderItems i ON i.PurchaseOrderId = po.PurchaseOrderId
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = po.RequisitionId
    LEFT JOIN dbo.PurchaseRequisitions pr ON pr.PrId = po.PurchaseRequisitionId
    GROUP BY po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status], r.RefNo,
             po.PaymentTerms, po.ExpectedDate, po.ReviewNote, u.FullName, pr.PrNo
    ORDER BY po.DateCreated DESC, po.PurchaseOrderId DESC;
END
GO

/* Manager: create the APPROVED purchase order for the chosen supplier from the budget-approved purchase requisition */
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

    DECLARE @PrId INT, @PrReviewer INT, @PrDate DATETIME;
    SELECT @PrId = PrId, @PrReviewer = ReviewedById, @PrDate = DateReviewed
    FROM dbo.PurchaseRequisitions
    WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved';

    IF @PrId IS NULL
        THROW 50053, N'This requisition has no purchase requisition approved by the Head yet.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE RequisitionId = @RequisitionId
               AND [Status] IN (N'PendingApproval', N'Approved', N'Ordered'))
        THROW 50003, N'This requisition already has an active purchase order.', 1;

    DECLARE @NoQuote NVARCHAR(30) =
        (SELECT TOP 1 p.Sku
         FROM dbo.PurchaseRequisitionItems i
         JOIN dbo.Products p ON p.ProductId = i.ProductId
         LEFT JOIN dbo.SupplierPrices sp ON sp.SupplierId = @SupplierId AND sp.ProductId = i.ProductId
         WHERE i.PrId = @PrId AND sp.ProductId IS NULL
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

        INSERT INTO dbo.PurchaseOrders (PoNo, RequisitionId, Supplier, SupplierId, CreatedById, [Status], PaymentTerms,
                                        PurchaseRequisitionId, ReviewedById, DateReviewed)
        VALUES (@PoNo, @RequisitionId, @Supplier, @SupplierId, @UserId, N'Approved', @Terms, @PrId, @PrReviewer, @PrDate);

        DECLARE @PoId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.PurchaseOrderItems (PurchaseOrderId, ProductId, Quantity, UnitCost)
        SELECT @PoId, i.ProductId, i.ShortQty, sp.UnitCost
        FROM dbo.PurchaseRequisitionItems i
        JOIN dbo.SupplierPrices sp ON sp.SupplierId = @SupplierId AND sp.ProductId = i.ProductId
        WHERE i.PrId = @PrId;

        UPDATE dbo.PurchaseRequisitions SET [Status] = N'Ordered' WHERE PrId = @PrId;

    COMMIT;
    SELECT @PoId;
END
GO

/* ---------------------------------------------------------------
   4. APPROVAL: reserve the available stock + create the purchase requisition for the shortage
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
        CASE WHEN @Short = 0 THEN N'Stock sufficient - reserved and ready to issue'
             ELSE CAST(@Short AS NVARCHAR(10)) + N' item(s) short - available stock reserved, purchase requisition created' END;

    UPDATE dbo.Requisitions
    SET [Status] = N'Approved', ReviewedById = @HeadId, DateReviewed = GETDATE(), StockCheck = @Check
    WHERE RequisitionId = @RequisitionId AND [Status] = N'Pending';

    IF @@ROWCOUNT = 0
        THROW 50021, 'This requisition is no longer pending.', 1;

    -- paper 3.2: the system reserves the available stock; if something is short, the shortage becomes a purchase requisition
    DECLARE @Res INT, @ShortLines INT;
    EXEC dbo.usp_IssReserveCore @RequisitionId, @Res OUTPUT, @ShortLines OUTPUT;
    EXEC dbo.usp_PurCreatePrCore @RequisitionId;

    SELECT @RefNo AS RefNo,
           @Check AS StockCheck,
           p.Sku,
           p.ProductName,
           p.Size,
           i.Quantity AS Requested,
           p.StockQty - p.ReservedQty + i.ReservedQty AS Available
    FROM dbo.RequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    WHERE i.RequisitionId = @RequisitionId
    ORDER BY p.Sku;
END
GO

/* ---------------------------------------------------------------
   5. NOTIFICATIONS
   --------------------------------------------------------------- */
CREATE OR ALTER TRIGGER dbo.trg_PurchaseRequisitions_Notify ON dbo.PurchaseRequisitions AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- new purchase requisition -> the Head checks the budget
    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT N'Head', NULL, N'Purchase requisition ' + i.PrNo + N' (shortage of ' + r.RefNo + N') is waiting for your budget approval.'
    FROM inserted i
    JOIN dbo.Requisitions r ON r.RequisitionId = i.RequisitionId
    LEFT JOIN deleted d ON d.PrId = i.PrId
    WHERE i.[Status] = N'Pending' AND d.PrId IS NULL;

    -- budget approved -> the purchasing officer gets the quotations and creates the PO
    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT N'Manager', NULL, N'Purchase requisition ' + i.PrNo + N' was approved by the Head. Compare the supplier quotations and create the purchase order.'
    FROM inserted i JOIN deleted d ON d.PrId = i.PrId
    WHERE i.[Status] = N'Approved' AND d.[Status] <> N'Approved';

    -- not approved -> the department is told that the request is held / to be revised
    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT x.ToRole, NULL, N'Purchase requisition ' + i.PrNo + N' was HELD by the Head. Reason: ' + ISNULL(i.ReviewNote, N'-')
    FROM inserted i JOIN deleted d ON d.PrId = i.PrId
    CROSS JOIN (VALUES (N'Manager')) x (ToRole)
    WHERE i.[Status] = N'Held' AND d.[Status] <> N'Held';

    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT NULL, r.RequestedById, N'The purchase for your requisition ' + r.RefNo + N' was held by the Head. Reason: ' + ISNULL(i.ReviewNote, N'-')
    FROM inserted i JOIN deleted d ON d.PrId = i.PrId
    JOIN dbo.Requisitions r ON r.RequisitionId = i.RequisitionId
    WHERE i.[Status] = N'Held' AND d.[Status] <> N'Held';
END
GO

CREATE OR ALTER TRIGGER dbo.trg_PurchaseOrders_Notify ON dbo.PurchaseOrders AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    -- PO sent to the supplier -> warehouse / manager expect the delivery
    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT x.ToRole, NULL, N'Purchase order ' + i.PoNo + N' was sent to ' + i.Supplier + N'. Delivery expected on '
           + ISNULL(CONVERT(NVARCHAR(12), i.ExpectedDate, 107), N'a date to be confirmed') + N'.'
    FROM inserted i JOIN deleted d ON d.PurchaseOrderId = i.PurchaseOrderId
    CROSS JOIN (VALUES (N'Manager')) x (ToRole)
    WHERE i.[Status] = N'Ordered' AND d.[Status] <> N'Ordered';
END
GO

/* When goods are received: close the PO, RESERVE the accepted goods for the requisition (paper 3.4),
   and when a delivery is closed short, raise a new purchase requisition for what is still missing */
CREATE OR ALTER TRIGGER dbo.trg_Deliveries_Received ON dbo.Deliveries AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE([Status]) RETURN;

    UPDATE po SET po.[Status] = N'Received', po.DateReceived = GETDATE()
    FROM dbo.PurchaseOrders po
    JOIN inserted i ON i.PurchaseOrderId = po.PurchaseOrderId
    WHERE i.[Status] IN (N'Complete', N'Short') AND po.[Status] = N'Ordered';

    DECLARE @Req INT, @Res INT, @Short INT;
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR
        SELECT DISTINCT RequisitionId FROM inserted
        WHERE RequisitionId IS NOT NULL AND [Status] IN (N'Partial', N'Complete', N'Short');
    OPEN c;
    FETCH NEXT FROM c INTO @Req;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC dbo.usp_IssReserveCore @Req, @Res OUTPUT, @Short OUTPUT;

        IF @Res > 0
            INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
            SELECT x.ToRole, NULL,
                   N'Delivery ' + d.DeliveryRef + N' was received: ' + CAST(@Res AS NVARCHAR(10)) + N' pair(s) were reserved for requisition ' + r.RefNo + N'. Ready to issue.'
            FROM dbo.Requisitions r
            JOIN inserted d ON d.RequisitionId = r.RequisitionId
            CROSS JOIN (VALUES (N'Manager')) x (ToRole)
            WHERE r.RequisitionId = @Req
              AND d.DeliveryId = (SELECT MIN(DeliveryId) FROM inserted WHERE RequisitionId = @Req);

        IF EXISTS (SELECT 1 FROM inserted WHERE RequisitionId = @Req AND [Status] = N'Short')
            EXEC dbo.usp_PurCreatePrCore @Req;

        FETCH NEXT FROM c INTO @Req;
    END
    CLOSE c;
    DEALLOCATE c;

    INSERT INTO dbo.Notifications (ToRole, ToUserId, [Message])
    SELECT x.ToRole, NULL, N'Delivery ' + i.DeliveryRef + N' from ' + i.SupplierName + N' was closed SHORT. Follow up with the supplier.'
    FROM inserted i JOIN deleted d ON d.DeliveryId = i.DeliveryId
    CROSS JOIN (VALUES (N'Head'), (N'Manager')) x (ToRole)
    WHERE i.[Status] = N'Short' AND d.[Status] <> N'Short';
END
GO

/* ---------------------------------------------------------------
   6. BACK-FILL: requisitions that were approved BEFORE this script get their stock reserved
      and (if short) their purchase requisition, exactly as the new approval does
   --------------------------------------------------------------- */
DECLARE @BfReq INT, @BfRes INT, @BfShort INT;
DECLARE bf CURSOR LOCAL FAST_FORWARD FOR
    SELECT RequisitionId FROM dbo.Requisitions WHERE [Status] = N'Approved' ORDER BY RequisitionId;
OPEN bf;
FETCH NEXT FROM bf INTO @BfReq;
WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC dbo.usp_IssReserveCore @BfReq, @BfRes OUTPUT, @BfShort OUTPUT;
    EXEC dbo.usp_PurCreatePrCore @BfReq;
    FETCH NEXT FROM bf INTO @BfReq;
END
CLOSE bf;
DEALLOCATE bf;
GO

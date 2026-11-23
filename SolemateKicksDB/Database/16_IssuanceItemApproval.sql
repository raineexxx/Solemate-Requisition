USE SolemateKicksDB;
GO

IF COL_LENGTH(N'dbo.RequisitionItems', N'ItemStatus') IS NULL
BEGIN
    RAISERROR('Run 14_ItemApproval.sql first: RequisitionItems.ItemStatus was not found.', 16, 1);
    SET NOEXEC ON;   -- skips the rest of this script
END
GO

/* Approved requisitions that still have something to issue (approved items only) */
CREATE OR ALTER PROCEDURE dbo.usp_IssGetRequisitions
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.RequisitionId, r.RefNo, r.Branch, r.DateCreated, u.FullName AS RequestedBy,
           SUM(i.Quantity) AS Requested, SUM(i.ReservedQty) AS Reserved, SUM(i.IssuedQty) AS Issued
    FROM dbo.Requisitions r
    JOIN dbo.AppUsers u ON u.UserId = r.RequestedById
    JOIN dbo.RequisitionItems i ON i.RequisitionId = r.RequisitionId AND i.ItemStatus = N'Approved'
    WHERE r.[Status] = N'Approved'
    GROUP BY r.RequisitionId, r.RefNo, r.Branch, r.DateCreated, u.FullName
    HAVING SUM(i.Quantity) > SUM(i.IssuedQty)
    ORDER BY r.DateCreated, r.RequisitionId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_IssGetItems
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size,
           i.Quantity AS Requested, i.IssuedQty AS Issued, i.ReservedQty AS Reserved,
           i.Quantity - i.IssuedQty - i.ReservedQty AS Outstanding,
           p.StockQty - p.ReservedQty AS Available
    FROM dbo.RequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE i.RequisitionId = @RequisitionId AND i.ItemStatus = N'Approved'
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* Reserve whatever stock is available for the APPROVED items (run it again after a delivery arrives). */
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

    SELECT ISNULL((SELECT SUM(Take) FROM @T), 0) AS Reserved,
           (SELECT COUNT(*) FROM dbo.RequisitionItems
            WHERE RequisitionId = @RequisitionId AND ItemStatus = N'Approved'
              AND Quantity - IssuedQty - ReservedQty > 0) AS ShortLines;
END
GO

/* Issue the reserved quantities: slip + stock deduction. The requisition is closed later, when the branch signs. */
CREATE OR ALTER PROCEDURE dbo.usp_IssCreate
    @RequisitionId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 52001, 'Only the manager can reserve and issue stock.', 1;

    DECLARE @Branch NVARCHAR(50);
    SELECT @Branch = Branch FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved';
    IF @Branch IS NULL THROW 52002, 'Only approved requisitions can be issued.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.RequisitionItems WHERE RequisitionId = @RequisitionId AND ItemStatus = N'Approved' AND ReservedQty > 0)
        THROW 52003, 'Nothing is reserved for this requisition. Reserve stock first.', 1;

    BEGIN TRAN;

        DECLARE @Prefix NVARCHAR(30) = N'ISS-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next   INT = (SELECT COUNT(*) FROM dbo.Issuances WITH (UPDLOCK, HOLDLOCK) WHERE IssueNo LIKE @Prefix + N'%') + 1;
        DECLARE @IssueNo NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.Issuances (IssueNo, RequisitionId, Branch, IssuedById)
        VALUES (@IssueNo, @RequisitionId, @Branch, @UserId);
        DECLARE @IssId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.IssuanceItems (IssuanceId, ProductId, Quantity)
        SELECT @IssId, ProductId, ReservedQty
        FROM dbo.RequisitionItems WHERE RequisitionId = @RequisitionId AND ItemStatus = N'Approved' AND ReservedQty > 0;

        -- deduct stock and release the reservation in the same statement
        UPDATE p SET p.StockQty = p.StockQty - i.ReservedQty, p.ReservedQty = p.ReservedQty - i.ReservedQty
        FROM dbo.Products p
        JOIN dbo.RequisitionItems i ON i.ProductId = p.ProductId
        WHERE i.RequisitionId = @RequisitionId AND i.ItemStatus = N'Approved' AND i.ReservedQty > 0;

        INSERT INTO dbo.StockMovements (ProductId, QtyChange, StockAfter, Reason, CreatedById)
        SELECT i.ProductId, -i.ReservedQty, p.StockQty, N'Issued ' + @IssueNo, @UserId
        FROM dbo.RequisitionItems i JOIN dbo.Products p ON p.ProductId = i.ProductId
        WHERE i.RequisitionId = @RequisitionId AND i.ItemStatus = N'Approved' AND i.ReservedQty > 0;

        UPDATE dbo.RequisitionItems SET IssuedQty = IssuedQty + ReservedQty, ReservedQty = 0
        WHERE RequisitionId = @RequisitionId AND ItemStatus = N'Approved' AND ReservedQty > 0;

    COMMIT;

    -- Closed is always 0 here: the requisition closes when the branch signs (usp_IssAcknowledge)
    SELECT @IssueNo AS IssueNo, CAST(0 AS BIT) AS Closed;
END
GO

/* The receiving branch signs for the goods (cashier or manager of that branch).
   When every approved item has been issued and every slip is signed, the requisition is closed.
   Returns one row: Closed = 1 when this signature closed the requisition. */
CREATE OR ALTER PROCEDURE dbo.usp_IssAcknowledge
    @IssuanceId INT, @UserId INT, @ReceivedBy NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Role NVARCHAR(20), @UBranch NVARCHAR(50);
    SELECT @Role = [Role], @UBranch = Branch FROM dbo.AppUsers WHERE UserId = @UserId;
    IF @Role IS NULL OR @Role NOT IN (N'Cashier', N'Manager')
        THROW 52010, 'Only branch staff can acknowledge receipt.', 1;

    SET @ReceivedBy = LTRIM(RTRIM(ISNULL(@ReceivedBy, N'')));
    IF @ReceivedBy = N'' THROW 52011, 'Enter the name of the person who received the items.', 1;

    DECLARE @Branch NVARCHAR(50), @Status NVARCHAR(20), @ReqId INT;
    SELECT @Branch = Branch, @Status = [Status], @ReqId = RequisitionId FROM dbo.Issuances WHERE IssuanceId = @IssuanceId;
    IF @Branch IS NULL THROW 52012, 'The issuance slip was not found.', 1;
    IF @Branch <> @UBranch THROW 52013, 'Only staff of the receiving branch can acknowledge this slip.', 1;
    IF @Status <> N'Issued' THROW 52014, 'This slip was already acknowledged.', 1;

    DECLARE @Closed BIT = 0;

    BEGIN TRAN;

        UPDATE dbo.Issuances
        SET [Status] = N'Acknowledged', AcknowledgedById = @UserId, ReceivedBy = @ReceivedBy, DateAcknowledged = GETDATE()
        WHERE IssuanceId = @IssuanceId AND [Status] = N'Issued';

        IF NOT EXISTS (SELECT 1 FROM dbo.RequisitionItems
                       WHERE RequisitionId = @ReqId AND ItemStatus = N'Approved' AND IssuedQty < Quantity)
           AND NOT EXISTS (SELECT 1 FROM dbo.Issuances WHERE RequisitionId = @ReqId AND [Status] = N'Issued')
        BEGIN
            UPDATE dbo.Requisitions SET [Status] = N'Closed' WHERE RequisitionId = @ReqId AND [Status] = N'Approved';
            IF @@ROWCOUNT > 0 SET @Closed = 1;
        END

    COMMIT;

    SELECT @Closed AS Closed;
END
GO

SET NOEXEC OFF;
GO

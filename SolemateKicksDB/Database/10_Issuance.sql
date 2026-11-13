-- =====================================================================
--  Solemate Issuance module  (run AFTER 9_Sales.sql)
--  Paper 3.2: reserve stock for an approved requisition -> issuance slip ->
--  stock is deducted on issuance -> branch signs an acknowledgment ->
--  requisition is closed when everything requested has been issued.
--  Adds: Issuances + IssuanceItems, RequisitionItems.ReservedQty / IssuedQty,
--        'Closed' requisition status, procedures usp_Iss...
-- =====================================================================
USE SolemateKicksDB;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Req_Status')
    ALTER TABLE dbo.Requisitions DROP CONSTRAINT CK_Req_Status;
GO
ALTER TABLE dbo.Requisitions ADD CONSTRAINT CK_Req_Status
    CHECK ([Status] IN (N'Pending', N'Approved', N'Rejected', N'Cancelled', N'Closed'));
GO

IF COL_LENGTH(N'dbo.RequisitionItems', N'ReservedQty') IS NULL
    ALTER TABLE dbo.RequisitionItems ADD ReservedQty INT NOT NULL CONSTRAINT DF_ReqItems_Reserved DEFAULT 0;
IF COL_LENGTH(N'dbo.RequisitionItems', N'IssuedQty') IS NULL
    ALTER TABLE dbo.RequisitionItems ADD IssuedQty INT NOT NULL CONSTRAINT DF_ReqItems_Issued DEFAULT 0;
GO

IF OBJECT_ID(N'dbo.Issuances') IS NULL
BEGIN
    CREATE TABLE dbo.Issuances (
        IssuanceId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Issuances PRIMARY KEY,
        IssueNo          NVARCHAR(30) NOT NULL CONSTRAINT UQ_Issuances_No UNIQUE,   -- ISS-yyyyMMdd-0001
        RequisitionId    INT NOT NULL CONSTRAINT FK_Iss_Req REFERENCES dbo.Requisitions(RequisitionId),
        Branch           NVARCHAR(50) NOT NULL,
        IssuedById       INT NOT NULL CONSTRAINT FK_Iss_IssuedBy REFERENCES dbo.AppUsers(UserId),
        DateIssued       DATETIME NOT NULL CONSTRAINT DF_Iss_Date DEFAULT GETDATE(),
        [Status]         NVARCHAR(20) NOT NULL CONSTRAINT DF_Iss_Status DEFAULT N'Issued'
                         CONSTRAINT CK_Iss_Status CHECK ([Status] IN (N'Issued', N'Acknowledged')),
        AcknowledgedById INT NULL CONSTRAINT FK_Iss_AckBy REFERENCES dbo.AppUsers(UserId),
        ReceivedBy       NVARCHAR(100) NULL,        -- name signed by the branch
        DateAcknowledged DATETIME NULL
    );
END
GO

IF OBJECT_ID(N'dbo.IssuanceItems') IS NULL
BEGIN
    CREATE TABLE dbo.IssuanceItems (
        ItemId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IssuanceItems PRIMARY KEY,
        IssuanceId INT NOT NULL CONSTRAINT FK_IssItems_Iss REFERENCES dbo.Issuances(IssuanceId) ON DELETE CASCADE,
        ProductId  INT NOT NULL CONSTRAINT FK_IssItems_Product REFERENCES dbo.Products(ProductId),
        Quantity   INT NOT NULL CONSTRAINT CK_IssItems_Qty CHECK (Quantity >= 1)
    );
END
GO

/* Stock corrections can never go below what is reserved for requisitions. */
CREATE OR ALTER PROCEDURE dbo.usp_InvAdjustStock
    @ProductId INT, @QtyChange INT, @Reason NVARCHAR(100), @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Reason = LTRIM(RTRIM(ISNULL(@Reason, N'')));
    IF @QtyChange = 0 THROW 50111, 'Quantity cannot be zero.', 1;
    IF @Reason = N''  THROW 50112, 'A reason is required.', 1;

    DECLARE @Current INT, @Reserved INT;

    BEGIN TRAN;

    SELECT @Current = StockQty, @Reserved = ReservedQty
    FROM dbo.Products WITH (UPDLOCK, ROWLOCK) WHERE ProductId = @ProductId;

    IF @Current IS NULL THROW 50113, 'The product was not found.', 1;
    IF @Current + @QtyChange < 0
        THROW 50114, 'Not enough stock. You cannot remove more than the current stock.', 1;
    IF @Current + @QtyChange < @Reserved
        THROW 50115, 'That would take stock below the quantity reserved for approved requisitions.', 1;

    UPDATE dbo.Products SET StockQty = @Current + @QtyChange WHERE ProductId = @ProductId;

    INSERT INTO dbo.StockMovements (ProductId, QtyChange, StockAfter, Reason, CreatedById)
    VALUES (@ProductId, @QtyChange, @Current + @QtyChange, @Reason, @UserId);

    COMMIT TRAN;
    SELECT @Current + @QtyChange AS NewStock;
END
GO

/* Approved requisitions that still have something to issue */
CREATE OR ALTER PROCEDURE dbo.usp_IssGetRequisitions
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.RequisitionId, r.RefNo, r.Branch, r.DateCreated, u.FullName AS RequestedBy,
           SUM(i.Quantity) AS Requested, SUM(i.ReservedQty) AS Reserved, SUM(i.IssuedQty) AS Issued
    FROM dbo.Requisitions r
    JOIN dbo.AppUsers u ON u.UserId = r.RequestedById
    JOIN dbo.RequisitionItems i ON i.RequisitionId = r.RequisitionId
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
    WHERE i.RequisitionId = @RequisitionId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* Reserve whatever stock is available for the requisition (run it again after a delivery arrives). */
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
        WHERE i.RequisitionId = @RequisitionId;

        UPDATE p SET p.ReservedQty = p.ReservedQty + t.Take
        FROM dbo.Products p JOIN @T t ON t.ProductId = p.ProductId WHERE t.Take > 0;

        UPDATE i SET i.ReservedQty = i.ReservedQty + t.Take
        FROM dbo.RequisitionItems i JOIN @T t ON t.ItemId = i.ItemId WHERE t.Take > 0;

    COMMIT;

    SELECT ISNULL((SELECT SUM(Take) FROM @T), 0) AS Reserved,
           (SELECT COUNT(*) FROM dbo.RequisitionItems
            WHERE RequisitionId = @RequisitionId AND Quantity - IssuedQty - ReservedQty > 0) AS ShortLines;
END
GO

/* Issue the reserved quantities: slip + stock deduction + requisition closed when complete. */
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
    IF NOT EXISTS (SELECT 1 FROM dbo.RequisitionItems WHERE RequisitionId = @RequisitionId AND ReservedQty > 0)
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
        FROM dbo.RequisitionItems WHERE RequisitionId = @RequisitionId AND ReservedQty > 0;

        -- deduct stock and release the reservation in the same statement
        UPDATE p SET p.StockQty = p.StockQty - i.ReservedQty, p.ReservedQty = p.ReservedQty - i.ReservedQty
        FROM dbo.Products p
        JOIN dbo.RequisitionItems i ON i.ProductId = p.ProductId
        WHERE i.RequisitionId = @RequisitionId AND i.ReservedQty > 0;

        INSERT INTO dbo.StockMovements (ProductId, QtyChange, StockAfter, Reason, CreatedById)
        SELECT i.ProductId, -i.ReservedQty, p.StockQty, N'Issued ' + @IssueNo, @UserId
        FROM dbo.RequisitionItems i JOIN dbo.Products p ON p.ProductId = i.ProductId
        WHERE i.RequisitionId = @RequisitionId AND i.ReservedQty > 0;

        UPDATE dbo.RequisitionItems SET IssuedQty = IssuedQty + ReservedQty, ReservedQty = 0
        WHERE RequisitionId = @RequisitionId AND ReservedQty > 0;

        DECLARE @Closed BIT = 0;
        IF NOT EXISTS (SELECT 1 FROM dbo.RequisitionItems WHERE RequisitionId = @RequisitionId AND IssuedQty < Quantity)
        BEGIN
            UPDATE dbo.Requisitions SET [Status] = N'Closed' WHERE RequisitionId = @RequisitionId;
            SET @Closed = 1;
        END

    COMMIT;

    SELECT @IssueNo AS IssueNo, @Closed AS Closed;
END
GO

/* Issuance slips. Cashiers only see slips for their own branch. */
CREATE OR ALTER PROCEDURE dbo.usp_IssGetSlips
    @ViewerId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Branch NVARCHAR(50) = (SELECT CASE WHEN [Role] = N'Cashier' THEN Branch ELSE NULL END
                                    FROM dbo.AppUsers WHERE UserId = @ViewerId);
    SELECT TOP (200)
           s.IssuanceId, s.IssueNo, r.RefNo AS RequisitionRef, s.Branch, s.DateIssued,
           u.FullName AS IssuedBy,
           (SELECT ISNULL(SUM(Quantity), 0) FROM dbo.IssuanceItems WHERE IssuanceId = s.IssuanceId) AS TotalQty,
           s.[Status], ISNULL(s.ReceivedBy, N'') AS ReceivedBy, s.DateAcknowledged
    FROM dbo.Issuances s
    JOIN dbo.Requisitions r ON r.RequisitionId = s.RequisitionId
    JOIN dbo.AppUsers u ON u.UserId = s.IssuedById
    WHERE @Branch IS NULL OR s.Branch = @Branch
    ORDER BY s.DateIssued DESC, s.IssuanceId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_IssGetSlipItems
    @IssuanceId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size, i.Quantity
    FROM dbo.IssuanceItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE i.IssuanceId = @IssuanceId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* The receiving branch signs for the goods (cashier or manager of that branch). */
CREATE OR ALTER PROCEDURE dbo.usp_IssAcknowledge
    @IssuanceId INT, @UserId INT, @ReceivedBy NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Role NVARCHAR(20), @UBranch NVARCHAR(50);
    SELECT @Role = [Role], @UBranch = Branch FROM dbo.AppUsers WHERE UserId = @UserId;
    IF @Role IS NULL OR @Role NOT IN (N'Cashier', N'Manager')
        THROW 52010, 'Only branch staff can acknowledge receipt.', 1;

    SET @ReceivedBy = LTRIM(RTRIM(ISNULL(@ReceivedBy, N'')));
    IF @ReceivedBy = N'' THROW 52011, 'Enter the name of the person who received the items.', 1;

    DECLARE @Branch NVARCHAR(50), @Status NVARCHAR(20);
    SELECT @Branch = Branch, @Status = [Status] FROM dbo.Issuances WHERE IssuanceId = @IssuanceId;
    IF @Branch IS NULL THROW 52012, 'The issuance slip was not found.', 1;
    IF @Branch <> @UBranch THROW 52013, 'Only staff of the receiving branch can acknowledge this slip.', 1;
    IF @Status <> N'Issued' THROW 52014, 'This slip was already acknowledged.', 1;

    UPDATE dbo.Issuances
    SET [Status] = N'Acknowledged', AcknowledgedById = @UserId, ReceivedBy = @ReceivedBy, DateAcknowledged = GETDATE()
    WHERE IssuanceId = @IssuanceId AND [Status] = N'Issued';
END
GO

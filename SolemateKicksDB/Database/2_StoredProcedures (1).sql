
USE SolemateKicksDB;
GO

/* ---------------------------------------------------------------
   LOGIN
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Authenticate
    @Username     NVARCHAR(50),
    @PasswordHash NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, Username, PasswordHash, FullName, [Role], Branch
    FROM dbo.AppUsers
    WHERE Username = @Username AND PasswordHash = @PasswordHash;
END
GO

/* ---------------------------------------------------------------
   CATALOG
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetBrands
AS
BEGIN
    SET NOCOUNT ON;
    SELECT BrandId, BrandName FROM dbo.Brands ORDER BY BrandName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetCatalog
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.ProductId,
           b.BrandId,
           p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           p.Price,
           p.StockQty    AS Stock,
           p.ReorderLevel
    FROM dbo.Products p
    JOIN dbo.Brands b ON b.BrandId = p.BrandId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* ---------------------------------------------------------------
   CREATE REQUISITION
   @ItemsXml looks like: <Items><Item ProductId="5" Quantity="3"/><Item .../></Items>
   Everything happens in ONE transaction. Returns the new reference number.
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_CreateRequisition
    @UserId   INT,
    @Remarks  NVARCHAR(200) = NULL,
    @ItemsXml NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;     -- any error rolls the whole transaction back

    DECLARE @Role NVARCHAR(20), @Branch NVARCHAR(50);
    SELECT @Role = [Role], @Branch = Branch FROM dbo.AppUsers WHERE UserId = @UserId;

    IF @Role IS NULL
        THROW 50001, 'User not found.', 1;
    IF @Role NOT IN (N'Cashier', N'Manager')
        THROW 50002, 'Only cashiers and managers can create requisitions.', 1;

    -- shred the XML; merge duplicate products
    DECLARE @x XML = CAST(@ItemsXml AS XML);
    DECLARE @Items TABLE (ProductId INT NOT NULL PRIMARY KEY, Quantity INT NOT NULL);

    INSERT INTO @Items (ProductId, Quantity)
    SELECT ProductId, SUM(Quantity)
    FROM (SELECT t.i.value('@ProductId', 'int') AS ProductId,
                 t.i.value('@Quantity',  'int') AS Quantity
          FROM @x.nodes('/Items/Item') AS t(i)) s
    GROUP BY ProductId;

    IF NOT EXISTS (SELECT 1 FROM @Items)
        THROW 50003, 'Add at least one item to the requisition.', 1;
    IF EXISTS (SELECT 1 FROM @Items WHERE Quantity < 1)
        THROW 50004, 'Every quantity must be at least 1.', 1;
    IF EXISTS (SELECT 1 FROM @Items i LEFT JOIN dbo.Products p ON p.ProductId = i.ProductId WHERE p.ProductId IS NULL)
        THROW 50005, 'One of the selected products no longer exists.', 1;

    BEGIN TRAN;

        -- reference number: REQ-yyyyMMdd-0001, 0002 ...
        DECLARE @Prefix NVARCHAR(30) = N'REQ-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next   INT = (SELECT COUNT(*) FROM dbo.Requisitions WITH (UPDLOCK, HOLDLOCK)
                               WHERE RefNo LIKE @Prefix + N'%') + 1;
        DECLARE @RefNo  NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.Requisitions (RefNo, RequestedById, Branch, DateCreated, [Status], Remarks)
        VALUES (@RefNo, @UserId, @Branch, GETDATE(), N'Pending', NULLIF(LTRIM(RTRIM(@Remarks)), N''));

        DECLARE @Id INT = SCOPE_IDENTITY();

        INSERT INTO dbo.RequisitionItems (RequisitionId, ProductId, Quantity)
        SELECT @Id, ProductId, Quantity FROM @Items;

    COMMIT;

    SELECT @RefNo AS RefNo;
END
GO

/* ---------------------------------------------------------------
   REVISE REQUISITION (requester only; Pending or Rejected -> back to Pending)
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_ReviseRequisition
    @RequisitionId INT,
    @UserId        INT,
    @Remarks       NVARCHAR(200) = NULL,
    @ItemsXml      NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Owner INT, @Status NVARCHAR(20);
    SELECT @Owner = RequestedById, @Status = [Status]
    FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId;

    IF @Owner IS NULL
        THROW 50010, 'Requisition not found.', 1;
    IF @Owner <> @UserId
        THROW 50011, 'Only the requester can revise this requisition.', 1;
    IF @Status NOT IN (N'Pending', N'Rejected')
        THROW 50012, 'Only pending or rejected requisitions can be revised.', 1;

    DECLARE @x XML = CAST(@ItemsXml AS XML);
    DECLARE @Items TABLE (ProductId INT NOT NULL PRIMARY KEY, Quantity INT NOT NULL);

    INSERT INTO @Items (ProductId, Quantity)
    SELECT ProductId, SUM(Quantity)
    FROM (SELECT t.i.value('@ProductId', 'int') AS ProductId,
                 t.i.value('@Quantity',  'int') AS Quantity
          FROM @x.nodes('/Items/Item') AS t(i)) s
    GROUP BY ProductId;

    IF NOT EXISTS (SELECT 1 FROM @Items)
        THROW 50003, 'Add at least one item to the requisition.', 1;
    IF EXISTS (SELECT 1 FROM @Items WHERE Quantity < 1)
        THROW 50004, 'Every quantity must be at least 1.', 1;
    IF EXISTS (SELECT 1 FROM @Items i LEFT JOIN dbo.Products p ON p.ProductId = i.ProductId WHERE p.ProductId IS NULL)
        THROW 50005, 'One of the selected products no longer exists.', 1;

    BEGIN TRAN;

        DELETE FROM dbo.RequisitionItems WHERE RequisitionId = @RequisitionId;

        INSERT INTO dbo.RequisitionItems (RequisitionId, ProductId, Quantity)
        SELECT @RequisitionId, ProductId, Quantity FROM @Items;

        UPDATE dbo.Requisitions
        SET [Status]     = N'Pending',
            Remarks      = NULLIF(LTRIM(RTRIM(@Remarks)), N''),
            RejectReason = NULL,
            ReviewedById = NULL,
            DateReviewed = NULL,
            StockCheck   = NULL
        WHERE RequisitionId = @RequisitionId;

    COMMIT;
END
GO

/* ---------------------------------------------------------------
   CANCEL REQUISITION (requester only)
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_CancelRequisition
    @RequisitionId INT,
    @UserId        INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Owner INT, @Status NVARCHAR(20);
    SELECT @Owner = RequestedById, @Status = [Status]
    FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId;

    IF @Owner IS NULL
        THROW 50010, 'Requisition not found.', 1;
    IF @Owner <> @UserId
        THROW 50013, 'Only the requester can cancel this requisition.', 1;
    IF @Status NOT IN (N'Pending', N'Rejected')
        THROW 50014, 'Only pending or rejected requisitions can be cancelled.', 1;

    UPDATE dbo.Requisitions SET [Status] = N'Cancelled' WHERE RequisitionId = @RequisitionId;
END
GO

/* ---------------------------------------------------------------
   APPROVE (head only) + automatic stock check
   Returns one row per item with requested vs. available quantity.
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
         WHERE i.RequisitionId = @RequisitionId AND p.StockQty < i.Quantity);

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
           p.StockQty AS Available
    FROM dbo.RequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    WHERE i.RequisitionId = @RequisitionId
    ORDER BY p.Sku;
END
GO

/* ---------------------------------------------------------------
   REJECT (head only, reason required)
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_RejectRequisition
    @RequisitionId INT,
    @HeadId        INT,
    @Reason        NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @HeadId AND [Role] = N'Head')
        THROW 50020, 'Only the head can approve or reject requisitions.', 1;
    IF @Reason IS NULL OR LTRIM(RTRIM(@Reason)) = N''
        THROW 50022, 'A reason is required when rejecting a requisition.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId)
        THROW 50010, 'Requisition not found.', 1;

    UPDATE dbo.Requisitions
    SET [Status] = N'Rejected', ReviewedById = @HeadId, DateReviewed = GETDATE(),
        RejectReason = LTRIM(RTRIM(@Reason))
    WHERE RequisitionId = @RequisitionId AND [Status] = N'Pending';

    IF @@ROWCOUNT = 0
        THROW 50021, 'This requisition is no longer pending.', 1;
END
GO

/* ---------------------------------------------------------------
   LISTS FOR THE MAIN SCREEN
   Cashiers only see their own requisitions (enforced here, not in C#).
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetRequisitions
    @ViewerId INT,
    @Status   NVARCHAR(20) = NULL      -- NULL or 'All' = every status
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OnlyMine BIT =
        CASE WHEN EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @ViewerId AND [Role] = N'Cashier')
             THEN 1 ELSE 0 END;

    SELECT r.RequisitionId,
           r.RefNo,
           r.DateCreated,
           r.RequestedById,
           u.FullName          AS RequestedBy,
           r.Branch,
           r.[Status],
           ISNULL(t.Lines, 0)  AS Lines,
           ISNULL(t.Qty, 0)    AS TotalQty,
           rv.FullName         AS ReviewedBy,
           r.Remarks,
           r.RejectReason,
           r.StockCheck
    FROM dbo.Requisitions r
    JOIN dbo.AppUsers u       ON u.UserId  = r.RequestedById
    LEFT JOIN dbo.AppUsers rv ON rv.UserId = r.ReviewedById
    LEFT JOIN (SELECT RequisitionId, COUNT(*) AS Lines, SUM(Quantity) AS Qty
               FROM dbo.RequisitionItems GROUP BY RequisitionId) t
           ON t.RequisitionId = r.RequisitionId
    WHERE (@OnlyMine = 0 OR r.RequestedById = @ViewerId)
      AND (@Status IS NULL OR @Status = N'All' OR r.[Status] = @Status)
    ORDER BY r.DateCreated DESC, r.RequisitionId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetRequisitionLines
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           i.Quantity,
           p.StockQty    AS Available
    FROM dbo.RequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE i.RequisitionId = @RequisitionId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* ---------------------------------------------------------------
   DATA FOR RE-OPENING A REQUISITION (revise)
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetRequisitionHeader
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RefNo, Remarks, [Status] FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetRevisionLines
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.ProductId,
           p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           p.StockQty    AS Stock,
           i.Quantity
    FROM dbo.RequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE i.RequisitionId = @RequisitionId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* ---------------------------------------------------------------
   BANNER COUNTS (returned with RETURN so C# reads them as an int)
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetPendingCount
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @n INT = (SELECT COUNT(*) FROM dbo.Requisitions WHERE [Status] = N'Pending');
    RETURN @n;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_GetRejectedCount
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @n INT = (SELECT COUNT(*) FROM dbo.Requisitions WHERE RequestedById = @UserId AND [Status] = N'Rejected');
    RETURN @n;
END
GO

/* ---------------------------------------------------------------
   CHECK: list the procedures that were created (should show 14)
   --------------------------------------------------------------- */
SELECT name AS StoredProcedure FROM sys.procedures WHERE name LIKE 'usp[_]%' ORDER BY name;
GO

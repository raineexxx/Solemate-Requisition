USE SolemateKicksDB;
GO

/* =====================================================================
   Requisitions - the Head approves or rejects EACH ITEM, and Purchasing only sees approved items

   Run AFTER 13_SelectItems.sql. Safe to run more than once.

   How it works
   - Every requisition item has its own decision: Pending, Approved or Rejected (a rejected item has a reason).
   - The requisition becomes Approved when at least one item is approved (rejected items stay marked as rejected),
     and Rejected when every item is rejected.
   - Purchasing (+ NEW PO) lists only approved items that still have quantity left to order.
   - Old requisitions are filled in: items of Approved requisitions count as approved, items of Rejected ones as rejected.
   - usp_ApproveRequisition and usp_RejectRequisition keep their old behaviour (whole request) and now also set the items.
   ===================================================================== */

IF COL_LENGTH(N'dbo.RequisitionItems', N'ItemStatus') IS NULL
    ALTER TABLE dbo.RequisitionItems
        ADD ItemStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_ReqItems_Status DEFAULT N'Pending'
            CONSTRAINT CK_ReqItems_Status CHECK (ItemStatus IN (N'Pending', N'Approved', N'Rejected'));
IF COL_LENGTH(N'dbo.RequisitionItems', N'ItemRejectReason') IS NULL
    ALTER TABLE dbo.RequisitionItems ADD ItemRejectReason NVARCHAR(200) NULL;
GO

/* requisitions decided before this script: copy the whole-request decision to their items */
UPDATE ri
SET ri.ItemStatus = r.[Status],
    ri.ItemRejectReason = CASE WHEN r.[Status] = N'Rejected' THEN r.RejectReason ELSE ri.ItemRejectReason END
FROM dbo.RequisitionItems ri
JOIN dbo.Requisitions r ON r.RequisitionId = ri.RequisitionId
WHERE ri.ItemStatus = N'Pending' AND r.[Status] IN (N'Approved', N'Rejected');
GO

/* Purchasing: only APPROVED items of approved requisitions can be ordered */
CREATE OR ALTER VIEW dbo.vw_PurPendingItems
AS
SELECT ri.ItemId AS RequisitionItemId,
       ri.RequisitionId,
       r.RefNo,
       r.Branch,
       r.DateCreated,
       ri.ProductId,
       p.ProductName,
       ri.Quantity AS RequestedQty,
       CASE
           -- an old PO (made before item selection existed) covered the whole requisition
           WHEN EXISTS (SELECT 1 FROM dbo.PurchaseOrders po
                        WHERE po.RequisitionId = ri.RequisitionId AND po.[Status] <> N'Cancelled'
                          AND NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrderItems x
                                          WHERE x.PurchaseOrderId = po.PurchaseOrderId AND x.RequisitionItemId IS NOT NULL))
               THEN ri.Quantity
           ELSE ISNULL((SELECT SUM(x.Quantity)
                        FROM dbo.PurchaseOrderItems x
                        JOIN dbo.PurchaseOrders po ON po.PurchaseOrderId = x.PurchaseOrderId
                        WHERE x.RequisitionItemId = ri.ItemId AND po.[Status] <> N'Cancelled'), 0)
       END AS OrderedQty,
       pr.Price AS UnitPrice
FROM dbo.RequisitionItems ri
JOIN dbo.Requisitions r ON r.RequisitionId = ri.RequisitionId AND r.[Status] = N'Approved'
JOIN dbo.vw_PurProducts p ON p.ProductId = ri.ProductId
JOIN dbo.Products pr ON pr.ProductId = ri.ProductId
WHERE ri.ItemStatus = N'Approved';
GO

/* Items of one requisition with their decision (for the review window) */
CREATE OR ALTER PROCEDURE dbo.usp_GetReviewLines
    @RequisitionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT i.ItemId AS RequisitionItemId,
           p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           i.Quantity,
           p.StockQty    AS Available,
           i.ItemStatus,
           ISNULL(i.ItemRejectReason, N'') AS ItemRejectReason
    FROM dbo.RequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE i.RequisitionId = @RequisitionId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* The Head saves the decision for every item of a pending requisition. Head only.
   @Decisions is XML text: <items><d i="ItemId" s="Approved|Rejected" r="reason"/>...</items>
   A rejected item needs a reason. Returns one row: RefNo, Status, ApprovedCount, RejectedCount, ShortCount. */
CREATE OR ALTER PROCEDURE dbo.usp_ReviewRequisition
    @RequisitionId INT,
    @HeadId        INT,
    @Decisions     NVARCHAR(MAX)
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

    DECLARE @d TABLE (ItemId INT NOT NULL PRIMARY KEY, Decision NVARCHAR(20) NOT NULL, Reason NVARCHAR(200) NULL);
    DECLARE @x XML = CAST(@Decisions AS XML);

    INSERT INTO @d (ItemId, Decision, Reason)
    SELECT n.value('@i', 'int'),
           n.value('@s', 'nvarchar(20)'),
           NULLIF(LTRIM(RTRIM(n.value('@r', 'nvarchar(200)'))), N'')
    FROM @x.nodes('/items/d') AS t(n);

    IF EXISTS (SELECT 1 FROM @d WHERE Decision NOT IN (N'Approved', N'Rejected'))
        THROW 50041, 'Each item must be approved or rejected.', 1;
    IF EXISTS (SELECT 1 FROM @d x WHERE NOT EXISTS (SELECT 1 FROM dbo.RequisitionItems ri
                                                    WHERE ri.ItemId = x.ItemId AND ri.RequisitionId = @RequisitionId))
        THROW 50042, 'An item does not belong to this requisition.', 1;
    IF EXISTS (SELECT 1 FROM dbo.RequisitionItems ri
               WHERE ri.RequisitionId = @RequisitionId
                 AND NOT EXISTS (SELECT 1 FROM @d x WHERE x.ItemId = ri.ItemId))
        THROW 50040, 'Decide every item of the requisition (approve or reject each one).', 1;
    IF EXISTS (SELECT 1 FROM @d WHERE Decision = N'Rejected' AND Reason IS NULL)
        THROW 50022, 'A reason is required for every rejected item.', 1;

    BEGIN TRAN;

    UPDATE ri
    SET ri.ItemStatus = x.Decision,
        ri.ItemRejectReason = CASE WHEN x.Decision = N'Rejected' THEN x.Reason ELSE NULL END
    FROM dbo.RequisitionItems ri
    JOIN @d x ON x.ItemId = ri.ItemId;

    DECLARE @Approved INT = (SELECT COUNT(*) FROM @d WHERE Decision = N'Approved');
    DECLARE @Rejected INT = (SELECT COUNT(*) FROM @d WHERE Decision = N'Rejected');
    DECLARE @Short INT =
        (SELECT COUNT(*)
         FROM dbo.RequisitionItems ri
         JOIN dbo.Products p ON p.ProductId = ri.ProductId
         WHERE ri.RequisitionId = @RequisitionId AND ri.ItemStatus = N'Approved' AND p.StockQty < ri.Quantity);

    DECLARE @NewStatus NVARCHAR(20) = CASE WHEN @Approved > 0 THEN N'Approved' ELSE N'Rejected' END;

    -- one line for the requester: "SKU: reason; SKU: reason"
    DECLARE @Reasons NVARCHAR(MAX) =
        STUFF((SELECT N'; ' + p.Sku + N': ' + x.Reason
               FROM @d x
               JOIN dbo.RequisitionItems ri ON ri.ItemId = x.ItemId
               JOIN dbo.Products p ON p.ProductId = ri.ProductId
               WHERE x.Decision = N'Rejected'
               ORDER BY p.Sku
               FOR XML PATH(N''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'');

    DECLARE @Check NVARCHAR(200) =
        CASE WHEN @Approved = 0 THEN NULL
             WHEN @Short = 0 THEN N'Stock sufficient for all approved items'
             ELSE CAST(@Short AS NVARCHAR(10)) + N' approved item(s) short on stock - purchase needed' END;
    IF @Approved > 0 AND @Rejected > 0
        SET @Check = LEFT(@Check + N' | ' + CAST(@Rejected AS NVARCHAR(10)) + N' item(s) rejected', 200);

    UPDATE dbo.Requisitions
    SET [Status] = @NewStatus,
        ReviewedById = @HeadId,
        DateReviewed = GETDATE(),
        RejectReason = LEFT(@Reasons, 200),
        StockCheck = @Check
    WHERE RequisitionId = @RequisitionId AND [Status] = N'Pending';

    IF @@ROWCOUNT = 0
        THROW 50021, 'This requisition is no longer pending.', 1;

    COMMIT;

    SELECT @RefNo AS RefNo, @NewStatus AS [Status], @Approved AS ApprovedCount,
           @Rejected AS RejectedCount, @Short AS ShortCount;
END
GO

/* Whole-request approve (old behaviour) - now also marks every item approved */
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

    DECLARE @Short INT =
        (SELECT COUNT(*)
         FROM dbo.RequisitionItems i
         JOIN dbo.Products p ON p.ProductId = i.ProductId
         WHERE i.RequisitionId = @RequisitionId AND p.StockQty < i.Quantity);

    DECLARE @Check NVARCHAR(200) =
        CASE WHEN @Short = 0 THEN N'Stock sufficient for all items'
             ELSE CAST(@Short AS NVARCHAR(10)) + N' item(s) short on stock - purchase requisition needed' END;

    BEGIN TRAN;

    UPDATE dbo.Requisitions
    SET [Status] = N'Approved', ReviewedById = @HeadId, DateReviewed = GETDATE(), StockCheck = @Check
    WHERE RequisitionId = @RequisitionId AND [Status] = N'Pending';

    IF @@ROWCOUNT = 0
        THROW 50021, 'This requisition is no longer pending.', 1;

    UPDATE dbo.RequisitionItems SET ItemStatus = N'Approved', ItemRejectReason = NULL
    WHERE RequisitionId = @RequisitionId;

    COMMIT;

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

/* Whole-request reject (old behaviour) - now also marks every item rejected */
CREATE OR ALTER PROCEDURE dbo.usp_RejectRequisition
    @RequisitionId INT,
    @HeadId        INT,
    @Reason        NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @HeadId AND [Role] = N'Head')
        THROW 50020, 'Only the head can approve or reject requisitions.', 1;
    IF @Reason IS NULL OR LTRIM(RTRIM(@Reason)) = N''
        THROW 50022, 'A reason is required when rejecting a requisition.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId)
        THROW 50010, 'Requisition not found.', 1;

    BEGIN TRAN;

    UPDATE dbo.Requisitions
    SET [Status] = N'Rejected', ReviewedById = @HeadId, DateReviewed = GETDATE(),
        RejectReason = LTRIM(RTRIM(@Reason))
    WHERE RequisitionId = @RequisitionId AND [Status] = N'Pending';

    IF @@ROWCOUNT = 0
        THROW 50021, 'This requisition is no longer pending.', 1;

    UPDATE dbo.RequisitionItems SET ItemStatus = N'Rejected', ItemRejectReason = LTRIM(RTRIM(@Reason))
    WHERE RequisitionId = @RequisitionId;

    COMMIT;
END
GO

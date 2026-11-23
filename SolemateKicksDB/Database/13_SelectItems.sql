USE SolemateKicksDB;
GO


IF COL_LENGTH(N'dbo.PurchaseOrderItems', N'RequisitionItemId') IS NULL
    ALTER TABLE dbo.PurchaseOrderItems ADD RequisitionItemId INT NULL;
GO

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
           -- an old PO (made before this script) covered the whole requisition
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
JOIN dbo.Products pr ON pr.ProductId = ri.ProductId;
GO

/* Reference numbers / branches of a PO (several when the PO mixes requests) */
CREATE OR ALTER FUNCTION dbo.fn_PurReqRefs (@PurchaseOrderId INT)
RETURNS NVARCHAR(400)
AS
BEGIN
    DECLARE @s NVARCHAR(400) =
        STUFF((SELECT DISTINCT N', ' + r.RefNo
               FROM dbo.PurchaseOrderItems x
               JOIN dbo.RequisitionItems ri ON ri.ItemId = x.RequisitionItemId
               JOIN dbo.Requisitions r ON r.RequisitionId = ri.RequisitionId
               WHERE x.PurchaseOrderId = @PurchaseOrderId
               FOR XML PATH(N''), TYPE).value('.', 'nvarchar(400)'), 1, 2, N'');
    IF @s IS NULL
        SET @s = ISNULL((SELECT r.RefNo FROM dbo.PurchaseOrders po
                         JOIN dbo.Requisitions r ON r.RequisitionId = po.RequisitionId
                         WHERE po.PurchaseOrderId = @PurchaseOrderId), N'');
    RETURN @s;
END
GO

CREATE OR ALTER FUNCTION dbo.fn_PurBranches (@PurchaseOrderId INT)
RETURNS NVARCHAR(400)
AS
BEGIN
    DECLARE @s NVARCHAR(400) =
        STUFF((SELECT DISTINCT N', ' + r.Branch
               FROM dbo.PurchaseOrderItems x
               JOIN dbo.RequisitionItems ri ON ri.ItemId = x.RequisitionItemId
               JOIN dbo.Requisitions r ON r.RequisitionId = ri.RequisitionId
               WHERE x.PurchaseOrderId = @PurchaseOrderId
               FOR XML PATH(N''), TYPE).value('.', 'nvarchar(400)'), 1, 2, N'');
    IF @s IS NULL
        SET @s = ISNULL((SELECT r.Branch FROM dbo.PurchaseOrders po
                         JOIN dbo.Requisitions r ON r.RequisitionId = po.RequisitionId
                         WHERE po.PurchaseOrderId = @PurchaseOrderId), N'');
    RETURN @s;
END
GO

/* Items still waiting to be ordered (remaining quantity > 0), oldest request first */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetPendingItems
AS
BEGIN
    SET NOCOUNT ON;
    SELECT RequisitionItemId, RequisitionId, RefNo, Branch, ProductId, ProductName,
           RequestedQty, OrderedQty,
           RequestedQty - OrderedQty AS RemainingQty,
           UnitPrice
    FROM dbo.vw_PurPendingItems
    WHERE RequestedQty - OrderedQty > 0
    ORDER BY DateCreated, RefNo, ProductName;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurCreateOrder
    @SupplierId   INT,
    @PaymentTerms NVARCHAR(100),
    @Items        NVARCHAR(MAX),
    @UserId       INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @PaymentTerms = LTRIM(RTRIM(ISNULL(@PaymentTerms, N'')));

    DECLARE @Supplier NVARCHAR(100) =
        (SELECT SupplierName FROM dbo.Suppliers WHERE SupplierId = @SupplierId AND IsActive = 1);

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Head')
        THROW 50006, N'Only the Head can create a purchase order.', 1;
    IF @Supplier IS NULL
        THROW 50001, N'Please choose an active supplier.', 1;

    DECLARE @it TABLE (RequisitionItemId INT NOT NULL PRIMARY KEY, Qty INT NOT NULL);
    DECLARE @x XML = CAST(@Items AS XML);

    INSERT INTO @it (RequisitionItemId, Qty)
    SELECT n.value('@r', 'int'), n.value('@q', 'int')
    FROM @x.nodes('/items/i') AS t(n);

    IF NOT EXISTS (SELECT 1 FROM @it)
        THROW 50030, N'Select at least one item to order.', 1;
    IF EXISTS (SELECT 1 FROM @it WHERE Qty < 1)
        THROW 50031, N'The quantity to order must be at least 1.', 1;
    IF EXISTS (SELECT 1 FROM @it i
               LEFT JOIN dbo.vw_PurPendingItems v ON v.RequisitionItemId = i.RequisitionItemId
               WHERE v.RequisitionItemId IS NULL OR i.Qty > v.RequestedQty - v.OrderedQty)
        THROW 50032, N'An item is not approved, or the quantity is more than what is left to order. Refresh and try again.', 1;

    -- the PO points to one requisition only when all its items come from the same request
    DECLARE @ReqId INT, @ReqCount INT;
    SELECT @ReqId = MIN(v.RequisitionId), @ReqCount = COUNT(DISTINCT v.RequisitionId)
    FROM @it i
    JOIN dbo.vw_PurPendingItems v ON v.RequisitionItemId = i.RequisitionItemId;
    IF @ReqCount > 1 SET @ReqId = NULL;

    BEGIN TRAN;

    DECLARE @seq INT = (SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE CAST(DateCreated AS DATE) = CAST(GETDATE() AS DATE)) + 1;
    DECLARE @PoNo NVARCHAR(30) = N'PO-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-' + RIGHT(N'0000' + CAST(@seq AS NVARCHAR(10)), 4);

    INSERT INTO dbo.PurchaseOrders (PoNo, RequisitionId, Supplier, SupplierId, CreatedById, PaymentTerms)
    VALUES (@PoNo, @ReqId, @Supplier, @SupplierId, @UserId, NULLIF(@PaymentTerms, N''));

    DECLARE @PoId INT = SCOPE_IDENTITY();

    INSERT INTO dbo.PurchaseOrderItems (PurchaseOrderId, ProductId, Quantity, UnitCost, RequisitionItemId)
    SELECT @PoId, v.ProductId, i.Qty, v.UnitPrice, v.RequisitionItemId
    FROM @it i
    JOIN dbo.vw_PurPendingItems v ON v.RequisitionItemId = i.RequisitionItemId;

    COMMIT;
    SELECT @PoId;
END
GO

/* List of purchase orders */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetOrders
AS
BEGIN
    SET NOCOUNT ON;
    SELECT po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status],
           CAST(ISNULL(SUM(i.Quantity * i.UnitCost), 0) AS DECIMAL(12,2)) AS Total,
           dbo.fn_PurReqRefs(po.PurchaseOrderId) AS RequisitionRef,
           ISNULL(po.PaymentTerms, N'') AS PaymentTerms,
           po.DateSent
    FROM dbo.PurchaseOrders po
    LEFT JOIN dbo.PurchaseOrderItems i ON i.PurchaseOrderId = po.PurchaseOrderId
    GROUP BY po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status], po.PaymentTerms, po.DateSent
    ORDER BY po.DateCreated DESC, po.PurchaseOrderId DESC;
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
           dbo.fn_PurReqRefs(po.PurchaseOrderId) AS RequisitionRef,
           dbo.fn_PurBranches(po.PurchaseOrderId) AS Branch,
           po.[Status],
           CAST(ISNULL((SELECT SUM(Quantity * UnitCost) FROM dbo.PurchaseOrderItems
                        WHERE PurchaseOrderId = po.PurchaseOrderId), 0) AS DECIMAL(12,2)) AS Total
    FROM dbo.PurchaseOrders po
    LEFT JOIN dbo.Suppliers s ON s.SupplierId = po.SupplierId
    WHERE po.PurchaseOrderId = @PurchaseOrderId;
END
GO

/* Order history of one supplier */
CREATE OR ALTER PROCEDURE dbo.usp_SupOrderHistory
    @SupplierId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Name NVARCHAR(100) = (SELECT SupplierName FROM dbo.Suppliers WHERE SupplierId = @SupplierId);

    SELECT po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status],
           CAST(ISNULL(SUM(i.Quantity * i.UnitCost), 0) AS DECIMAL(12,2)) AS Total,
           dbo.fn_PurReqRefs(po.PurchaseOrderId) AS RequisitionRef,
           ISNULL(po.PaymentTerms, N'') AS PaymentTerms,
           po.DateSent
    FROM dbo.PurchaseOrders po
    LEFT JOIN dbo.PurchaseOrderItems i ON i.PurchaseOrderId = po.PurchaseOrderId
    WHERE po.SupplierId = @SupplierId
       OR (po.SupplierId IS NULL AND po.Supplier = @Name)
    GROUP BY po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status], po.PaymentTerms, po.DateSent
    ORDER BY po.DateCreated DESC, po.PurchaseOrderId DESC;
END
GO

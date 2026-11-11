USE SolemateKicksDB;
GO

/* ---------------- TABLES ---------------- */
IF OBJECT_ID(N'dbo.PurchaseOrders') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseOrders (
        PurchaseOrderId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseOrders PRIMARY KEY,
        PoNo            NVARCHAR(30)  NOT NULL CONSTRAINT UQ_PurchaseOrders_PoNo UNIQUE,   -- PO-yyyyMMdd-0001
        RequisitionId   INT NULL CONSTRAINT FK_PO_Req REFERENCES dbo.Requisitions(RequisitionId),
        Supplier        NVARCHAR(100) NOT NULL,
        CreatedById     INT NOT NULL CONSTRAINT FK_PO_CreatedBy REFERENCES dbo.AppUsers(UserId),
        DateCreated     DATETIME NOT NULL CONSTRAINT DF_PO_Date DEFAULT GETDATE(),
        [Status]        NVARCHAR(20) NOT NULL CONSTRAINT DF_PO_Status DEFAULT N'Ordered'
                        CONSTRAINT CK_PO_Status CHECK ([Status] IN (N'Ordered', N'Received', N'Cancelled')),
        DateReceived    DATETIME NULL,
        ReceivedById    INT NULL CONSTRAINT FK_PO_ReceivedBy REFERENCES dbo.AppUsers(UserId)
    );
END
GO

IF OBJECT_ID(N'dbo.PurchaseOrderItems') IS NULL
BEGIN
    CREATE TABLE dbo.PurchaseOrderItems (
        ItemId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PurchaseOrderItems PRIMARY KEY,
        PurchaseOrderId INT NOT NULL CONSTRAINT FK_POItems_PO REFERENCES dbo.PurchaseOrders(PurchaseOrderId) ON DELETE CASCADE,
        ProductId       INT NOT NULL CONSTRAINT FK_POItems_Product REFERENCES dbo.Products(ProductId),
        Quantity        INT NOT NULL CONSTRAINT CK_POItems_Qty CHECK (Quantity >= 1),
        UnitCost        DECIMAL(10,2) NOT NULL
    );
    CREATE INDEX IX_POItems_PO ON dbo.PurchaseOrderItems(PurchaseOrderId);
END
GO

/* ---------------- PROCEDURES ---------------- */

/* List of purchase orders */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetOrders
AS
BEGIN
    SET NOCOUNT ON;
    SELECT po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status],
           CAST(ISNULL(SUM(i.Quantity * i.UnitCost), 0) AS DECIMAL(12,2)) AS Total,
           ISNULL(r.RefNo, N'') AS RequisitionRef
    FROM dbo.PurchaseOrders po
    LEFT JOIN dbo.PurchaseOrderItems i ON i.PurchaseOrderId = po.PurchaseOrderId
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = po.RequisitionId
    GROUP BY po.PurchaseOrderId, po.PoNo, po.DateCreated, po.Supplier, po.[Status], r.RefNo
    ORDER BY po.DateCreated DESC, po.PurchaseOrderId DESC;
END
GO

/* Approved requisitions that do not have an active PO yet */
CREATE OR ALTER PROCEDURE dbo.usp_PurGetApprovedRequisitions
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.RequisitionId, r.RefNo, r.Branch, r.DateCreated, COUNT(i.ItemId) AS ItemCount
    FROM dbo.Requisitions r
    JOIN dbo.RequisitionItems i ON i.RequisitionId = r.RequisitionId
    WHERE r.[Status] = N'Approved'
      AND NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrders po
                      WHERE po.RequisitionId = r.RequisitionId AND po.[Status] <> N'Cancelled')
    GROUP BY r.RequisitionId, r.RefNo, r.Branch, r.DateCreated
    ORDER BY r.DateCreated;
END
GO

/* Create a PO from an approved requisition. Returns the new PurchaseOrderId. */
CREATE OR ALTER PROCEDURE dbo.usp_PurCreateOrder
    @RequisitionId INT, @Supplier NVARCHAR(100), @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF LTRIM(RTRIM(@Supplier)) = N'' THROW 50001, N'Supplier is required.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50006, N'Only the Head or a Manager can create a purchase order.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved')
        THROW 50002, N'Only approved requisitions can be turned into a purchase order.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE RequisitionId = @RequisitionId AND [Status] <> N'Cancelled')
        THROW 50003, N'This requisition already has a purchase order.', 1;

    BEGIN TRAN;

    DECLARE @seq INT = (SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE CAST(DateCreated AS DATE) = CAST(GETDATE() AS DATE)) + 1;
    DECLARE @PoNo NVARCHAR(30) = N'PO-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-' + RIGHT(N'0000' + CAST(@seq AS NVARCHAR(10)), 4);

    INSERT INTO dbo.PurchaseOrders (PoNo, RequisitionId, Supplier, CreatedById)
    VALUES (@PoNo, @RequisitionId, LTRIM(RTRIM(@Supplier)), @UserId);

    DECLARE @PoId INT = SCOPE_IDENTITY();

    INSERT INTO dbo.PurchaseOrderItems (PurchaseOrderId, ProductId, Quantity, UnitCost)
    SELECT @PoId, i.ProductId, i.Quantity, p.Price
    FROM dbo.RequisitionItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    WHERE i.RequisitionId = @RequisitionId;

    COMMIT;
    SELECT @PoId;
END
GO

/* usp_PurReceiveOrder (with the Head / Manager check) is created by 5_PurchasingRoles.sql */
USE SolemateKicksDB;
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurReceiveOrder
    @PurchaseOrderId INT, @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50005, N'Only the Head or a Manager can mark a purchase order as received.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId AND [Status] = N'Ordered')
        THROW 50004, N'Only purchase orders with status Ordered can be received.', 1;

    BEGIN TRAN;

    DECLARE @PoNo NVARCHAR(30) = (SELECT PoNo FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId);
    DECLARE @Reason NVARCHAR(100) = N'Received ' + @PoNo;
    DECLARE @ItemId INT = 0, @ProductId INT, @Qty INT;

    WHILE 1 = 1
    BEGIN
        SELECT TOP 1 @ItemId = ItemId, @ProductId = ProductId, @Qty = Quantity
        FROM dbo.PurchaseOrderItems
        WHERE PurchaseOrderId = @PurchaseOrderId AND ItemId > @ItemId
        ORDER BY ItemId;

        IF @@ROWCOUNT = 0 BREAK;

        EXEC dbo.usp_InvAdjustStock @ProductId, @Qty, @Reason, @UserId;
    END

    UPDATE dbo.PurchaseOrders
    SET [Status] = N'Received', DateReceived = GETDATE(), ReceivedById = @UserId
    WHERE PurchaseOrderId = @PurchaseOrderId;

    COMMIT;
END
GO
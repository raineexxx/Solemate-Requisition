USE SolemateKicksDB;
GO

/* =====================================================================
   Purchasing - the Manager sends a purchase order to the supplier

   Run AFTER 13_SelectItems.sql (and the Delivery scripts 6_ and 7_).
   Sending a PO:
     - marks it as sent (PurchaseOrders.DateSent)
     - records an EXPECTED delivery in the Delivery module (PoRef = the PO number,
       same supplier, same items and quantities), so the Manager sees it on the Delivery screen.
   Safe to run more than once.
   ===================================================================== */

IF COL_LENGTH(N'dbo.PurchaseOrders', N'DateSent') IS NULL
    ALTER TABLE dbo.PurchaseOrders ADD DateSent DATETIME NULL;
GO

CREATE OR ALTER PROCEDURE dbo.usp_PurSend
    @PurchaseOrderId INT,
    @UserId          INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 50040, N'Only the Manager can send a purchase order to the supplier.', 1;

    DECLARE @PoNo NVARCHAR(30), @Supplier NVARCHAR(100), @SupplierId INT, @ReqId INT, @Status NVARCHAR(20), @Sent DATETIME;
    SELECT @PoNo = PoNo, @Supplier = Supplier, @SupplierId = SupplierId, @ReqId = RequisitionId,
           @Status = [Status], @Sent = DateSent
    FROM dbo.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId;

    IF @PoNo IS NULL
        THROW 50041, N'Purchase order not found.', 1;
    IF @Status <> N'Ordered'
        THROW 50042, N'Only an ordered purchase order can be sent.', 1;
    IF @Sent IS NOT NULL
        THROW 50043, N'This purchase order was already sent to the supplier.', 1;
    IF @SupplierId IS NULL
        THROW 50044, N'This purchase order has no supplier.', 1;

    DECLARE @Expected DATE = DATEADD(DAY, 7, CAST(GETDATE() AS DATE));   -- default lead time: 7 days

    BEGIN TRAN;

        DECLARE @Prefix NVARCHAR(30) = N'DEL-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next   INT = (SELECT COUNT(*) FROM dbo.Deliveries WITH (UPDLOCK, HOLDLOCK)
                               WHERE DeliveryRef LIKE @Prefix + N'%') + 1;
        DECLARE @Ref    NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.Deliveries (DeliveryRef, PoRef, SupplierName, SupplierId, RequisitionId, ExpectedDate,
                                    [Status], DateCreated, CreatedById, Remarks)
        VALUES (@Ref, @PoNo, @Supplier, @SupplierId, @ReqId, @Expected,
                N'Expected', GETDATE(), @UserId, N'From purchase order ' + @PoNo);

        DECLARE @DelId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.DeliveryItems (DeliveryId, ProductId, OrderedQty, UnitCost)
        SELECT @DelId, ProductId, SUM(Quantity), MAX(UnitCost)
        FROM dbo.PurchaseOrderItems
        WHERE PurchaseOrderId = @PurchaseOrderId
        GROUP BY ProductId;

        UPDATE dbo.PurchaseOrders SET DateSent = GETDATE() WHERE PurchaseOrderId = @PurchaseOrderId;

    COMMIT;

    SELECT @Ref AS DeliveryRef, @Expected AS ExpectedDate;
END
GO

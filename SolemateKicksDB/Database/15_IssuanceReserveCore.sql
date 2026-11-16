USE SolemateKicksDB;
GO

CREATE OR ALTER PROCEDURE dbo.usp_IssReserveCore
    @RequisitionId INT,
    @Reserved      INT OUTPUT,
    @ShortLines    INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Reserved = 0;
    SET @ShortLines = 0;

    IF NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved')
        RETURN;

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

    SELECT @Reserved = ISNULL(SUM(Take), 0) FROM @T;
    SELECT @ShortLines = COUNT(*) FROM dbo.RequisitionItems
    WHERE RequisitionId = @RequisitionId AND Quantity - IssuedQty - ReservedQty > 0;
END
GO
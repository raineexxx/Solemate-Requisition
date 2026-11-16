USE SolemateKicksDB;
GO

CREATE OR ALTER PROCEDURE dbo.usp_IssGetBranchStock
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Role NVARCHAR(20), @Branch NVARCHAR(100);
    SELECT @Role = [Role], @Branch = Branch FROM dbo.AppUsers WHERE UserId = @UserId;

    SELECT r.Branch,
           p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           SUM(ii.Quantity) AS Qty
    FROM dbo.Issuances i
    JOIN dbo.IssuanceItems ii ON ii.IssuanceId = i.IssuanceId
    JOIN dbo.Requisitions r   ON r.RequisitionId = i.RequisitionId
    JOIN dbo.Products p       ON p.ProductId = ii.ProductId
    JOIN dbo.Brands b         ON b.BrandId = p.BrandId
    WHERE i.DateAcknowledged IS NOT NULL
      AND (@Role IN (N'Manager', N'Head') OR r.Branch = @Branch)
    GROUP BY r.Branch, p.Sku, b.BrandName, p.ProductName, p.Size
    ORDER BY r.Branch, b.BrandName, p.ProductName, p.Size;
END
GO
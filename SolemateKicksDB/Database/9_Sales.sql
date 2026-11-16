-- =====================================================================
--  Solemate Sales module  (run AFTER 3_Inventory.sql)
--  Adds: Products.ReservedQty, Sales + SaleItems tables, 3 procedures (usp_Sal...)
--  Every sale deducts stock IMMEDIATELY (real-time) and is logged in StockMovements.
-- =====================================================================
USE SolemateKicksDB;
GO

-- Quantity set aside for approved requisitions (used by 10_Issuance.sql).
-- A sale can only use stock that is NOT reserved.
IF COL_LENGTH(N'dbo.Products', N'ReservedQty') IS NULL
    ALTER TABLE dbo.Products ADD ReservedQty INT NOT NULL CONSTRAINT DF_Products_Reserved DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Products_Reserved')
    ALTER TABLE dbo.Products ADD CONSTRAINT CK_Products_Reserved CHECK (ReservedQty >= 0 AND ReservedQty <= StockQty);
GO

IF OBJECT_ID(N'dbo.Sales') IS NULL
BEGIN
    CREATE TABLE dbo.Sales (
        SaleId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Sales PRIMARY KEY,
        SaleNo      NVARCHAR(30) NOT NULL CONSTRAINT UQ_Sales_SaleNo UNIQUE,      -- SAL-yyyyMMdd-0001
        CashierId   INT NOT NULL CONSTRAINT FK_Sales_Cashier REFERENCES dbo.AppUsers(UserId),
        Branch      NVARCHAR(50) NOT NULL,
        DateCreated DATETIME NOT NULL CONSTRAINT DF_Sales_Date DEFAULT GETDATE(),
        Total       DECIMAL(12,2) NOT NULL CONSTRAINT DF_Sales_Total DEFAULT 0,
        Remarks     NVARCHAR(200) NULL
    );
    CREATE INDEX IX_Sales_Date ON dbo.Sales(DateCreated);
END
GO

IF OBJECT_ID(N'dbo.SaleItems') IS NULL
BEGIN
    CREATE TABLE dbo.SaleItems (
        ItemId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SaleItems PRIMARY KEY,
        SaleId    INT NOT NULL CONSTRAINT FK_SaleItems_Sale REFERENCES dbo.Sales(SaleId) ON DELETE CASCADE,
        ProductId INT NOT NULL CONSTRAINT FK_SaleItems_Product REFERENCES dbo.Products(ProductId),
        Quantity  INT NOT NULL CONSTRAINT CK_SaleItems_Qty CHECK (Quantity >= 1),
        UnitPrice DECIMAL(10,2) NOT NULL
    );
    CREATE INDEX IX_SaleItems_Sale ON dbo.SaleItems(SaleId);
END
GO

/* ---------------------------------------------------------------
   Record a sale. @ItemsXml: <Items><Item ProductId="5" Quantity="2"/>...</Items>
   ONE transaction: check stock -> save sale -> deduct stock -> log movement.
   --------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_SalCreate
    @UserId   INT,
    @Remarks  NVARCHAR(200) = NULL,
    @ItemsXml NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Role NVARCHAR(20), @Branch NVARCHAR(50);
    SELECT @Role = [Role], @Branch = Branch FROM dbo.AppUsers WHERE UserId = @UserId;

    IF @Role IS NULL THROW 50201, 'User not found.', 1;
    IF @Role NOT IN (N'Cashier', N'Manager') THROW 50202, 'Only cashiers and managers can record sales.', 1;

    DECLARE @x XML = CAST(@ItemsXml AS XML);
    DECLARE @Items TABLE (ProductId INT NOT NULL PRIMARY KEY, Quantity INT NOT NULL);

    INSERT INTO @Items (ProductId, Quantity)
    SELECT ProductId, SUM(Quantity)
    FROM (SELECT t.i.value('@ProductId', 'int') AS ProductId,
                 t.i.value('@Quantity',  'int') AS Quantity
          FROM @x.nodes('/Items/Item') AS t(i)) s
    GROUP BY ProductId;

    IF NOT EXISTS (SELECT 1 FROM @Items) THROW 50203, 'Add at least one item to the sale.', 1;
    IF EXISTS (SELECT 1 FROM @Items WHERE Quantity < 1) THROW 50204, 'Every quantity must be at least 1.', 1;
    IF EXISTS (SELECT 1 FROM @Items i LEFT JOIN dbo.Products p ON p.ProductId = i.ProductId WHERE p.ProductId IS NULL)
        THROW 50205, 'One of the selected products no longer exists.', 1;

    BEGIN TRAN;

        -- lock the product rows, then make sure every item is really available (stock minus reserved)
        DECLARE @ShortSku NVARCHAR(30) = NULL, @msg NVARCHAR(200);
        SELECT TOP 1 @ShortSku = p.Sku
        FROM @Items i
        JOIN dbo.Products p WITH (UPDLOCK, ROWLOCK) ON p.ProductId = i.ProductId
        WHERE p.StockQty - p.ReservedQty < i.Quantity
        ORDER BY p.Sku;

        IF @ShortSku IS NOT NULL
        BEGIN
            SET @msg = N'Not enough available stock for ' + @ShortSku + N'. Reduce the quantity and try again.';
            THROW 50206, @msg, 1;
        END

        DECLARE @Prefix NVARCHAR(30) = N'SAL-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next   INT = (SELECT COUNT(*) FROM dbo.Sales WITH (UPDLOCK, HOLDLOCK) WHERE SaleNo LIKE @Prefix + N'%') + 1;
        DECLARE @SaleNo NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.Sales (SaleNo, CashierId, Branch, Remarks)
        VALUES (@SaleNo, @UserId, @Branch, NULLIF(LTRIM(RTRIM(@Remarks)), N''));

        DECLARE @SaleId INT = SCOPE_IDENTITY();

        INSERT INTO dbo.SaleItems (SaleId, ProductId, Quantity, UnitPrice)
        SELECT @SaleId, i.ProductId, i.Quantity, p.Price
        FROM @Items i JOIN dbo.Products p ON p.ProductId = i.ProductId;

        -- real-time deduction
        UPDATE p SET p.StockQty = p.StockQty - i.Quantity
        FROM dbo.Products p JOIN @Items i ON i.ProductId = p.ProductId;

        INSERT INTO dbo.StockMovements (ProductId, QtyChange, StockAfter, Reason, CreatedById)
        SELECT i.ProductId, -i.Quantity, p.StockQty, N'Sale ' + @SaleNo, @UserId
        FROM @Items i JOIN dbo.Products p ON p.ProductId = i.ProductId;

        DECLARE @Total DECIMAL(12,2) = (SELECT SUM(Quantity * UnitPrice) FROM dbo.SaleItems WHERE SaleId = @SaleId);
        UPDATE dbo.Sales SET Total = @Total WHERE SaleId = @SaleId;

    COMMIT;

    SELECT @SaleNo AS SaleNo, @Total AS Total;
END
GO

/* Sales history. Cashiers see only their own sales. */
CREATE OR ALTER PROCEDURE dbo.usp_SalGetRecent
    @ViewerId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @OnlyMine BIT =
        CASE WHEN EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @ViewerId AND [Role] = N'Cashier') THEN 1 ELSE 0 END;

    SELECT TOP (200)
           s.SaleId, s.SaleNo, s.DateCreated, u.FullName AS Cashier, s.Branch,
           ISNULL(t.Lines, 0) AS Lines, ISNULL(t.Qty, 0) AS Qty, s.Total
    FROM dbo.Sales s
    JOIN dbo.AppUsers u ON u.UserId = s.CashierId
    LEFT JOIN (SELECT SaleId, COUNT(*) AS Lines, SUM(Quantity) AS Qty FROM dbo.SaleItems GROUP BY SaleId) t
           ON t.SaleId = s.SaleId
    WHERE @OnlyMine = 0 OR s.CashierId = @ViewerId
    ORDER BY s.DateCreated DESC, s.SaleId DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_SalGetItems
    @SaleId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size,
           i.Quantity, i.UnitPrice, CAST(i.Quantity * i.UnitPrice AS DECIMAL(12,2)) AS LineTotal
    FROM dbo.SaleItems i
    JOIN dbo.Products p ON p.ProductId = i.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE i.SaleId = @SaleId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

/* Catalog for the Sales screen. "Stock" here = what can really be sold (stock minus reserved). */
CREATE OR ALTER PROCEDURE dbo.usp_SalGetCatalog
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.ProductId, b.BrandId, p.Sku, b.BrandName AS Brand, p.ProductName AS Product, p.Size, p.Price,
           p.StockQty - p.ReservedQty AS Stock, p.ReorderLevel
    FROM dbo.Products p
    JOIN dbo.Brands b ON b.BrandId = p.BrandId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

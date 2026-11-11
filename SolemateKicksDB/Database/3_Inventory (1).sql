-- =====================================================================
--  Solemate Inventory module  (run AFTER Database.sql and StoredProcedures.sql)
--  Adds: StockMovements table + 4 stored procedures (usp_Inv...)
--  Does not change any existing table or procedure.
-- =====================================================================
USE SolemateKicksDB;
GO

-- Every change to stock is logged here (stock in, correction, opening stock).
IF OBJECT_ID('dbo.StockMovements', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockMovements
    (
        MovementId  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ProductId   INT NOT NULL CONSTRAINT FK_StockMovements_Products REFERENCES dbo.Products(ProductId),
        QtyChange   INT NOT NULL,                 -- + stock in, - removed
        StockAfter  INT NOT NULL,                 -- stock level after this change
        Reason      NVARCHAR(100) NOT NULL,
        CreatedById INT NULL CONSTRAINT FK_StockMovements_AppUsers REFERENCES dbo.AppUsers(UserId),
        DateCreated DATETIME NOT NULL CONSTRAINT DF_StockMovements_Date DEFAULT (GETDATE())
    );
END
GO

-- ---------------------------------------------------------------------
--  All products with brand and stock (screen filters by brand/status in the app)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_InvGetStock
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.ProductId,
           p.BrandId,
           p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           p.Price,
           p.StockQty    AS Stock,
           p.ReorderLevel
    FROM dbo.Products p
    JOIN dbo.Brands   b ON b.BrandId = p.BrandId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

-- ---------------------------------------------------------------------
--  Add a new product (@ProductId = 0) or edit one. Stock is NOT edited here
--  (use usp_InvAdjustStock) except the opening stock of a brand-new product.
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_InvSaveProduct
    @ProductId    INT,
    @BrandId      INT,
    @Sku          NVARCHAR(30),
    @ProductName  NVARCHAR(100),
    @Size         INT,
    @Price        DECIMAL(10,2),
    @ReorderLevel INT,
    @OpeningStock INT,
    @UserId       INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Sku = LTRIM(RTRIM(ISNULL(@Sku, N'')));
    SET @ProductName = LTRIM(RTRIM(ISNULL(@ProductName, N'')));

    IF @Sku = N''          THROW 50101, 'SKU is required.', 1;
    IF @ProductName = N''  THROW 50102, 'Product name is required.', 1;
    IF @Size <= 0          THROW 50103, 'Size must be greater than zero.', 1;
    IF @Price < 0          THROW 50104, 'Price cannot be negative.', 1;
    IF @ReorderLevel < 0   THROW 50105, 'Reorder level cannot be negative.', 1;
    IF @OpeningStock < 0   THROW 50106, 'Opening stock cannot be negative.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Brands WHERE BrandId = @BrandId)
        THROW 50107, 'The selected brand was not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.Products WHERE Sku = @Sku AND ProductId <> @ProductId)
        THROW 50108, 'Another product already uses this SKU.', 1;

    BEGIN TRAN;

    IF @ProductId = 0
    BEGIN
        INSERT INTO dbo.Products (BrandId, Sku, ProductName, Size, Price, StockQty, ReorderLevel)
        VALUES (@BrandId, @Sku, @ProductName, @Size, @Price, @OpeningStock, @ReorderLevel);

        SET @ProductId = CONVERT(INT, SCOPE_IDENTITY());

        IF @OpeningStock > 0
            INSERT INTO dbo.StockMovements (ProductId, QtyChange, StockAfter, Reason, CreatedById)
            VALUES (@ProductId, @OpeningStock, @OpeningStock, N'Opening stock', @UserId);
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE ProductId = @ProductId)
            THROW 50109, 'The product was not found.', 1;

        UPDATE dbo.Products
        SET BrandId = @BrandId, Sku = @Sku, ProductName = @ProductName,
            Size = @Size, Price = @Price, ReorderLevel = @ReorderLevel
        WHERE ProductId = @ProductId;
    END

    COMMIT TRAN;

    SELECT @ProductId AS ProductId;
END
GO

-- ---------------------------------------------------------------------
--  Stock in (+) or correction (-). Cannot take stock below zero. Logged.
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_InvAdjustStock
    @ProductId INT,
    @QtyChange INT,
    @Reason    NVARCHAR(100),
    @UserId    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Reason = LTRIM(RTRIM(ISNULL(@Reason, N'')));

    IF @QtyChange = 0   THROW 50111, 'Quantity cannot be zero.', 1;
    IF @Reason = N''    THROW 50112, 'A reason is required.', 1;

    DECLARE @Current INT;

    BEGIN TRAN;

    SELECT @Current = StockQty FROM dbo.Products WITH (UPDLOCK, ROWLOCK) WHERE ProductId = @ProductId;

    IF @Current IS NULL THROW 50113, 'The product was not found.', 1;
    IF @Current + @QtyChange < 0
        THROW 50114, 'Not enough stock. You cannot remove more than the current stock.', 1;

    UPDATE dbo.Products SET StockQty = @Current + @QtyChange WHERE ProductId = @ProductId;

    INSERT INTO dbo.StockMovements (ProductId, QtyChange, StockAfter, Reason, CreatedById)
    VALUES (@ProductId, @QtyChange, @Current + @QtyChange, @Reason, @UserId);

    COMMIT TRAN;

    SELECT @Current + @QtyChange AS NewStock;
END
GO

-- ---------------------------------------------------------------------
--  Recent stock history (@ProductId = 0 -> all products)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_InvGetMovements
    @ProductId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (300)
           m.MovementId,
           m.DateCreated,
           p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           m.QtyChange,
           m.StockAfter,
           m.Reason,
           ISNULL(u.FullName, N'') AS ByUser
    FROM dbo.StockMovements m
    JOIN dbo.Products p ON p.ProductId = m.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    LEFT JOIN dbo.AppUsers u ON u.UserId = m.CreatedById
    WHERE @ProductId = 0 OR m.ProductId = @ProductId
    ORDER BY m.MovementId DESC;
END
GO

USE SolemateKicksDB;
GO

/* =====================================================================
   Purchasing module - suppliers (name, address, email)

   Run AFTER 4_Purchasing.sql. It works before OR after the Delivery upgrade script,
   because the Suppliers table is shared with the Delivery module: this script creates
   the table with ALL columns, or adds the missing ones to a table that already exists.
   Safe to run more than once.
   ===================================================================== */

/* ---------------- TABLE (shared with Delivery) ---------------- */
IF OBJECT_ID(N'dbo.Suppliers') IS NULL
BEGIN
    CREATE TABLE dbo.Suppliers (
        SupplierId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY,
        SupplierName  NVARCHAR(100) NOT NULL CONSTRAINT UQ_Suppliers_Name UNIQUE,
        ContactPerson NVARCHAR(100) NULL,
        Phone         NVARCHAR(30)  NULL,
        Email         NVARCHAR(100) NULL,
        [Address]     NVARCHAR(200) NOT NULL CONSTRAINT DF_Suppliers_Address DEFAULT N'',
        IsActive      BIT NOT NULL CONSTRAINT DF_Suppliers_Active DEFAULT 1,
        DateCreated   DATETIME NOT NULL CONSTRAINT DF_Suppliers_Date DEFAULT (GETDATE())
    );
END
GO

/* the table already existed (older version of this script, or the Delivery version): add whatever is missing */
IF COL_LENGTH(N'dbo.Suppliers', N'ContactPerson') IS NULL
    ALTER TABLE dbo.Suppliers ADD ContactPerson NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.Suppliers', N'Phone') IS NULL
    ALTER TABLE dbo.Suppliers ADD Phone NVARCHAR(30) NULL;
IF COL_LENGTH(N'dbo.Suppliers', N'Email') IS NULL
    ALTER TABLE dbo.Suppliers ADD Email NVARCHAR(100) NULL;
IF COL_LENGTH(N'dbo.Suppliers', N'Address') IS NULL
    ALTER TABLE dbo.Suppliers ADD [Address] NVARCHAR(200) NOT NULL CONSTRAINT DF_Suppliers_Address DEFAULT N'';
IF COL_LENGTH(N'dbo.Suppliers', N'IsActive') IS NULL
    ALTER TABLE dbo.Suppliers ADD IsActive BIT NOT NULL CONSTRAINT DF_Suppliers_Active DEFAULT 1;
IF COL_LENGTH(N'dbo.Suppliers', N'DateCreated') IS NULL
    ALTER TABLE dbo.Suppliers ADD DateCreated DATETIME NOT NULL CONSTRAINT DF_Suppliers_Date DEFAULT (GETDATE());
GO

/* the email may be left blank (Delivery saves NULL for a blank email) */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Suppliers') AND name = N'Email' AND is_nullable = 0)
    ALTER TABLE dbo.Suppliers ALTER COLUMN Email NVARCHAR(100) NULL;
GO

/* Purchase orders point to a supplier (the name is still copied into PurchaseOrders.Supplier) */
IF COL_LENGTH(N'dbo.PurchaseOrders', N'SupplierId') IS NULL
BEGIN
    ALTER TABLE dbo.PurchaseOrders
        ADD SupplierId INT NULL CONSTRAINT FK_PO_Supplier REFERENCES dbo.Suppliers(SupplierId);
END
GO

/* ---------------- PROCEDURES ---------------- */

/* List suppliers. @ActiveOnly = 1 hides suppliers that were set inactive (used by the New PO dialog). */
CREATE OR ALTER PROCEDURE dbo.usp_SupGetAll
    @ActiveOnly INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SupplierId,
           SupplierName,
           ISNULL([Address], N'') AS [Address],
           ISNULL(Email, N'')     AS Email
    FROM dbo.Suppliers
    WHERE @ActiveOnly = 0 OR IsActive = 1
    ORDER BY SupplierName;
END
GO

/* Add (@SupplierId = 0) or edit a supplier. Returns the SupplierId. Head / Manager only. */
CREATE OR ALTER PROCEDURE dbo.usp_SupSave
    @SupplierId   INT,
    @SupplierName NVARCHAR(100),
    @Address      NVARCHAR(200),
    @Email        NVARCHAR(100),
    @UserId       INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @SupplierName = LTRIM(RTRIM(ISNULL(@SupplierName, N'')));
    SET @Address      = LTRIM(RTRIM(ISNULL(@Address, N'')));
    SET @Email        = LTRIM(RTRIM(ISNULL(@Email, N'')));

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50010, N'Only the Head or a Manager can add or edit suppliers.', 1;

    IF @SupplierName = N''
        THROW 50011, N'Supplier name is required.', 1;

    IF EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierName = @SupplierName AND SupplierId <> @SupplierId)
        THROW 50012, N'A supplier with this name already exists.', 1;

    IF @SupplierId = 0
    BEGIN
        INSERT INTO dbo.Suppliers (SupplierName, [Address], Email)
        VALUES (@SupplierName, @Address, NULLIF(@Email, N''));

        SET @SupplierId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
            THROW 50013, N'Supplier not found.', 1;

        UPDATE dbo.Suppliers
        SET SupplierName = @SupplierName, [Address] = @Address, Email = NULLIF(@Email, N'')
        WHERE SupplierId = @SupplierId;

        -- keep the supplier name on existing purchase orders up to date
        UPDATE dbo.PurchaseOrders SET Supplier = @SupplierName WHERE SupplierId = @SupplierId;

        -- ...and on existing deliveries, when the Delivery module is installed
        IF OBJECT_ID(N'dbo.Deliveries') IS NOT NULL AND COL_LENGTH(N'dbo.Deliveries', N'SupplierId') IS NOT NULL
            EXEC sp_executesql
                N'UPDATE dbo.Deliveries SET SupplierName = @n WHERE SupplierId = @id',
                N'@n NVARCHAR(100), @id INT',
                @n = @SupplierName, @id = @SupplierId;
    END

    SELECT @SupplierId;
END
GO

/* Delete a supplier. Not allowed when purchase orders or deliveries already use it. Head / Manager only. */
CREATE OR ALTER PROCEDURE dbo.usp_SupDelete
    @SupplierId INT,
    @UserId     INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50014, N'Only the Head or a Manager can delete suppliers.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
        THROW 50013, N'Supplier not found.', 1;

    BEGIN TRY
        DELETE FROM dbo.Suppliers WHERE SupplierId = @SupplierId;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 547   -- foreign key: purchase orders or deliveries use this supplier
            THROW 50015, N'This supplier is used by purchase orders or deliveries, so it cannot be deleted.', 1;
        THROW;
    END CATCH
END
GO

/* Create a PO from an approved requisition, for a chosen (active) supplier. Returns the new PurchaseOrderId. */
CREATE OR ALTER PROCEDURE dbo.usp_PurCreateOrder
    @RequisitionId INT,
    @SupplierId    INT,
    @UserId        INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Supplier NVARCHAR(100) =
        (SELECT SupplierName FROM dbo.Suppliers WHERE SupplierId = @SupplierId AND IsActive = 1);

    IF @Supplier IS NULL
        THROW 50001, N'Please choose an active supplier.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] IN (N'Head', N'Manager'))
        THROW 50006, N'Only the Head or a Manager can create a purchase order.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved')
        THROW 50002, N'Only approved requisitions can be turned into a purchase order.', 1;
    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE RequisitionId = @RequisitionId AND [Status] <> N'Cancelled')
        THROW 50003, N'This requisition already has a purchase order.', 1;

    BEGIN TRAN;

    DECLARE @seq INT = (SELECT COUNT(*) FROM dbo.PurchaseOrders WHERE CAST(DateCreated AS DATE) = CAST(GETDATE() AS DATE)) + 1;
    DECLARE @PoNo NVARCHAR(30) = N'PO-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-' + RIGHT(N'0000' + CAST(@seq AS NVARCHAR(10)), 4);

    INSERT INTO dbo.PurchaseOrders (PoNo, RequisitionId, Supplier, SupplierId, CreatedById)
    VALUES (@PoNo, @RequisitionId, @Supplier, @SupplierId, @UserId);

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

/* ---------------- SUPPLIER LIST (same for everyone) ----------------
   Adds each supplier only when its name is not in the table yet, so it is safe to run again
   and it never creates duplicates or changes suppliers that already exist. */
INSERT INTO dbo.Suppliers (SupplierName, [Address], Email)
SELECT v.SupplierName, v.[Address], v.Email
FROM (VALUES
    (N'Olem Shoe Corp',
     N'800 NW 21st Street, Miami, FL 33127, USA',
     N'customer.service@olemshoe.com'),
    (N'Dong Hung Industry Joint Stock Company (DHI)',
     N'667 Dien Bien Phu Street, Ward 22, Binh Thanh District, Ho Chi Minh City, Vietnam',
     N'contact@donghungfootwear.com'),
    (N'Clarco Shoes (Jinjiang Clarco Footwear Co., Ltd.)',
     N'7th Floor, Business Bldg No. 234-1, Qiguang East Rd, Handai, Chendai, Jinjiang, Fujian, China',
     N'sales01@clarco.com')
) AS v (SupplierName, [Address], Email)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Suppliers s WHERE s.SupplierName = v.SupplierName);
GO

SELECT SupplierId, SupplierName, [Address], Email, IsActive FROM dbo.Suppliers ORDER BY SupplierName;
GO
USE SolemateKicksDB;
GO

/* Run this AFTER 4_Purchasing.sql and 5_PurchasingRoles.sql.
   It works whether or not 7_DeliveryUpgrade.sql was run first, and it is safe to run more than once.

   Why the old version failed: 7_DeliveryUpgrade.sql already creates dbo.Suppliers
   (without an Address column), so the old "create table if missing" step was skipped and
   usp_SupGetAll / usp_SupSave then failed with "Invalid column name 'Address'". */

/* ---------------- TABLE ---------------- */
IF OBJECT_ID(N'dbo.Suppliers') IS NULL
BEGIN
    CREATE TABLE dbo.Suppliers (
        SupplierId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY,
        SupplierName  NVARCHAR(100) NOT NULL CONSTRAINT UQ_Suppliers_Name UNIQUE,
        ContactPerson NVARCHAR(100) NULL,
        Phone         NVARCHAR(30)  NULL,
        Email         NVARCHAR(100) NULL,
        IsActive      BIT NOT NULL CONSTRAINT DF_Suppliers_Active DEFAULT 1,
        DateCreated   DATETIME NOT NULL CONSTRAINT DF_Suppliers_Date DEFAULT (GETDATE())
    );
END
GO

/* Add any column the table is still missing (so the order of the scripts does not matter) */
IF COL_LENGTH(N'dbo.Suppliers', N'Address') IS NULL
    ALTER TABLE dbo.Suppliers ADD [Address] NVARCHAR(200) NOT NULL CONSTRAINT DF_Suppliers_Address DEFAULT N'';
GO
IF COL_LENGTH(N'dbo.Suppliers', N'ContactPerson') IS NULL
    ALTER TABLE dbo.Suppliers ADD ContactPerson NVARCHAR(100) NULL;
GO
IF COL_LENGTH(N'dbo.Suppliers', N'Phone') IS NULL
    ALTER TABLE dbo.Suppliers ADD Phone NVARCHAR(30) NULL;
GO
IF COL_LENGTH(N'dbo.Suppliers', N'Email') IS NULL
    ALTER TABLE dbo.Suppliers ADD Email NVARCHAR(100) NULL;
GO
IF COL_LENGTH(N'dbo.Suppliers', N'IsActive') IS NULL
    ALTER TABLE dbo.Suppliers ADD IsActive BIT NOT NULL CONSTRAINT DF_Suppliers_Active DEFAULT 1;
GO
IF COL_LENGTH(N'dbo.Suppliers', N'DateCreated') IS NULL
    ALTER TABLE dbo.Suppliers ADD DateCreated DATETIME NOT NULL CONSTRAINT DF_Suppliers_Date DEFAULT (GETDATE());
GO

/* Purchase orders now point to a supplier (the name is still copied into PurchaseOrders.Supplier) */
IF COL_LENGTH(N'dbo.PurchaseOrders', N'SupplierId') IS NULL
BEGIN
    ALTER TABLE dbo.PurchaseOrders
        ADD SupplierId INT NULL CONSTRAINT FK_PO_Supplier REFERENCES dbo.Suppliers(SupplierId);
END
GO

/* Link existing purchase orders to a supplier with the same name (creates the supplier if needed) */
INSERT INTO dbo.Suppliers (SupplierName)
SELECT DISTINCT po.Supplier
FROM dbo.PurchaseOrders po
WHERE po.SupplierId IS NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.Suppliers s WHERE s.SupplierName = po.Supplier);
GO

UPDATE po
SET po.SupplierId = s.SupplierId
FROM dbo.PurchaseOrders po
JOIN dbo.Suppliers s ON s.SupplierName = po.Supplier
WHERE po.SupplierId IS NULL;
GO

/* ---------------- PROCEDURES ---------------- */

/* List all suppliers */
CREATE OR ALTER PROCEDURE dbo.usp_SupGetAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SupplierId,
           SupplierName,
           ISNULL([Address], N'') AS [Address],
           ISNULL(Email, N'')     AS Email
    FROM dbo.Suppliers
    WHERE IsActive = 1
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
        VALUES (@SupplierName, @Address, @Email);

        SET @SupplierId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
            THROW 50013, N'Supplier not found.', 1;

        UPDATE dbo.Suppliers
        SET SupplierName = @SupplierName, [Address] = @Address, Email = @Email
        WHERE SupplierId = @SupplierId;

        -- keep the supplier name on existing purchase orders up to date
        UPDATE dbo.PurchaseOrders SET Supplier = @SupplierName WHERE SupplierId = @SupplierId;
    END

    SELECT @SupplierId AS SupplierId;
END
GO

/* Delete a supplier. Not allowed when it already has purchase orders or deliveries. Head / Manager only. */
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

    IF EXISTS (SELECT 1 FROM dbo.PurchaseOrders WHERE SupplierId = @SupplierId)
        THROW 50015, N'This supplier already has purchase orders, so it cannot be deleted.', 1;

    -- the Delivery module also points to suppliers (checked with dynamic SQL so this script
    -- still compiles if the Deliveries table has not been created yet)
    IF OBJECT_ID(N'dbo.Deliveries') IS NOT NULL AND COL_LENGTH(N'dbo.Deliveries', N'SupplierId') IS NOT NULL
    BEGIN
        DECLARE @hasDeliveries BIT = 0;
        EXEC sp_executesql
            N'IF EXISTS (SELECT 1 FROM dbo.Deliveries WHERE SupplierId = @id) SET @has = 1;',
            N'@id INT, @has BIT OUTPUT', @id = @SupplierId, @has = @hasDeliveries OUTPUT;

        IF @hasDeliveries = 1
            THROW 50016, N'This supplier already has deliveries, so it cannot be deleted.', 1;
    END

    DELETE FROM dbo.Suppliers WHERE SupplierId = @SupplierId;
END
GO

/* Create a PO from an approved requisition, for a chosen supplier. Returns the new PurchaseOrderId.
   This replaces the old version that took the supplier as typed text. */
CREATE OR ALTER PROCEDURE dbo.usp_PurCreateOrder
    @RequisitionId INT,
    @SupplierId    INT,
    @UserId        INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Supplier NVARCHAR(100) = (SELECT SupplierName FROM dbo.Suppliers WHERE SupplierId = @SupplierId);

    IF @Supplier IS NULL
        THROW 50001, N'Please choose a supplier.', 1;
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
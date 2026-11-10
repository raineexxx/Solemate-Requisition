
USE master;
GO
IF DB_ID(N'SolemateKicksDB') IS NOT NULL
BEGIN
    ALTER DATABASE SolemateKicksDB SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SolemateKicksDB;
END
GO
CREATE DATABASE SolemateKicksDB;
GO
USE SolemateKicksDB;
GO

/* ---------------- TABLES ---------------- */
CREATE TABLE dbo.Brands (
    BrandId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Brands PRIMARY KEY,
    BrandName  NVARCHAR(50) NOT NULL CONSTRAINT UQ_Brands_Name UNIQUE
);

CREATE TABLE dbo.Products (
    ProductId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
    BrandId       INT NOT NULL CONSTRAINT FK_Products_Brands REFERENCES dbo.Brands(BrandId),
    Sku           NVARCHAR(30)  NOT NULL CONSTRAINT UQ_Products_Sku UNIQUE,
    ProductName   NVARCHAR(100) NOT NULL,
    Size          INT NOT NULL,
    Price         DECIMAL(10,2) NOT NULL CONSTRAINT CK_Products_Price CHECK (Price >= 0),
    StockQty      INT NOT NULL CONSTRAINT DF_Products_Stock DEFAULT 0 CONSTRAINT CK_Products_Stock CHECK (StockQty >= 0),
    ReorderLevel  INT NOT NULL CONSTRAINT DF_Products_Reorder DEFAULT 3
);
CREATE INDEX IX_Products_Brand ON dbo.Products(BrandId);

CREATE TABLE dbo.AppUsers (
    UserId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppUsers PRIMARY KEY,
    Username      NVARCHAR(50)  NOT NULL CONSTRAINT UQ_AppUsers_Username UNIQUE,
    PasswordHash  NVARCHAR(64)  NOT NULL,   -- SHA-256 hex, same as PasswordHelper.Hash in C#
    FullName      NVARCHAR(100) NOT NULL,
    [Role]        NVARCHAR(20)  NOT NULL CONSTRAINT CK_AppUsers_Role CHECK ([Role] IN (N'Cashier', N'Head', N'Manager')),
    Branch        NVARCHAR(50)  NOT NULL
);

CREATE TABLE dbo.Requisitions (
    RequisitionId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Requisitions PRIMARY KEY,
    RefNo          NVARCHAR(30) NOT NULL CONSTRAINT UQ_Requisitions_RefNo UNIQUE,   -- REQ-yyyyMMdd-0001
    RequestedById  INT NOT NULL CONSTRAINT FK_Req_RequestedBy REFERENCES dbo.AppUsers(UserId),
    Branch         NVARCHAR(50) NOT NULL,
    DateCreated    DATETIME NOT NULL CONSTRAINT DF_Req_Date DEFAULT GETDATE(),
    [Status]       NVARCHAR(20) NOT NULL CONSTRAINT DF_Req_Status DEFAULT N'Pending'
                   CONSTRAINT CK_Req_Status CHECK ([Status] IN (N'Pending', N'Approved', N'Rejected', N'Cancelled')),
    Remarks        NVARCHAR(200) NULL,
    ReviewedById   INT NULL CONSTRAINT FK_Req_ReviewedBy REFERENCES dbo.AppUsers(UserId),
    DateReviewed   DATETIME NULL,
    RejectReason   NVARCHAR(200) NULL,
    StockCheck     NVARCHAR(200) NULL
);
CREATE INDEX IX_Requisitions_Status ON dbo.Requisitions([Status]);
CREATE INDEX IX_Requisitions_RequestedBy ON dbo.Requisitions(RequestedById);

CREATE TABLE dbo.RequisitionItems (
    ItemId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RequisitionItems PRIMARY KEY,
    RequisitionId  INT NOT NULL CONSTRAINT FK_Items_Req REFERENCES dbo.Requisitions(RequisitionId) ON DELETE CASCADE,
    ProductId      INT NOT NULL CONSTRAINT FK_Items_Product REFERENCES dbo.Products(ProductId),
    Quantity       INT NOT NULL CONSTRAINT CK_Items_Qty CHECK (Quantity >= 1)
);
CREATE INDEX IX_Items_Req ON dbo.RequisitionItems(RequisitionId);
GO

/* ---------------- SAMPLE DATA ---------------- */
INSERT INTO dbo.Brands (BrandName) VALUES
    (N'Nike'),
    (N'Adidas'),
    (N'Puma'),
    (N'Asics'),
    (N'New Balance'),
    (N'Fila'),
    (N'Crocs');

INSERT INTO dbo.Products (BrandId, Sku, ProductName, Size, Price, StockQty, ReorderLevel) VALUES
    (1, N'NIK-01-7', N'Air Force 1 Low', 7, 5495.00, 7, 3),
    (1, N'NIK-01-8', N'Air Force 1 Low', 8, 5495.00, 1, 3),
    (1, N'NIK-01-9', N'Air Force 1 Low', 9, 5495.00, 8, 3),
    (1, N'NIK-01-10', N'Air Force 1 Low', 10, 5495.00, 2, 3),
    (1, N'NIK-02-7', N'Dunk Low', 7, 5895.00, 9, 3),
    (1, N'NIK-02-8', N'Dunk Low', 8, 5895.00, 3, 3),
    (1, N'NIK-02-9', N'Dunk Low', 9, 5895.00, 10, 3),
    (1, N'NIK-02-10', N'Dunk Low', 10, 5895.00, 4, 3),
    (1, N'NIK-03-7', N'Air Max 90', 7, 5995.00, 11, 3),
    (1, N'NIK-03-8', N'Air Max 90', 8, 5995.00, 5, 3),
    (1, N'NIK-03-9', N'Air Max 90', 9, 5995.00, 12, 3),
    (1, N'NIK-03-10', N'Air Max 90', 10, 5995.00, 6, 3),
    (2, N'ADI-01-7', N'Superstar', 7, 4995.00, 0, 3),
    (2, N'ADI-01-8', N'Superstar', 8, 4995.00, 7, 3),
    (2, N'ADI-01-9', N'Superstar', 9, 4995.00, 1, 3),
    (2, N'ADI-01-10', N'Superstar', 10, 4995.00, 8, 3),
    (2, N'ADI-02-7', N'Samba OG', 7, 5495.00, 2, 3),
    (2, N'ADI-02-8', N'Samba OG', 8, 5495.00, 9, 3),
    (2, N'ADI-02-9', N'Samba OG', 9, 5495.00, 3, 3),
    (2, N'ADI-02-10', N'Samba OG', 10, 5495.00, 10, 3),
    (2, N'ADI-03-7', N'Stan Smith', 7, 4495.00, 4, 3),
    (2, N'ADI-03-8', N'Stan Smith', 8, 4495.00, 11, 3),
    (2, N'ADI-03-9', N'Stan Smith', 9, 4495.00, 5, 3),
    (2, N'ADI-03-10', N'Stan Smith', 10, 4495.00, 12, 3),
    (3, N'PUM-01-7', N'Suede Classic', 7, 3495.00, 6, 3),
    (3, N'PUM-01-8', N'Suede Classic', 8, 3495.00, 0, 3),
    (3, N'PUM-01-9', N'Suede Classic', 9, 3495.00, 7, 3),
    (3, N'PUM-01-10', N'Suede Classic', 10, 3495.00, 1, 3),
    (3, N'PUM-02-7', N'RS-X', 7, 4995.00, 8, 3),
    (3, N'PUM-02-8', N'RS-X', 8, 4995.00, 2, 3),
    (3, N'PUM-02-9', N'RS-X', 9, 4995.00, 9, 3),
    (3, N'PUM-02-10', N'RS-X', 10, 4995.00, 3, 3),
    (3, N'PUM-03-7', N'Speedcat', 7, 4295.00, 10, 3),
    (3, N'PUM-03-8', N'Speedcat', 8, 4295.00, 4, 3),
    (3, N'PUM-03-9', N'Speedcat', 9, 4295.00, 11, 3),
    (3, N'PUM-03-10', N'Speedcat', 10, 4295.00, 5, 3),
    (4, N'ASI-01-7', N'Gel-1130', 7, 4995.00, 12, 3),
    (4, N'ASI-01-8', N'Gel-1130', 8, 4995.00, 6, 3),
    (4, N'ASI-01-9', N'Gel-1130', 9, 4995.00, 0, 3),
    (4, N'ASI-01-10', N'Gel-1130', 10, 4995.00, 7, 3),
    (4, N'ASI-02-7', N'Gel-Lyte III', 7, 4495.00, 1, 3),
    (4, N'ASI-02-8', N'Gel-Lyte III', 8, 4495.00, 8, 3),
    (4, N'ASI-02-9', N'Gel-Lyte III', 9, 4495.00, 2, 3),
    (4, N'ASI-02-10', N'Gel-Lyte III', 10, 4495.00, 9, 3),
    (4, N'ASI-03-7', N'Gel-Kayano 14', 7, 5995.00, 3, 3),
    (4, N'ASI-03-8', N'Gel-Kayano 14', 8, 5995.00, 10, 3),
    (4, N'ASI-03-9', N'Gel-Kayano 14', 9, 5995.00, 4, 3),
    (4, N'ASI-03-10', N'Gel-Kayano 14', 10, 5995.00, 11, 3),
    (5, N'NEW-01-7', N'574', 7, 3995.00, 5, 3),
    (5, N'NEW-01-8', N'574', 8, 3995.00, 12, 3),
    (5, N'NEW-01-9', N'574', 9, 3995.00, 6, 3),
    (5, N'NEW-01-10', N'574', 10, 3995.00, 0, 3),
    (5, N'NEW-02-7', N'530', 7, 4995.00, 7, 3),
    (5, N'NEW-02-8', N'530', 8, 4995.00, 1, 3),
    (5, N'NEW-02-9', N'530', 9, 4995.00, 8, 3),
    (5, N'NEW-02-10', N'530', 10, 4995.00, 2, 3),
    (5, N'NEW-03-7', N'327', 7, 4695.00, 9, 3),
    (5, N'NEW-03-8', N'327', 8, 4695.00, 3, 3),
    (5, N'NEW-03-9', N'327', 9, 4695.00, 10, 3),
    (5, N'NEW-03-10', N'327', 10, 4695.00, 4, 3),
    (6, N'FIL-01-7', N'Disruptor II', 7, 3295.00, 11, 3),
    (6, N'FIL-01-8', N'Disruptor II', 8, 3295.00, 5, 3),
    (6, N'FIL-01-9', N'Disruptor II', 9, 3295.00, 12, 3),
    (6, N'FIL-01-10', N'Disruptor II', 10, 3295.00, 6, 3),
    (6, N'FIL-02-7', N'Ray Tracer', 7, 2995.00, 0, 3),
    (6, N'FIL-02-8', N'Ray Tracer', 8, 2995.00, 7, 3),
    (6, N'FIL-02-9', N'Ray Tracer', 9, 2995.00, 1, 3),
    (6, N'FIL-02-10', N'Ray Tracer', 10, 2995.00, 8, 3),
    (6, N'FIL-03-7', N'Oakmont', 7, 2495.00, 2, 3),
    (6, N'FIL-03-8', N'Oakmont', 8, 2495.00, 9, 3),
    (6, N'FIL-03-9', N'Oakmont', 9, 2495.00, 3, 3),
    (6, N'FIL-03-10', N'Oakmont', 10, 2495.00, 10, 3),
    (7, N'CRO-01-7', N'Classic Clog', 7, 2795.00, 4, 3),
    (7, N'CRO-01-8', N'Classic Clog', 8, 2795.00, 11, 3),
    (7, N'CRO-01-9', N'Classic Clog', 9, 2795.00, 5, 3),
    (7, N'CRO-01-10', N'Classic Clog', 10, 2795.00, 12, 3),
    (7, N'CRO-02-7', N'Classic Slide', 7, 1695.00, 6, 3),
    (7, N'CRO-02-8', N'Classic Slide', 8, 1695.00, 0, 3),
    (7, N'CRO-02-9', N'Classic Slide', 9, 1695.00, 7, 3),
    (7, N'CRO-02-10', N'Classic Slide', 10, 1695.00, 1, 3),
    (7, N'CRO-03-7', N'Echo Clog', 7, 3495.00, 8, 3),
    (7, N'CRO-03-8', N'Echo Clog', 8, 3495.00, 2, 3),
    (7, N'CRO-03-9', N'Echo Clog', 9, 3495.00, 9, 3),
    (7, N'CRO-03-10', N'Echo Clog', 10, 3495.00, 3, 3);

-- Demo logins: cashier/cashier123, manager/manager123, head/head123
INSERT INTO dbo.AppUsers (Username, PasswordHash, FullName, [Role], Branch) VALUES
    (N'cashier', N'b4c94003c562bb0d89535eca77f07284fe560fd48a7cc1ed99f0a56263d616ba', N'Maria Santos', N'Cashier', N'Main Store'),
    (N'manager', N'866485796cfa8d7c0cf7111640205b83076433547577511d81f8030ae99ecea5', N'Mark Dela Cruz', N'Manager', N'Main Store'),
    (N'head', N'7fb69d64d085e299af365f6783a4f0e3c77b6fd4bf14febdb3af8ba0416f777d', N'Henry Cruz', N'Head', N'Head Office');
GO

/* Optional sample requisitions so the screens are not empty on first run */
INSERT INTO dbo.Requisitions (RefNo, RequestedById, Branch, DateCreated, [Status], Remarks)
VALUES (N'REQ-SAMPLE-0001', 1, N'Main Store', GETDATE(), N'Pending', N'Restock low sizes');
INSERT INTO dbo.RequisitionItems (RequisitionId, ProductId, Quantity)
SELECT TOP 3 SCOPE_IDENTITY(), ProductId, 6 FROM dbo.Products WHERE StockQty <= ReorderLevel ORDER BY ProductId;
GO

/* ---------------- QUICK CHECKS ---------------- */
SELECT COUNT(*) AS Brands FROM dbo.Brands;
SELECT COUNT(*) AS Products FROM dbo.Products;
SELECT UserId, Username, FullName, [Role], Branch FROM dbo.AppUsers;
SELECT r.RefNo, r.[Status], i.Quantity, p.Sku FROM dbo.Requisitions r
JOIN dbo.RequisitionItems i ON i.RequisitionId = r.RequisitionId
JOIN dbo.Products p ON p.ProductId = i.ProductId;
GO

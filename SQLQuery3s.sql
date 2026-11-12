-- =====================================================================
--  Solemate Delivery module - upgrade
--  Run AFTER 6_Delivery.sql. Safe to run more than once.
--
--  Adds:
--   1. Expected delivery date + overdue flag
--   2. Printable receiving report (data procedures for the print/PDF screen)
--   3. Suppliers table (dropdown instead of free text)
--   4. Search + date-range filter on the delivery list
--   5. Unit cost per item + delivery value
--
--  Existing procedures keep working: every new parameter has a default and
--  every new result column is added at the END of the result set.
-- =====================================================================
USE SolemateKicksDB;
GO

IF OBJECT_ID('dbo.Deliveries', 'U') IS NULL
BEGIN
    RAISERROR('Run 6_Delivery.sql first: the Deliveries table was not found.', 16, 1);
    SET NOEXEC ON;   -- skips the rest of this script
END
GO

-- ---------------------------------------------------------------------
--  3. SUPPLIERS
-- ---------------------------------------------------------------------
IF OBJECT_ID('dbo.Suppliers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Suppliers
    (
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

-- ---------------------------------------------------------------------
--  New columns on existing tables
-- ---------------------------------------------------------------------
IF COL_LENGTH('dbo.Deliveries', 'ExpectedDate') IS NULL
    ALTER TABLE dbo.Deliveries ADD ExpectedDate DATE NULL;
GO

IF COL_LENGTH('dbo.Deliveries', 'SupplierId') IS NULL
    ALTER TABLE dbo.Deliveries ADD SupplierId INT NULL
        CONSTRAINT FK_Deliveries_Supplier REFERENCES dbo.Suppliers(SupplierId);
GO

IF COL_LENGTH('dbo.DeliveryItems', 'UnitCost') IS NULL
    ALTER TABLE dbo.DeliveryItems ADD UnitCost DECIMAL(10,2) NULL
        CONSTRAINT CK_DelItems_Cost CHECK (UnitCost IS NULL OR UnitCost >= 0);
GO

-- ---------------------------------------------------------------------
--  Back-fill existing rows
-- ---------------------------------------------------------------------
INSERT INTO dbo.Suppliers (SupplierName)
SELECT DISTINCT d.SupplierName
FROM dbo.Deliveries d
WHERE NOT EXISTS (SELECT 1 FROM dbo.Suppliers s WHERE s.SupplierName = d.SupplierName);

UPDATE d
SET d.SupplierId = s.SupplierId
FROM dbo.Deliveries d
JOIN dbo.Suppliers s ON s.SupplierName = d.SupplierName
WHERE d.SupplierId IS NULL;

-- deliveries still waiting get a default due date of 7 days after they were recorded
UPDATE dbo.Deliveries
SET ExpectedDate = CAST(DATEADD(DAY, 7, DateCreated) AS DATE)
WHERE ExpectedDate IS NULL AND [Status] IN (N'Expected', N'Partial');
GO

-- ---------------------------------------------------------------------
--  3. Supplier procedures
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_DelGetSuppliers
    @ActiveOnly INT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT SupplierId,
           SupplierName,
           ISNULL(ContactPerson, N'') AS ContactPerson,
           ISNULL(Phone, N'')         AS Phone,
           ISNULL(Email, N'')         AS Email,
           IsActive
    FROM dbo.Suppliers
    WHERE @ActiveOnly = 0 OR IsActive = 1
    ORDER BY SupplierName;
END
GO

-- @SupplierId = 0 adds a new supplier, otherwise updates that supplier (manager only)
CREATE OR ALTER PROCEDURE dbo.usp_DelSaveSupplier
    @UserId        INT,
    @SupplierId    INT,
    @SupplierName  NVARCHAR(100),
    @ContactPerson NVARCHAR(100) = NULL,
    @Phone         NVARCHAR(30)  = NULL,
    @Email         NVARCHAR(100) = NULL,
    @IsActive      INT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 51021, 'Only the manager can manage suppliers.', 1;

    SET @SupplierName = LTRIM(RTRIM(ISNULL(@SupplierName, N'')));
    IF @SupplierName = N''
        THROW 51022, 'Supplier name is required.', 1;

    IF EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierName = @SupplierName AND SupplierId <> @SupplierId)
        THROW 51023, 'A supplier with this name already exists.', 1;

    IF @SupplierId = 0
    BEGIN
        INSERT INTO dbo.Suppliers (SupplierName, ContactPerson, Phone, Email, IsActive)
        VALUES (@SupplierName, NULLIF(LTRIM(RTRIM(@ContactPerson)), N''), NULLIF(LTRIM(RTRIM(@Phone)), N''),
                NULLIF(LTRIM(RTRIM(@Email)), N''), CASE WHEN @IsActive = 0 THEN 0 ELSE 1 END);
        SET @SupplierId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId = @SupplierId)
            THROW 51024, 'The supplier was not found.', 1;

        UPDATE dbo.Suppliers
        SET SupplierName  = @SupplierName,
            ContactPerson = NULLIF(LTRIM(RTRIM(@ContactPerson)), N''),
            Phone         = NULLIF(LTRIM(RTRIM(@Phone)), N''),
            Email         = NULLIF(LTRIM(RTRIM(@Email)), N''),
            IsActive      = CASE WHEN @IsActive = 0 THEN 0 ELSE 1 END
        WHERE SupplierId = @SupplierId;

        -- keep the name on existing deliveries in sync
        UPDATE dbo.Deliveries SET SupplierName = @SupplierName WHERE SupplierId = @SupplierId;
    END

    SELECT @SupplierId AS SupplierId;
END
GO

-- ---------------------------------------------------------------------
--  1 + 3 + 5. Record an expected delivery
--  @ItemsXml: <Items><Item ProductId="5" Quantity="10" UnitCost="2500.00"/>...</Items>
--             (UnitCost is optional)
--  @SupplierId > 0 picks a supplier from the list; otherwise @SupplierName is
--  matched by name and created if it does not exist yet.
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_DelCreate
    @UserId        INT,
    @SupplierName  NVARCHAR(100),
    @PoRef         NVARCHAR(30),
    @RequisitionId INT,
    @Remarks       NVARCHAR(200),
    @ItemsXml      NVARCHAR(MAX),
    @ExpectedDate  DATE = NULL,
    @SupplierId    INT  = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.AppUsers WHERE UserId = @UserId AND [Role] = N'Manager')
        THROW 51001, 'Only the manager can record deliveries.', 1;

    IF @SupplierId <> 0
    BEGIN
        SELECT @SupplierName = SupplierName FROM dbo.Suppliers WHERE SupplierId = @SupplierId AND IsActive = 1;
        IF @SupplierName IS NULL
            THROW 51007, 'The selected supplier was not found or is inactive.', 1;
    END

    SET @SupplierName = LTRIM(RTRIM(ISNULL(@SupplierName, N'')));
    IF @SupplierName = N''
        THROW 51002, 'Supplier is required.', 1;

    IF @ExpectedDate IS NOT NULL AND @ExpectedDate < CAST(GETDATE() AS DATE)
        THROW 51008, 'The expected delivery date cannot be in the past.', 1;

    IF @RequisitionId <> 0 AND NOT EXISTS (SELECT 1 FROM dbo.Requisitions WHERE RequisitionId = @RequisitionId AND [Status] = N'Approved')
        THROW 51003, 'The linked requisition must be an approved requisition.', 1;

    DECLARE @x XML = CAST(@ItemsXml AS XML);
    DECLARE @Items TABLE (ProductId INT NOT NULL PRIMARY KEY, Quantity INT NOT NULL, UnitCost DECIMAL(10,2) NULL);

    INSERT INTO @Items (ProductId, Quantity, UnitCost)
    SELECT ProductId, SUM(Quantity), MAX(UnitCost)
    FROM (SELECT t.i.value('@ProductId', 'int')           AS ProductId,
                 t.i.value('@Quantity',  'int')           AS Quantity,
                 t.i.value('@UnitCost',  'decimal(10,2)') AS UnitCost
          FROM @x.nodes('/Items/Item') AS t(i)) s
    GROUP BY ProductId;

    IF NOT EXISTS (SELECT 1 FROM @Items)
        THROW 51004, 'Add at least one item to the delivery.', 1;
    IF EXISTS (SELECT 1 FROM @Items WHERE Quantity < 1)
        THROW 51005, 'Every ordered quantity must be at least 1.', 1;
    IF EXISTS (SELECT 1 FROM @Items WHERE UnitCost < 0)
        THROW 51009, 'Unit cost cannot be negative.', 1;
    IF EXISTS (SELECT 1 FROM @Items i LEFT JOIN dbo.Products p ON p.ProductId = i.ProductId WHERE p.ProductId IS NULL)
        THROW 51006, 'One of the selected products no longer exists.', 1;

    BEGIN TRAN;

        -- free-text supplier: reuse the existing one with that name, or create it
        IF @SupplierId = 0
        BEGIN
            SELECT @SupplierId = SupplierId FROM dbo.Suppliers WHERE SupplierName = @SupplierName;
            IF @SupplierId IS NULL OR @SupplierId = 0
            BEGIN
                INSERT INTO dbo.Suppliers (SupplierName) VALUES (@SupplierName);
                SET @SupplierId = SCOPE_IDENTITY();
            END
        END

        DECLARE @Prefix NVARCHAR(30) = N'DEL-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + N'-';
        DECLARE @Next   INT = (SELECT COUNT(*) FROM dbo.Deliveries WITH (UPDLOCK, HOLDLOCK)
                               WHERE DeliveryRef LIKE @Prefix + N'%') + 1;
        DECLARE @Ref    NVARCHAR(30) = @Prefix + RIGHT(N'0000' + CAST(@Next AS NVARCHAR(10)), 4);

        INSERT INTO dbo.Deliveries (DeliveryRef, PoRef, SupplierName, SupplierId, RequisitionId, ExpectedDate,
                                    [Status], DateCreated, CreatedById, Remarks)
        VALUES (@Ref, NULLIF(LTRIM(RTRIM(@PoRef)), N''), @SupplierName, @SupplierId, NULLIF(@RequisitionId, 0), @ExpectedDate,
                N'Expected', GETDATE(), @UserId, NULLIF(LTRIM(RTRIM(@Remarks)), N''));

        DECLARE @Id INT = SCOPE_IDENTITY();

        INSERT INTO dbo.DeliveryItems (DeliveryId, ProductId, OrderedQty, UnitCost)
        SELECT @Id, ProductId, Quantity, UnitCost FROM @Items;

    COMMIT;

    SELECT @Ref AS DeliveryRef;
END
GO

-- ---------------------------------------------------------------------
--  1 + 4 + 5. Delivery list with search, date range, overdue and value
--  @Status: All / Expected / Partial / Complete / Short / Overdue
--  @Search: matches delivery ref, PO ref, supplier or requisition ref
--  @DateFrom / @DateTo: filter on the date the delivery was recorded
--  New columns (SupplierId, ExpectedDate, IsOverdue, TotalValue) are at the end.
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_DelGetList
    @Status   NVARCHAR(20),
    @Search   NVARCHAR(100) = NULL,
    @DateFrom DATE = NULL,
    @DateTo   DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @Search = NULLIF(LTRIM(RTRIM(@Search)), N'');

    SELECT d.DeliveryId,
           d.DeliveryRef,
           ISNULL(d.PoRef, N'')   AS PoRef,
           d.SupplierName,
           ISNULL(r.RefNo, N'')   AS RequisitionRef,
           d.[Status],
           d.DateCreated,
           d.DateReceived,
           ISNULL(t.Lines, 0)     AS Lines,
           ISNULL(t.Ordered, 0)   AS OrderedQty,
           ISNULL(t.Accepted, 0)  AS AcceptedQty,
           ISNULL(t.Rejected, 0)  AS RejectedQty,
           ISNULL(t.Ordered, 0) - ISNULL(t.Accepted, 0) AS OutstandingQty,
           u.FullName             AS CreatedBy,
           d.Remarks,
           d.SupplierId,
           d.ExpectedDate,
           CASE WHEN d.[Status] IN (N'Expected', N'Partial')
                 AND d.ExpectedDate IS NOT NULL
                 AND d.ExpectedDate < CAST(GETDATE() AS DATE)
                THEN 1 ELSE 0 END AS IsOverdue,
           ISNULL(t.Value, 0)     AS TotalValue
    FROM dbo.Deliveries d
    JOIN dbo.AppUsers u ON u.UserId = d.CreatedById
    LEFT JOIN dbo.Requisitions r ON r.RequisitionId = d.RequisitionId
    LEFT JOIN (SELECT DeliveryId, COUNT(*) AS Lines, SUM(OrderedQty) AS Ordered,
                      SUM(AcceptedQty) AS Accepted, SUM(RejectedQty) AS Rejected,
                      SUM(OrderedQty * ISNULL(UnitCost, 0)) AS Value
               FROM dbo.DeliveryItems GROUP BY DeliveryId) t ON t.DeliveryId = d.DeliveryId
    WHERE (   @Status IS NULL OR @Status = N'All' OR d.[Status] = @Status
           OR (@Status = N'Overdue' AND d.[Status] IN (N'Expected', N'Partial')
               AND d.ExpectedDate IS NOT NULL AND d.ExpectedDate < CAST(GETDATE() AS DATE)))
      AND (   @Search IS NULL
           OR d.DeliveryRef  LIKE N'%' + @Search + N'%'
           OR d.PoRef        LIKE N'%' + @Search + N'%'
           OR d.SupplierName LIKE N'%' + @Search + N'%'
           OR r.RefNo        LIKE N'%' + @Search + N'%')
      AND (@DateFrom IS NULL OR d.DateCreated >= @DateFrom)
      AND (@DateTo   IS NULL OR d.DateCreated <  DATEADD(DAY, 1, @DateTo))
    ORDER BY d.DateCreated DESC, d.DeliveryId DESC;
END
GO

-- ---------------------------------------------------------------------
--  5. Items of a delivery (UnitCost and LineValue added at the end)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_DelGetItems
    @DeliveryId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT di.ItemId,
           di.ProductId,
           p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           di.OrderedQty,
           di.AcceptedQty,
           di.RejectedQty,
           di.OrderedQty - di.AcceptedQty AS OutstandingQty,
           ISNULL(di.Note, N'') AS Note,
           ISNULL(di.UnitCost, 0)                AS UnitCost,
           di.OrderedQty * ISNULL(di.UnitCost, 0) AS LineValue
    FROM dbo.DeliveryItems di
    JOIN dbo.Products p ON p.ProductId = di.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    WHERE di.DeliveryId = @DeliveryId
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

-- ---------------------------------------------------------------------
--  2. Printable receiving report: header + lines of one report
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.usp_DelGetReportHeader
    @ReportRef NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT rr.ReportRef,
           rr.DateReceived,
           u.FullName              AS ReceivedBy,
           rr.TotalAccepted,
           rr.TotalRejected,
           ISNULL(rr.Remarks, N'') AS Remarks,
           d.DeliveryRef,
           ISNULL(d.PoRef, N'')    AS PoRef,
           d.SupplierName,
           ISNULL(req.RefNo, N'')  AS RequisitionRef
    FROM dbo.ReceivingReports rr
    JOIN dbo.Deliveries d ON d.DeliveryId = rr.DeliveryId
    JOIN dbo.AppUsers   u ON u.UserId     = rr.ReceivedById
    LEFT JOIN dbo.Requisitions req ON req.RequisitionId = d.RequisitionId
    WHERE rr.ReportRef = @ReportRef;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_DelGetReportLines
    @ReportRef NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.Sku,
           b.BrandName   AS Brand,
           p.ProductName AS Product,
           p.Size,
           ri.AcceptedQty,
           ri.RejectedQty,
           ISNULL(ri.Note, N'')                      AS Note,
           ISNULL(di.UnitCost, 0)                    AS UnitCost,
           ri.AcceptedQty * ISNULL(di.UnitCost, 0)   AS LineValue
    FROM dbo.ReceivingReports rr
    JOIN dbo.ReceivingReportItems ri ON ri.ReportId  = rr.ReportId
    JOIN dbo.Products p ON p.ProductId = ri.ProductId
    JOIN dbo.Brands   b ON b.BrandId   = p.BrandId
    LEFT JOIN dbo.DeliveryItems di ON di.ItemId = ri.ItemId
    WHERE rr.ReportRef = @ReportRef
    ORDER BY b.BrandName, p.ProductName, p.Size;
END
GO

-- ---------------------------------------------------------------------
--  Optional: remove the sample delivery before your demo / submission.
--  Remove the two dashes in front of the next line to run it.
-- ---------------------------------------------------------------------
-- DELETE FROM dbo.Deliveries WHERE DeliveryRef = N'DEL-SAMPLE-0001';
GO

SET NOEXEC OFF;
GO

-- ---------------------------------------------------------------------
--  CHECK (should show 6 tables, 13 procedures, 3 new columns)
-- ---------------------------------------------------------------------
SELECT COUNT(*) AS DeliveryTables FROM sys.tables WHERE name IN (N'Deliveries', N'DeliveryItems', N'ReceivingReports', N'ReceivingReportItems', N'DeliveryDiscrepancies', N'Suppliers');
SELECT COUNT(*) AS DeliveryProcs  FROM sys.procedures WHERE name LIKE 'usp[_]Del%';
SELECT (CASE WHEN COL_LENGTH('dbo.Deliveries', 'ExpectedDate') IS NOT NULL THEN 1 ELSE 0 END)
     + (CASE WHEN COL_LENGTH('dbo.Deliveries', 'SupplierId')   IS NOT NULL THEN 1 ELSE 0 END)
     + (CASE WHEN COL_LENGTH('dbo.DeliveryItems', 'UnitCost')  IS NOT NULL THEN 1 ELSE 0 END) AS NewColumns;
GO
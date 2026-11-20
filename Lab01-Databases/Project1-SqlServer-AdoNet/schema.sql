/* =============================================================================
   ПРОЄКТ №1 — VideoRentalRentalsDb  (SQL Server + ADO.NET & Dapper)
   Контекст: «Оренда» (Rentals) — транзакційний процес оренди фільмів клієнтам.
   -----------------------------------------------------------------------------
   Файл: schema.sql — DDL (таблиці, ключі, обмеження, індекси).
   Порядок запуску: schema.sql -> procedures.sql -> seed.sql -> verify.sql
   -----------------------------------------------------------------------------
   Типи зв'язків, продемонстровані в цій базі:
     1:1  Rentals          <-> RentalDetails        (PK залежної таблиці = FK)
     1:N  Customers         -> Rentals              (FK на боці «багатьох»)
     M:N  Rentals          <-> Movies              (через RentalItems)
   Посилання на інші контексти — БЕЗ FOREIGN KEY:
     Movies.Id            -> Movie.Id   у VideoRentalCatalogDb (Проєкт №2)
   ============================================================================= */

IF DB_ID('VideoRentalRentalsDb') IS NULL
    CREATE DATABASE VideoRentalRentalsDb;
GO

USE VideoRentalRentalsDb;
GO

/* Налаштування сеансу, ОБОВ'ЯЗКОВІ для фільтрованих індексів.
   sqlcmd за замовчуванням має QUOTED_IDENTIFIER OFF, тому задаємо явно
   (або запускайте sqlcmd з ключем -I). */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ---------- ідемпотентність: прибираємо об'єкти у зворотному порядку ---------- */
IF OBJECT_ID('dbo.LateFeePayments', 'U')      IS NOT NULL DROP TABLE dbo.LateFeePayments;
IF OBJECT_ID('dbo.RentalStatusHistory', 'U') IS NOT NULL DROP TABLE dbo.RentalStatusHistory;
IF OBJECT_ID('dbo.RentalItems', 'U')         IS NOT NULL DROP TABLE dbo.RentalItems;
IF OBJECT_ID('dbo.RentalDetails', 'U')       IS NOT NULL DROP TABLE dbo.RentalDetails;
IF OBJECT_ID('dbo.Rentals', 'U')             IS NOT NULL DROP TABLE dbo.Rentals;
IF OBJECT_ID('dbo.Movies', 'U')             IS NOT NULL DROP TABLE dbo.Movies;
IF OBJECT_ID('dbo.Customers', 'U')           IS NOT NULL DROP TABLE dbo.Customers;
GO

/* =============================================================================
   1. Customers — клієнти відеопрокату. Власник даних: цей контекст.
      Аналог Buyer / Customer у референсному застосунку eShop.
      PK: BIGINT IDENTITY — вставка йде лише з цього сервісу, послідовні
      значення дають компактні індекси (GUID тут не потрібен).
   ============================================================================= */
CREATE TABLE dbo.Customers
(
    Id          BIGINT         IDENTITY(1,1) CONSTRAINT PK_Customers PRIMARY KEY,
    MembershipNumber  NVARCHAR(20)   NOT NULL CONSTRAINT UQ_Customers_MembershipNumber UNIQUE,
    Email       NVARCHAR(256)  NOT NULL CONSTRAINT UQ_Customers_Email      UNIQUE,
    FullName    NVARCHAR(200)  NOT NULL,
    Phone       NVARCHAR(30)   NULL,
    IsBlocked   BIT            NOT NULL CONSTRAINT DF_Customers_IsBlocked DEFAULT 0,

    -- аудитні колонки
    CreatedAt   DATETIME2(3)   NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100)  NULL,
    UpdatedAt   DATETIME2(3)   NULL,
    UpdatedBy   NVARCHAR(100)  NULL,
    IsDeleted   BIT            NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT 0,  -- soft delete
    RowVersion  ROWVERSION,                                                          -- оптимістична конкурентність

    CONSTRAINT CK_Customers_Email CHECK (Email LIKE '%_@_%._%')
);
GO

/* індекс під запит: «знайти клієнта за номером картки серед активних»
   (фільтрований — обслуговує лише «живі» записи, менший за розміром)        */
CREATE INDEX IX_Customers_Active_MembershipNumber
    ON dbo.Customers (MembershipNumber) INCLUDE (FullName, Email)
    WHERE IsDeleted = 0;
GO

/* =============================================================================
   2. Movies — ЛОКАЛЬНА КОПІЯ ДОВІДНИКА з БД Каталогу (Проєкт №2).
      Id — той самий ідентифікатор, що й Movie.Id у VideoRentalCatalogDb,
      але FOREIGN KEY між базами НЕ створюється (database-per-service).
      Оновлюється подіями з Каталогу (див. таблицю політики дублювання).
   ============================================================================= */
CREATE TABLE dbo.Movies
(
    Id               BIGINT        NOT NULL CONSTRAINT PK_Movies PRIMARY KEY,  -- без IDENTITY: Id приходить з Каталогу
    Title            NVARCHAR(300) NOT NULL,
    Director           NVARCHAR(200) NOT NULL,
    Barcode             NVARCHAR(20)  NULL,
    DailyRate         DECIMAL(18,2) NOT NULL CONSTRAINT CK_Movies_DailyRate CHECK (DailyRate > 0),
    DiscsAvailable  INT           NOT NULL CONSTRAINT DF_Movies_DiscsAvailable DEFAULT 0
                                   CONSTRAINT CK_Movies_DiscsAvailable CHECK (DiscsAvailable >= 0),
    SyncedAt         DATETIME2(3)  NOT NULL CONSTRAINT DF_Movies_SyncedAt DEFAULT SYSUTCDATETIME(),
    RowVersion       ROWVERSION
);
GO

/* індекс під запит: «пошук фільму за назвою у формі оренди» */
CREATE INDEX IX_Movies_Title ON dbo.Movies (Title) INCLUDE (Director, DailyRate, DiscsAvailable);
GO

/* =============================================================================
   3. Rentals — оренда (аналог Order в eShop). Коренева сутність агрегату.
      1:N  Customers -> Rentals : FK стоїть на боці «багатьох».
      Статуси та допустимі переходи (за зразком OrderStatus з eShop):
        Submitted -> AwaitingValidation -> DiscsConfirmed -> PickedUp -> Returned
        Cancelled — лише ДО оренди фільмів на руки (PickedUp).
   ============================================================================= */
CREATE TABLE dbo.Rentals
(
    Id          BIGINT        IDENTITY(1,1) CONSTRAINT PK_Rentals PRIMARY KEY,
    CustomerId    BIGINT        NOT NULL
                CONSTRAINT FK_Rentals_Customers REFERENCES dbo.Customers (Id),   -- БЕЗ каскаду:
                                                                           -- клієнта з історією не видаляємо
    Status      NVARCHAR(30)  NOT NULL CONSTRAINT DF_Rentals_Status DEFAULT 'Submitted'
                CONSTRAINT CK_Rentals_Status CHECK (Status IN
                    ('Submitted', 'AwaitingValidation', 'DiscsConfirmed',
                     'PickedUp', 'Returned', 'Cancelled')),
    RentalDate    DATETIME2(3)  NOT NULL CONSTRAINT DF_Rentals_RentalDate DEFAULT SYSUTCDATETIME(),
    DueDate     DATE          NOT NULL,                        -- коли треба повернути
    PickedUpAt    DATETIME2(3)  NULL,
    ReturnedAt  DATETIME2(3)  NULL,
    LateFeeAmount  DECIMAL(18,2) NOT NULL CONSTRAINT DF_Rentals_LateFeeAmount DEFAULT 0
                CONSTRAINT CK_Rentals_LateFeeAmount CHECK (LateFeeAmount >= 0),

    CreatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_Rentals_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100) NULL,
    UpdatedAt   DATETIME2(3)  NULL,
    UpdatedBy   NVARCHAR(100) NULL,
    IsDeleted   BIT           NOT NULL CONSTRAINT DF_Rentals_IsDeleted DEFAULT 0,
    RowVersion  ROWVERSION,

    CONSTRAINT CK_Rentals_DueAfterRental CHECK (DueDate >= CAST(RentalDate AS DATE))
);
GO

/* індекс під запит: «оренди клієнта з певним статусом» (основний список в API) */
CREATE INDEX IX_Rentals_CustomerId_Status ON dbo.Rentals (CustomerId, Status) INCLUDE (DueDate, LateFeeAmount);
GO
/* індекс під запит: «прострочені оренди» — фільтрований, лише видані на руки */
CREATE INDEX IX_Rentals_Overdue ON dbo.Rentals (DueDate) INCLUDE (CustomerId)
    WHERE Status = 'PickedUp' AND IsDeleted = 0;
GO

/* =============================================================================
   4. RentalDetails — 1:1 до Rentals. Первинний ключ є водночас зовнішнім ключем,
      тому на одну оренду може існувати не більше одного рядка деталей.
      Каскадне видалення тут доречне: деталі не існують без оренди.
   ============================================================================= */
CREATE TABLE dbo.RentalDetails
(
    RentalId          BIGINT         NOT NULL
                    CONSTRAINT PK_RentalDetails PRIMARY KEY
                    CONSTRAINT FK_RentalDetails_Rentals REFERENCES dbo.Rentals (Id) ON DELETE CASCADE,
    PickupStore    NVARCHAR(100)  NOT NULL,
    ClerkName   NVARCHAR(200)  NULL,
    Comment         NVARCHAR(1000) NULL
);
GO

/* =============================================================================
   5. RentalItems — M:N між Rentals і Movies через проміжну таблицю з власними полями.
      MovieTitle / MovieDirector / DailyRate — ЗНІМОК на момент оформлення оренди
      (так само, як OrderItem в eShop зберігає ProductName і UnitPrice).
      Знімок свідомо НЕ оновлюється при зміні каталогу.
   ============================================================================= */
CREATE TABLE dbo.RentalItems
(
    RentalId      BIGINT        NOT NULL
                CONSTRAINT FK_RentalItems_Rentals REFERENCES dbo.Rentals (Id) ON DELETE CASCADE,
    MovieId      BIGINT        NOT NULL
                CONSTRAINT FK_RentalItems_Movies REFERENCES dbo.Movies (Id),
    MovieTitle   NVARCHAR(300) NOT NULL,                        -- знімок
    MovieDirector  NVARCHAR(200) NOT NULL,                        -- знімок
    DailyRate    DECIMAL(18,2) NOT NULL CONSTRAINT CK_RentalItems_DailyRate CHECK (DailyRate > 0),  -- знімок
    Units       INT           NOT NULL CONSTRAINT CK_RentalItems_Units CHECK (Units > 0),
    Discount    DECIMAL(18,2) NOT NULL CONSTRAINT DF_RentalItems_Discount DEFAULT 0
                CONSTRAINT CK_RentalItems_Discount CHECK (Discount >= 0),

    CONSTRAINT PK_RentalItems PRIMARY KEY (RentalId, MovieId)       -- складений ключ M:N
);
GO

/* індекс під запит: «у яких орендах фігурує цей фільм» (зворотний бік складеного PK) */
CREATE INDEX IX_RentalItems_MovieId ON dbo.RentalItems (MovieId) INCLUDE (Units);
GO

/* =============================================================================
   6. RentalStatusHistory — журнал переходів статусів (1:N від Rentals).
      Пишеться збережуваними процедурами в тій самій транзакції, що й зміна статусу.
   ============================================================================= */
CREATE TABLE dbo.RentalStatusHistory
(
    Id          BIGINT        IDENTITY(1,1) CONSTRAINT PK_RentalStatusHistory PRIMARY KEY,
    RentalId      BIGINT        NOT NULL
                CONSTRAINT FK_RentalStatusHistory_Rentals REFERENCES dbo.Rentals (Id) ON DELETE CASCADE,
    FromStatus  NVARCHAR(30)  NULL,
    ToStatus    NVARCHAR(30)  NOT NULL,
    ChangedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_RentalStatusHistory_ChangedAt DEFAULT SYSUTCDATETIME(),
    ChangedBy   NVARCHAR(100) NULL,
    Note        NVARCHAR(400) NULL
);
GO

/* індекс під запит: «історія однієї оренди в хронологічному порядку» */
CREATE INDEX IX_RentalStatusHistory_RentalId_ChangedAt ON dbo.RentalStatusHistory (RentalId, ChangedAt);
GO

/* =============================================================================
   7. LateFeePayments — оплати пені (1:N від Rentals). Транзакційні дані контексту.
   ============================================================================= */
CREATE TABLE dbo.LateFeePayments
(
    Id              BIGINT        IDENTITY(1,1) CONSTRAINT PK_LateFeePayments PRIMARY KEY,
    RentalId          BIGINT        NOT NULL
                    CONSTRAINT FK_LateFeePayments_Rentals REFERENCES dbo.Rentals (Id) ON DELETE CASCADE,
    Amount          DECIMAL(18,2) NOT NULL CONSTRAINT CK_LateFeePayments_Amount CHECK (Amount > 0),
    Method          NVARCHAR(30)  NOT NULL CONSTRAINT DF_LateFeePayments_Method DEFAULT 'Cash'
                    CONSTRAINT CK_LateFeePayments_Method CHECK (Method IN ('Cash', 'Card', 'Online')),
    PaidAt          DATETIME2(3)  NOT NULL CONSTRAINT DF_LateFeePayments_PaidAt DEFAULT SYSUTCDATETIME(),
    IdempotencyKey  NVARCHAR(64)  NULL CONSTRAINT UQ_LateFeePayments_IdempotencyKey UNIQUE  -- захист від повторів
);
GO

/* індекс під запит: «усі оплати по оренди» */
CREATE INDEX IX_LateFeePayments_RentalId ON dbo.LateFeePayments (RentalId) INCLUDE (Amount, PaidAt);
GO

/* =============================================================================
   Денормалізоване представлення для читань (звіт без ручних JOIN-ів у коді).
   ============================================================================= */
IF OBJECT_ID('dbo.vw_RentalSummary', 'V') IS NOT NULL DROP VIEW dbo.vw_RentalSummary;
GO
CREATE VIEW dbo.vw_RentalSummary
AS
SELECT  l.Id            AS RentalId,
        l.Status,
        l.RentalDate,
        l.DueDate,
        l.ReturnedAt,
        l.LateFeeAmount,
        r.Id            AS CustomerId,
        r.FullName      AS CustomerName,
        r.MembershipNumber,
        d.PickupStore,
        COUNT(i.MovieId) AS TitlesCount,
        ISNULL(SUM(i.Units), 0) AS UnitsTotal,
        ISNULL(SUM(i.DailyRate * i.Units - i.Discount), 0) AS DailyChargeTotal
FROM        dbo.Rentals        AS l
INNER JOIN  dbo.Customers      AS r ON r.Id = l.CustomerId
LEFT  JOIN  dbo.RentalDetails  AS d ON d.RentalId = l.Id
LEFT  JOIN  dbo.RentalItems    AS i ON i.RentalId = l.Id
WHERE   l.IsDeleted = 0
GROUP BY l.Id, l.Status, l.RentalDate, l.DueDate, l.ReturnedAt, l.LateFeeAmount,
         r.Id, r.FullName, r.MembershipNumber, d.PickupStore;
GO

PRINT N'VideoRentalRentalsDb: schema.sql виконано успішно.';
GO

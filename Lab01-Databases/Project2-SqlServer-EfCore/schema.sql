/* =============================================================================
   ПРОЄКТ №2 — VideoRentalCatalogDb  (SQL Server + EF Core Code-First)
   Контекст: «Каталог» (Catalog) — довідник фільмів, режисерів, жанрів і дисків.
   -----------------------------------------------------------------------------
   УВАГА. На етапі Лаб. 01 ця база ПРОЄКТУЄТЬСЯ. Цей файл — референсний DDL,
   що описує цільову схему; він слугує для перегляду ERD і для порівняння з
   тим, що згенерує перша міграція EF Core у Лаб. 03 (InitialCreate).
   Базу для Лаб. 03 створює команда:
       dotnet ef database update --project VideoRental.Catalog.Dal
                                 --startup-project VideoRental.Catalog.Api
   Виконувати цей скрипт вручну НЕ обов'язково — він документує схему.
   -----------------------------------------------------------------------------
   Типи зв'язків:
     1:1  Movies   <-> MovieDetails    (PK залежної таблиці = FK)
     1:N  Directors  -> Movies          (FK на боці «багатьох»)
     1:N  Movies    -> DiscDiscs
     M:N  Movies   <-> Genres         (через MovieGenres з власним полем AddedAt)
   Жодного FOREIGN KEY за межі цієї бази.
   ============================================================================= */

IF DB_ID('VideoRentalCatalogDb') IS NULL
    CREATE DATABASE VideoRentalCatalogDb;
GO

USE VideoRentalCatalogDb;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.MovieGenres', 'U')  IS NOT NULL DROP TABLE dbo.MovieGenres;
IF OBJECT_ID('dbo.DiscDiscs', 'U')  IS NOT NULL DROP TABLE dbo.DiscDiscs;
IF OBJECT_ID('dbo.MovieDetails', 'U') IS NOT NULL DROP TABLE dbo.MovieDetails;
IF OBJECT_ID('dbo.Movies', 'U')       IS NOT NULL DROP TABLE dbo.Movies;
IF OBJECT_ID('dbo.Genres', 'U')      IS NOT NULL DROP TABLE dbo.Genres;
IF OBJECT_ID('dbo.Directors', 'U')     IS NOT NULL DROP TABLE dbo.Directors;
GO

/* =============================================================================
   1. Directors — режисери (довідник). 1:N -> Movies.
      PK: INT IDENTITY — довідник невеликий, вставка лише з цього сервісу.
   ============================================================================= */
CREATE TABLE dbo.Directors
(
    Id         INT           IDENTITY(1,1) CONSTRAINT PK_Directors PRIMARY KEY,
    FullName   NVARCHAR(200) NOT NULL,
    Country    NVARCHAR(100) NULL,
    BirthYear  INT           NULL CONSTRAINT CK_Directors_BirthYear CHECK (BirthYear IS NULL OR BirthYear BETWEEN 1000 AND 2100),
    CreatedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_Directors_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt  DATETIME2(3)  NULL,
    IsDeleted  BIT           NOT NULL CONSTRAINT DF_Directors_IsDeleted DEFAULT 0,
    RowVersion ROWVERSION
);
GO

/* індекс під запит: «пошук режисера за прізвищем» */
CREATE INDEX IX_Directors_FullName ON dbo.Directors (FullName);
GO

/* =============================================================================
   2. Genres — жанри (довідник) для зв'язку M:N з фільмами.
   ============================================================================= */
CREATE TABLE dbo.Genres
(
    Id          INT           IDENTITY(1,1) CONSTRAINT PK_Genres PRIMARY KEY,
    Name        NVARCHAR(100) NOT NULL CONSTRAINT UQ_Genres_Name UNIQUE,
    Slug        NVARCHAR(100) NOT NULL CONSTRAINT UQ_Genres_Slug UNIQUE,
    Description NVARCHAR(500) NULL,
    CreatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_Genres_CreatedAt DEFAULT SYSUTCDATETIME(),
    IsDeleted   BIT           NOT NULL CONSTRAINT DF_Genres_IsDeleted DEFAULT 0
);
GO

/* =============================================================================
   3. Movies — коренева сутність каталогу (аналог CatalogItem в eShop).
      Бізнес-правила (ціна > 0, достатній залишок) реалізовано МЕТОДАМИ
      сутності в Лаб. 03 (RemoveStock / AddStock), а не відкритими setter-ами.
      Movies.Id — ВЛАСНИК ідентифікатора фільму в системі: саме цей Id
      копіюється в VideoRentalRentalsDb.Movies та в reviews.movieId (MongoDB).
   ============================================================================= */
CREATE TABLE dbo.Movies
(
    Id              BIGINT        IDENTITY(1,1) CONSTRAINT PK_Movies PRIMARY KEY,
    Title           NVARCHAR(300) NOT NULL,
    Barcode            NVARCHAR(20)  NOT NULL CONSTRAINT UQ_Movies_Barcode UNIQUE,  -- унікальний бізнес-ключ
    DailyRate        DECIMAL(18,2) NOT NULL CONSTRAINT CK_Movies_DailyRate CHECK (DailyRate > 0),
    AvailableDiscs INT           NOT NULL CONSTRAINT DF_Movies_AvailableDiscs DEFAULT 0
                                  CONSTRAINT CK_Movies_AvailableDiscs CHECK (AvailableDiscs >= 0),
    ReleaseYear   INT           NULL CONSTRAINT CK_Movies_ReleaseYear CHECK (ReleaseYear IS NULL OR ReleaseYear BETWEEN 1400 AND 2100),
    DirectorId        INT           NOT NULL
                    CONSTRAINT FK_Movies_Directors REFERENCES dbo.Directors (Id),  -- 1:N, БЕЗ каскаду:
                                                                              -- режисера з фільмами не видаляємо
    CreatedAt       DATETIME2(3)  NOT NULL CONSTRAINT DF_Movies_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt       DATETIME2(3)  NULL,
    IsDeleted       BIT           NOT NULL CONSTRAINT DF_Movies_IsDeleted DEFAULT 0,
    RowVersion      ROWVERSION                                                -- оптимістична конкурентність
);
GO

/* індекс під запит: «пошук фільмів за назвою з фільтром за тарифом» (список API з пагінацією) */
CREATE INDEX IX_Movies_Title ON dbo.Movies (Title) INCLUDE (DailyRate, AvailableDiscs, DirectorId);
GO
/* індекс під запит: «усі фільми режисера» */
CREATE INDEX IX_Movies_DirectorId ON dbo.Movies (DirectorId);
GO
/* фільтрований індекс під запит: «наявні в каталозі фільму» */
CREATE INDEX IX_Movies_Active ON dbo.Movies (Title) WHERE IsDeleted = 0;
GO

/* =============================================================================
   4. MovieDetails — 1:1 до Movies. PK є водночас FK.
      Каскадне видалення доречне: деталі не існують без фільму.
   ============================================================================= */
CREATE TABLE dbo.MovieDetails
(
    MovieId      BIGINT         NOT NULL
                CONSTRAINT PK_MovieDetails PRIMARY KEY
                CONSTRAINT FK_MovieDetails_Movies REFERENCES dbo.Movies (Id) ON DELETE CASCADE,
    Description NVARCHAR(2000) NOT NULL,
    Studio   NVARCHAR(200)  NULL,
    DurationMinutes   INT            NULL CONSTRAINT CK_MovieDetails_DurationMinutes CHECK (DurationMinutes IS NULL OR DurationMinutes > 0),
    Language    NVARCHAR(50)   NOT NULL CONSTRAINT DF_MovieDetails_Language DEFAULT N'uk',
    CoverUrl    NVARCHAR(500)  NULL
);
GO

/* =============================================================================
   5. DiscDiscs — 1:N до Movies: фізичні диски на полицях.
      Каскадне видалення доречне: диск не існує без фільму.
   ============================================================================= */
CREATE TABLE dbo.DiscDiscs
(
    Id              BIGINT       IDENTITY(1,1) CONSTRAINT PK_DiscDiscs PRIMARY KEY,
    MovieId          BIGINT       NOT NULL
                    CONSTRAINT FK_DiscDiscs_Movies REFERENCES dbo.Movies (Id) ON DELETE CASCADE,
    DiscNumber NVARCHAR(40) NOT NULL CONSTRAINT UQ_DiscDiscs_DiscNumber UNIQUE,
    Shelf           NVARCHAR(40) NULL,
    [Condition]     NVARCHAR(20) NOT NULL CONSTRAINT DF_DiscDiscs_Condition DEFAULT N'Good'
                    CONSTRAINT CK_DiscDiscs_Condition CHECK ([Condition] IN (N'New', N'Good', N'Worn', N'Damaged', N'Lost')),
    AcquiredAt      DATE         NULL
);
GO

/* індекс під запит: «усі диски фільму з їх станом» */
CREATE INDEX IX_DiscDiscs_MovieId ON dbo.DiscDiscs (MovieId) INCLUDE ([Condition], Shelf);
GO

/* =============================================================================
   6. MovieGenres — M:N між Movies і Genres ЧЕРЕЗ ПРОМІЖНУ СУТНІСТЬ
      З ВЛАСНИМ ПОЛЕМ AddedAt. Автоматичний M:N в EF Core такого поля
      зберегти не дозволив би — саме тому сутність описана явно.
   ============================================================================= */
CREATE TABLE dbo.MovieGenres
(
    MovieId   BIGINT       NOT NULL
             CONSTRAINT FK_MovieGenres_Movies  REFERENCES dbo.Movies  (Id) ON DELETE CASCADE,
    GenreId  INT          NOT NULL
             CONSTRAINT FK_MovieGenres_Genres REFERENCES dbo.Genres (Id),
    AddedAt  DATETIME2(3) NOT NULL CONSTRAINT DF_MovieGenres_AddedAt DEFAULT SYSUTCDATETIME(),  -- власне поле M:N
    AddedBy  NVARCHAR(100) NULL,
    IsPrimary BIT          NOT NULL CONSTRAINT DF_MovieGenres_IsPrimary DEFAULT 0,              -- головний жанр фільму

    CONSTRAINT PK_MovieGenres PRIMARY KEY (MovieId, GenreId)      -- складений ключ
);
GO

/* індекс під запит: «нові фільми жанру за період» (LINQ-запит через проміжну сутність, Лаб. 03) */
CREATE INDEX IX_MovieGenres_GenreId_AddedAt ON dbo.MovieGenres (GenreId, AddedAt DESC) INCLUDE (MovieId);
GO

PRINT N'VideoRentalCatalogDb: schema.sql виконано успішно.';
GO

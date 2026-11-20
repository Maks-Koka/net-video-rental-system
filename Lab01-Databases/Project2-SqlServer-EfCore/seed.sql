/* =============================================================================
   ПРОЄКТ №2 — VideoRentalCatalogDb : початкові довідникові дані
   -----------------------------------------------------------------------------
   Ці ж дані в Лаб. 03 подаються двома способами:
     - статичний сідінг  — HasData у конфігураціях (Directors, Genres: фіксовані Id);
     - програмний сідінг — DbInitializer.SeedAsync після MigrateAsync (Movies та
                           залежні записи).
   ІДЕМПОТЕНТНІСТЬ: усі вставки захищені перевіркою наявності.
   Порядок дотримує цілісність FK: спочатку довідники, потім залежні записи.
   ============================================================================= */

USE VideoRentalCatalogDb;
GO
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ---------------------------------------------------------------------------
   1. Directors — довідник із фіксованими Id (статичний сідінг HasData)
   --------------------------------------------------------------------------- */
SET IDENTITY_INSERT dbo.Directors ON;

INSERT INTO dbo.Directors (Id, FullName, Country, BirthYear)
SELECT v.Id, v.FullName, v.Country, v.BirthYear
FROM (VALUES
        (1, N'Сергій Параджанов',    N'Вірменія', 1924),
        (2, N'Олександр Довженко',   N'Україна',  1894),
        (3, N'Борис Івченко',        N'Україна',  1941),
        (4, N'Олесь Санін',          N'Україна',  1972),
        (5, N'Ахтем Сеітаблаєв',     N'Україна',  1972),
        (6, N'Антоніо Лукіч',        N'Україна',  1993)
     ) AS v (Id, FullName, Country, BirthYear)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Directors a WHERE a.Id = v.Id);

SET IDENTITY_INSERT dbo.Directors OFF;
GO

/* ---------------------------------------------------------------------------
   2. Genres — довідник із фіксованими Id (статичний сідінг HasData)
   --------------------------------------------------------------------------- */
SET IDENTITY_INSERT dbo.Genres ON;

INSERT INTO dbo.Genres (Id, Name, Slug, Description)
SELECT v.Id, v.Name, v.Slug, v.Description
FROM (VALUES
        (1, N'Комедія',            N'comedy',      N'Комедійні фільми'),
        (2, N'Історичний',  N'historical',  N'Фільми на історичному матеріалі'),
        (3, N'Драма',     N'drama',      N'Драматичні фільми'),
        (4, N'Класика',           N'classic',     N'Класика українського кіно'),
        (5, N'Документальний',      N'documentary',  N'Документальне кіно')
     ) AS v (Id, Name, Slug, Description)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Genres g WHERE g.Id = v.Id);

SET IDENTITY_INSERT dbo.Genres OFF;
GO

/* ---------------------------------------------------------------------------
   3. Movies — залежні записи (програмний сідінг у Лаб. 03).
      Id фіксуємо явно, бо саме ці Id дублюються в VideoRentalRentalsDb.Movies
      та в reviews.movieId (MongoDB) — див. таблицю політики дублювання.
   --------------------------------------------------------------------------- */
SET IDENTITY_INSERT dbo.Movies ON;

INSERT INTO dbo.Movies (Id, Title, Barcode, DailyRate, AvailableDiscs, ReleaseYear, DirectorId)
SELECT v.Id, v.Title, v.Barcode, v.DailyRate, v.AvailableDiscs, v.ReleaseYear, v.DirectorId
FROM (VALUES
        (1, N'Тіні забутих предків',                              N'482-001-00-0001-5', CAST(3.50 AS DECIMAL(18,2)), 5, 1965, 1),
        (2, N'Земля',                           N'482-001-00-0002-3', CAST(4.00 AS DECIMAL(18,2)), 3, 1930, 2),
        (3, N'Пропала грамота',                               N'482-001-00-0003-1', CAST(4.25 AS DECIMAL(18,2)), 4, 1972, 3),
        (4, N'Поводир',                            N'482-001-00-0004-9', CAST(5.00 AS DECIMAL(18,2)), 2, 2014, 4),
        (5, N'Кіборги',                        N'482-001-00-0005-7', CAST(4.75 AS DECIMAL(18,2)), 6, 2017, 5),
        (6, N'Мої думки тихі',    N'482-001-00-0006-5', CAST(5.50 AS DECIMAL(18,2)), 1, 2019, 6)
     ) AS v (Id, Title, Barcode, DailyRate, AvailableDiscs, ReleaseYear, DirectorId)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Movies b WHERE b.Id = v.Id);

SET IDENTITY_INSERT dbo.Movies OFF;
GO

/* ---------------------------------------------------------------------------
   4. MovieDetails — 1:1
   --------------------------------------------------------------------------- */
INSERT INTO dbo.MovieDetails (MovieId, Description, Studio, DurationMinutes, Language)
SELECT v.MovieId, v.Description, v.Studio, v.DurationMinutes, N'uk'
FROM (VALUES
        (1, N'Екранізація повісті Коцюбинського — вершина українського поетичного кіно.', N'Кіностудія Довженка',   368),
        (2, N'Німа класика Довженка про українське село на зламі епох.',             N'Київнаукфільм', 304),
        (3, N'Музична комедія за мотивами творів Гоголя про козака-характерника.',            N'Довженко-Центр',   288),
        (4, N'Історична драма про сліпого кобзаря та хлопчика в Україні 1930-х.',          N'Pronto Film', 336),
        (5, N'Воєнна драма про оборону Донецького аеропорту.',             N'Idas Film', 432),
        (6, N'Road-movie про пошук рідкісної качки в Закарпатті.',                       N'ToyCinema',   416)
     ) AS v (MovieId, Description, Studio, DurationMinutes)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MovieDetails d WHERE d.MovieId = v.MovieId);
GO

/* ---------------------------------------------------------------------------
   5. DiscDiscs — 1:N (фізичні диски)
   --------------------------------------------------------------------------- */
INSERT INTO dbo.DiscDiscs (MovieId, DiscNumber, Shelf, [Condition], AcquiredAt)
SELECT v.MovieId, v.DiscNumber, v.Shelf, v.Cond, v.AcquiredAt
FROM (VALUES
        (1, N'DISC-0001', N'A-01', N'Good',    CAST('2023-02-10' AS DATE)),
        (1, N'DISC-0002', N'A-01', N'New',     CAST('2025-05-04' AS DATE)),
        (2, N'DISC-0003', N'A-02', N'Good',    CAST('2022-11-20' AS DATE)),
        (3, N'DISC-0004', N'B-01', N'Worn',    CAST('2019-09-01' AS DATE)),
        (4, N'DISC-0005', N'B-02', N'New',     CAST('2026-01-15' AS DATE)),
        (5, N'DISC-0006', N'C-01', N'Good',    CAST('2021-06-30' AS DATE)),
        (6, N'DISC-0007', N'C-02', N'Good',    CAST('2024-03-12' AS DATE))
     ) AS v (MovieId, DiscNumber, Shelf, Cond, AcquiredAt)
WHERE NOT EXISTS (SELECT 1 FROM dbo.DiscDiscs c WHERE c.DiscNumber = v.DiscNumber);
GO

/* ---------------------------------------------------------------------------
   6. MovieGenres — M:N із власним полем AddedAt
   --------------------------------------------------------------------------- */
INSERT INTO dbo.MovieGenres (MovieId, GenreId, AddedAt, AddedBy, IsPrimary)
SELECT v.MovieId, v.GenreId, v.AddedAt, N'seed', v.IsPrimary
FROM (VALUES
        (1, 1, CAST('2026-09-01T10:00:00' AS DATETIME2(3)), 1),
        (1, 4, CAST('2026-09-01T10:00:00' AS DATETIME2(3)), 0),
        (2, 2, CAST('2026-09-05T10:00:00' AS DATETIME2(3)), 1),
        (2, 4, CAST('2026-09-05T10:00:00' AS DATETIME2(3)), 0),
        (3, 3, CAST('2026-10-01T10:00:00' AS DATETIME2(3)), 1),
        (3, 4, CAST('2026-10-01T10:00:00' AS DATETIME2(3)), 0),
        (4, 3, CAST('2026-11-01T10:00:00' AS DATETIME2(3)), 1),
        (5, 2, CAST('2026-11-10T10:00:00' AS DATETIME2(3)), 1),
        (5, 3, CAST('2026-11-10T10:00:00' AS DATETIME2(3)), 0),
        (6, 5, CAST('2026-11-15T10:00:00' AS DATETIME2(3)), 1)
     ) AS v (MovieId, GenreId, AddedAt, IsPrimary)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MovieGenres bg
                  WHERE bg.MovieId = v.MovieId AND bg.GenreId = v.GenreId);
GO

PRINT N'VideoRentalCatalogDb: seed.sql виконано успішно (повторний запуск дублікатів не створює).';
GO

SELECT 'Directors' AS [Table], COUNT(*) AS [Rows] FROM dbo.Directors
UNION ALL SELECT 'Genres',      COUNT(*) FROM dbo.Genres
UNION ALL SELECT 'Movies',       COUNT(*) FROM dbo.Movies
UNION ALL SELECT 'MovieDetails', COUNT(*) FROM dbo.MovieDetails
UNION ALL SELECT 'DiscDiscs',  COUNT(*) FROM dbo.DiscDiscs
UNION ALL SELECT 'MovieGenres',  COUNT(*) FROM dbo.MovieGenres;
GO

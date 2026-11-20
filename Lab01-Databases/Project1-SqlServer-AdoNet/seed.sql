/* =============================================================================
   ПРОЄКТ №1 — VideoRentalRentalsDb : тестові дані
   -----------------------------------------------------------------------------
   ІДЕМПОТЕНТНІСТЬ: кожна вставка захищена перевіркою наявності (IF NOT EXISTS /
   MERGE-подібний патерн), тому ПОВТОРНИЙ запуск цього скрипта НЕ створює
   дублікатів. Перевірка — у verify.sql (блок «ідемпотентність»).
   ============================================================================= */

USE VideoRentalRentalsDb;
GO

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ---------------------------------------------------------------------------
   1. Клієнти (власні дані контексту)
   --------------------------------------------------------------------------- */
INSERT INTO dbo.Customers (MembershipNumber, Email, FullName, Phone, CreatedBy)
SELECT v.MembershipNumber, v.Email, v.FullName, v.Phone, 'seed'
FROM (VALUES
        (N'VR-000001', N'olena.k@example.com',  N'Олена Коваленко', N'+380501112233'),
        (N'VR-000002', N'andrii.sh@example.com', N'Андрій Ткаченко', N'+380502223344'),
        (N'VR-000003', N'maria.b@example.com',   N'Марія Бондаренко', N'+380503334455'),
        (N'VR-000004', N'obrien@example.com',    N'Liam O''Brien',    N'+380504445566')  -- спецсимвол: перевірка проти SQL-ін'єкцій
     ) AS v (MembershipNumber, Email, FullName, Phone)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Customers r WHERE r.MembershipNumber = v.MembershipNumber);
GO

/* ---------------------------------------------------------------------------
   2. Локальна копія довідника фільмів (Id — ті самі, що в VideoRentalCatalogDb)
      Джерело даних: Movie у Проєкті №2. Оновлюється подіями з Каталогу.
   --------------------------------------------------------------------------- */
MERGE dbo.Movies AS target
USING (VALUES
        (1, N'Тіні забутих предків',                      N'Сергій Параджанов',     N'482-001-00-0001-5', CAST(3.50 AS DECIMAL(18,2)), 5),
        (2, N'Земля',                   N'Олександр Довженко',      N'482-001-00-0002-3', CAST(4.00 AS DECIMAL(18,2)), 3),
        (3, N'Пропала грамота',                       N'Борис Івченко', N'482-001-00-0003-1', CAST(4.25 AS DECIMAL(18,2)), 4),
        (4, N'Поводир',                    N'Олесь Санін',       N'482-001-00-0004-9', CAST(5.00 AS DECIMAL(18,2)), 2),
        (5, N'Кіборги',                N'Ахтем Сеітаблаєв',       N'482-001-00-0005-7', CAST(4.75 AS DECIMAL(18,2)), 6),
        (6, N'Мої думки тихі', N'Антоніо Лукіч',  N'482-001-00-0006-5', CAST(5.50 AS DECIMAL(18,2)), 1)
      ) AS src (Id, Title, Director, Barcode, DailyRate, DiscsAvailable)
ON target.Id = src.Id
WHEN MATCHED THEN
    UPDATE SET Title = src.Title, Director = src.Director, Barcode = src.Barcode,
               DailyRate = src.DailyRate, SyncedAt = SYSUTCDATETIME()
               -- DiscsAvailable НЕ перетираємо: ним керують оренди/повернення
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Title, Director, Barcode, DailyRate, DiscsAvailable)
    VALUES (src.Id, src.Title, src.Director, src.Barcode, src.DailyRate, src.DiscsAvailable);
GO

/* ---------------------------------------------------------------------------
   3. Сценарні оренди — створюємо через процедуру, щоб відпрацювали
      знімки полів каталогу та журнал статусів.
      Ідемпотентність: вставляємо лише якщо в клієнта ще немає жодної оренди.
   --------------------------------------------------------------------------- */
DECLARE @CustomerOlena  BIGINT = (SELECT Id FROM dbo.Customers WHERE MembershipNumber = N'VR-000001');
DECLARE @CustomerAndrii BIGINT = (SELECT Id FROM dbo.Customers WHERE MembershipNumber = N'VR-000002');
DECLARE @CustomerMaria  BIGINT = (SELECT Id FROM dbo.Customers WHERE MembershipNumber = N'VR-000003');
DECLARE @RentalId BIGINT, @LateFee DECIMAL(18,2);

/* --- Сценарій A: оренда у статусі Submitted (нова) --- */
IF NOT EXISTS (SELECT 1 FROM dbo.Rentals WHERE CustomerId = @CustomerOlena)
BEGIN
    EXEC dbo.usp_CreateRental
        @CustomerId     = @CustomerOlena,
        @DueDate      = '2026-12-20',
        @PickupStore = N'Центральний магазин',
        @Comment      = N'Перша оренда клієнта',
        @ItemsJson    = N'[{"MovieId":1,"Units":1},{"MovieId":2,"Units":1}]',
        @CreatedBy    = N'seed',
        @RentalId       = @RentalId OUTPUT;
END

/* --- Сценарій B: оренда на руках (PickedUp), ТЕРМІН ЯКОЇ ВЖЕ ВИЙШОВ.
       RentalDate задаємо явно в минулому, щоб DueDate < сьогодні не порушив
       обмеження CK_Rentals_DueAfterRental (DueDate >= RentalDate).              --- */
IF NOT EXISTS (SELECT 1 FROM dbo.Rentals WHERE CustomerId = @CustomerAndrii)
BEGIN
    EXEC dbo.usp_CreateRental
        @CustomerId     = @CustomerAndrii,
        @RentalDate     = '2026-10-20T10:00:00',
        @DueDate      = '2026-11-10',             -- термін вийшов -> прострочена
        @PickupStore = N'Магазин №2',
        @Comment      = N'Оренда для демонстрації пені',
        @ItemsJson    = N'[{"MovieId":5,"Units":2}]',
        @CreatedBy    = N'seed',
        @RentalId       = @RentalId OUTPUT;

    EXEC dbo.usp_ConfirmRentalDiscs @RentalId = @RentalId, @ChangedBy = N'seed';
    EXEC dbo.usp_PickUpRental        @RentalId = @RentalId, @ChangedBy = N'seed';
END

/* --- Сценарій C: повний цикл до Returned з нарахованою пенею
       (повернуто 2026-11-08 при терміні 2026-11-01 => 7 днів прострочення) --- */
IF NOT EXISTS (SELECT 1 FROM dbo.Rentals WHERE CustomerId = @CustomerMaria)
BEGIN
    EXEC dbo.usp_CreateRental
        @CustomerId     = @CustomerMaria,
        @RentalDate     = '2026-10-15T09:30:00',
        @DueDate      = '2026-11-01',
        @PickupStore = N'Центральний магазин',
        @Comment      = N'Завершений цикл оренди',
        @ItemsJson    = N'[{"MovieId":3,"Units":1},{"MovieId":4,"Units":1}]',
        @CreatedBy    = N'seed',
        @RentalId       = @RentalId OUTPUT;

    EXEC dbo.usp_ConfirmRentalDiscs @RentalId = @RentalId, @ChangedBy = N'seed';
    EXEC dbo.usp_PickUpRental        @RentalId = @RentalId, @ChangedBy = N'seed';
    EXEC dbo.usp_ReturnRental       @RentalId = @RentalId,
                                  @ReturnDate = '2026-11-08',
                                  @ChangedBy  = N'seed',
                                  @LateFee       = @LateFee OUTPUT;

    /* оплата нарахованої пені — з idempotency-ключем */
    IF @LateFee > 0 AND NOT EXISTS (SELECT 1 FROM dbo.LateFeePayments WHERE IdempotencyKey = N'seed-latefee-1')
        INSERT INTO dbo.LateFeePayments (RentalId, Amount, Method, IdempotencyKey)
        VALUES (@RentalId, @LateFee, N'Card', N'seed-latefee-1');
END
GO

PRINT N'VideoRentalRentalsDb: seed.sql виконано успішно (повторний запуск дублікатів не створює).';
GO

SELECT 'Customers' AS [Table], COUNT(*) AS [Rows] FROM dbo.Customers
UNION ALL SELECT 'Movies',             COUNT(*) FROM dbo.Movies
UNION ALL SELECT 'Rentals',             COUNT(*) FROM dbo.Rentals
UNION ALL SELECT 'RentalDetails',       COUNT(*) FROM dbo.RentalDetails
UNION ALL SELECT 'RentalItems',         COUNT(*) FROM dbo.RentalItems
UNION ALL SELECT 'RentalStatusHistory', COUNT(*) FROM dbo.RentalStatusHistory
UNION ALL SELECT 'LateFeePayments',      COUNT(*) FROM dbo.LateFeePayments;
GO

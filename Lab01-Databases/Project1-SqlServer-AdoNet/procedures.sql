/* =============================================================================
   ПРОЄКТ №1 — VideoRentalRentalsDb : збережувані процедури
   -----------------------------------------------------------------------------
   7 процедур: CRUD + бізнес-операції з транзакційною логікою.
   Коди помилок (їх перехоплює DAL у Лаб. 02 і перетворює на HTTP-статуси):
     50001 — сутність не знайдено            -> NotFoundException      -> 404
     50002 — недопустимий перехід статусу    -> BusinessConflictException -> 409
     50003 — недостатньо дисків         -> BusinessConflictException -> 409
     50004 — порушення бізнес-правила        -> BusinessConflictException -> 409
   -----------------------------------------------------------------------------
   Допустимі переходи статусів:
     Submitted -> AwaitingValidation -> DiscsConfirmed -> PickedUp -> Returned
     Cancelled — лише з Submitted / AwaitingValidation / DiscsConfirmed
   ============================================================================= */

USE VideoRentalRentalsDb;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* =============================================================================
   1. usp_CreateRental — створення оренди разом із позиціями в ОДНІЙ транзакції.
      Назва, режисер і тариф фільму беруться з dbo.Movies і записуються в позицію
      як ЗНІМОК — за таблицею політики дублювання (Лаб. 01, Крок 2).
      @ItemsJson: [{"MovieId":1,"Units":2},{"MovieId":5,"Units":1}]
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_CreateRental
    @CustomerId     BIGINT,
    @DueDate      DATE,
    @PickupStore NVARCHAR(100),
    @Comment      NVARCHAR(1000) = NULL,
    @ItemsJson    NVARCHAR(MAX),
    @CreatedBy    NVARCHAR(100)  = NULL,
    @RentalDate     DATETIME2(3)   = NULL,   -- NULL => SYSUTCDATETIME(); задається
                                           -- явно лише для seed / міграції даних
    @RentalId       BIGINT         OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;                 -- будь-яка помилка відкочує транзакцію

    SET @RentalDate = ISNULL(@RentalDate, SYSUTCDATETIME());

    IF @DueDate < CAST(@RentalDate AS DATE)
        THROW 50004, 'DueDate cannot be earlier than RentalDate', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @CustomerId AND IsDeleted = 0)
        THROW 50001, 'Customer not found', 1;

    IF EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @CustomerId AND IsBlocked = 1)
        THROW 50004, 'Customer is blocked and cannot borrow movies', 1;

    IF ISJSON(@ItemsJson) <> 1
        THROW 50004, 'ItemsJson is not a valid JSON array', 1;

    BEGIN TRANSACTION;

    /* розбираємо вхідний JSON у тимчасову таблицю */
    DECLARE @Items TABLE (MovieId BIGINT PRIMARY KEY, Units INT NOT NULL);

    INSERT INTO @Items (MovieId, Units)
    SELECT  j.MovieId, SUM(j.Units)
    FROM    OPENJSON(@ItemsJson)
            WITH (MovieId BIGINT '$.MovieId', Units INT '$.Units') AS j
    GROUP BY j.MovieId;

    IF NOT EXISTS (SELECT 1 FROM @Items)
        THROW 50004, 'Rental must contain at least one item', 1;

    IF EXISTS (SELECT 1 FROM @Items WHERE Units <= 0)
        THROW 50004, 'Units must be positive', 1;

    /* усі фільми мають існувати в локальній копії довідника */
    IF EXISTS (SELECT 1 FROM @Items i
               WHERE NOT EXISTS (SELECT 1 FROM dbo.Movies b WHERE b.Id = i.MovieId))
        THROW 50001, 'One or more movies not found', 1;

    INSERT INTO dbo.Rentals (CustomerId, Status, RentalDate, DueDate, CreatedBy)
    VALUES (@CustomerId, 'Submitted', @RentalDate, @DueDate, @CreatedBy);

    SET @RentalId = CAST(SCOPE_IDENTITY() AS BIGINT);

    INSERT INTO dbo.RentalDetails (RentalId, PickupStore, ClerkName, Comment)
    VALUES (@RentalId, @PickupStore, @CreatedBy, @Comment);

    /* ЗНІМОК полів каталогу на момент оформлення */
    INSERT INTO dbo.RentalItems (RentalId, MovieId, MovieTitle, MovieDirector, DailyRate, Units)
    SELECT  @RentalId, b.Id, b.Title, b.Director, b.DailyRate, i.Units
    FROM    @Items AS i
    INNER JOIN dbo.Movies AS b ON b.Id = i.MovieId;

    INSERT INTO dbo.RentalStatusHistory (RentalId, FromStatus, ToStatus, ChangedBy, Note)
    VALUES (@RentalId, NULL, 'Submitted', @CreatedBy, 'Rental created');

    COMMIT TRANSACTION;
END
GO

/* =============================================================================
   2. usp_ConfirmRentalDiscs — НАЙВАЖЛИВІША транзакційна процедура.
      Перехід Submitted / AwaitingValidation -> DiscsConfirmed.
      Працює в явній транзакції та ВІДМОВЛЯЄ керованою помилкою, якщо
      перехід недопустимий (напр. оренду вже видано на руки або скасовано).
      UPDLOCK блокує рядок до кінця транзакції — два паралельні підтвердження
      не зможуть «тихо» перетерти одне одного.
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_ConfirmRentalDiscs
    @RentalId    BIGINT,
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30) =
        (SELECT Status FROM dbo.Rentals WITH (UPDLOCK) WHERE Id = @RentalId AND IsDeleted = 0);

    IF @Status IS NULL
        THROW 50001, 'Rental not found', 1;

    IF @Status NOT IN ('Submitted', 'AwaitingValidation')
        THROW 50002, 'Discs can be confirmed only for a new rental', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.RentalItems WHERE RentalId = @RentalId)
        THROW 50004, 'Rental has no items', 1;

    /* перевіряємо наявність дисків ДО переходу статусу */
    IF EXISTS (SELECT 1
               FROM   dbo.RentalItems AS i
               INNER JOIN dbo.Movies AS b ON b.Id = i.MovieId
               WHERE  i.RentalId = @RentalId AND b.DiscsAvailable < i.Units)
        THROW 50003, 'Not enough discs available for one or more movies', 1;

    UPDATE dbo.Rentals
    SET    Status    = 'DiscsConfirmed',
           UpdatedAt = SYSUTCDATETIME(),
           UpdatedBy = @ChangedBy
    WHERE  Id = @RentalId;

    INSERT INTO dbo.RentalStatusHistory (RentalId, FromStatus, ToStatus, ChangedBy, Note)
    VALUES (@RentalId, @Status, 'DiscsConfirmed', @ChangedBy, 'Discs availability confirmed');

    COMMIT TRANSACTION;
END
GO

/* =============================================================================
   3. usp_PickUpRental — оренда фільмів на руки: DiscsConfirmed -> PickedUp.
      Зменшує залишок дисків у ЛОКАЛЬНІЙ копії довідника в тій самій
      транзакції, що й зміна статусу: або обидві дії, або жодна.
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_PickUpRental
    @RentalId    BIGINT,
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30) =
        (SELECT Status FROM dbo.Rentals WITH (UPDLOCK) WHERE Id = @RentalId AND IsDeleted = 0);

    IF @Status IS NULL
        THROW 50001, 'Rental not found', 1;

    IF @Status <> 'DiscsConfirmed'
        THROW 50002, 'Only a rental with confirmed discs can be pickedUp', 1;

    /* блокуємо рядки фільмів у детермінованому порядку (MovieId) — проти deadlock */
    DECLARE @Shortage INT =
    (
        SELECT COUNT(*)
        FROM   dbo.RentalItems AS i
        INNER JOIN dbo.Movies AS b WITH (UPDLOCK) ON b.Id = i.MovieId
        WHERE  i.RentalId = @RentalId AND b.DiscsAvailable < i.Units
    );

    IF @Shortage > 0
        THROW 50003, 'Not enough discs available to issue the rental', 1;

    UPDATE  b
    SET     b.DiscsAvailable = b.DiscsAvailable - i.Units
    FROM    dbo.Movies    AS b
    INNER JOIN dbo.RentalItems AS i ON i.MovieId = b.Id
    WHERE   i.RentalId = @RentalId;

    UPDATE dbo.Rentals
    SET    Status    = 'PickedUp',
           PickedUpAt  = SYSUTCDATETIME(),
           UpdatedAt = SYSUTCDATETIME(),
           UpdatedBy = @ChangedBy
    WHERE  Id = @RentalId;

    INSERT INTO dbo.RentalStatusHistory (RentalId, FromStatus, ToStatus, ChangedBy, Note)
    VALUES (@RentalId, @Status, 'PickedUp', @ChangedBy, 'Movies handed over to the customer');

    COMMIT TRANSACTION;
END
GO

/* =============================================================================
   4. usp_ReturnRental — повернення: PickedUp -> Returned.
      Повертає диски в довідник, рахує пеню за прострочені дні
      (тариф DailyRate — знімок із позиції оренди) — усе в одній транзакції.
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_ReturnRental
    @RentalId     BIGINT,
    @ReturnDate DATETIME2(3)  = NULL,
    @ChangedBy  NVARCHAR(100) = NULL,
    @LateFee       DECIMAL(18,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ReturnDate = ISNULL(@ReturnDate, SYSUTCDATETIME());

    BEGIN TRANSACTION;

    DECLARE @Status  NVARCHAR(30),
            @DueDate DATE;

    SELECT @Status = Status, @DueDate = DueDate
    FROM   dbo.Rentals WITH (UPDLOCK)
    WHERE  Id = @RentalId AND IsDeleted = 0;

    IF @Status IS NULL
        THROW 50001, 'Rental not found', 1;

    IF @Status <> 'PickedUp'
        THROW 50002, 'Only an pickedUp rental can be returned', 1;

    /* повертаємо диски */
    UPDATE  b
    SET     b.DiscsAvailable = b.DiscsAvailable + i.Units
    FROM    dbo.Movies    AS b
    INNER JOIN dbo.RentalItems AS i ON i.MovieId = b.Id
    WHERE   i.RentalId = @RentalId;

    /* пеня = прострочені дні * сума денних тарифів позицій */
    DECLARE @OverdueDays INT = CASE
        WHEN CAST(@ReturnDate AS DATE) > @DueDate
        THEN DATEDIFF(DAY, @DueDate, CAST(@ReturnDate AS DATE))
        ELSE 0 END;

    DECLARE @DailyTotal DECIMAL(18,2) =
        ISNULL((SELECT SUM(i.DailyRate * i.Units - i.Discount)
                FROM dbo.RentalItems AS i WHERE i.RentalId = @RentalId), 0);

    SET @LateFee = CAST(@OverdueDays * @DailyTotal AS DECIMAL(18,2));

    UPDATE dbo.Rentals
    SET    Status     = 'Returned',
           ReturnedAt = @ReturnDate,
           LateFeeAmount = @LateFee,
           UpdatedAt  = SYSUTCDATETIME(),
           UpdatedBy  = @ChangedBy
    WHERE  Id = @RentalId;

    INSERT INTO dbo.RentalStatusHistory (RentalId, FromStatus, ToStatus, ChangedBy, Note)
    VALUES (@RentalId, @Status, 'Returned', @ChangedBy,
            CONCAT('Returned; overdue days: ', @OverdueDays, '; fine: ', @LateFee));

    COMMIT TRANSACTION;
END
GO

/* =============================================================================
   5. usp_CancelRental — скасування. Допустиме ЛИШЕ до оренди дисків.
      Демонструє відмову на недопустимому переході керованою помилкою.
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_CancelRental
    @RentalId    BIGINT,
    @Reason    NVARCHAR(400)  = NULL,
    @ChangedBy NVARCHAR(100)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30) =
        (SELECT Status FROM dbo.Rentals WITH (UPDLOCK) WHERE Id = @RentalId AND IsDeleted = 0);

    IF @Status IS NULL
        THROW 50001, 'Rental not found', 1;

    IF @Status NOT IN ('Submitted', 'AwaitingValidation', 'DiscsConfirmed')
        THROW 50002, 'A rental can be cancelled only before the movies are pickedUp', 1;

    UPDATE dbo.Rentals
    SET    Status    = 'Cancelled',
           UpdatedAt = SYSUTCDATETIME(),
           UpdatedBy = @ChangedBy
    WHERE  Id = @RentalId;

    INSERT INTO dbo.RentalStatusHistory (RentalId, FromStatus, ToStatus, ChangedBy, Note)
    VALUES (@RentalId, @Status, 'Cancelled', @ChangedBy, ISNULL(@Reason, 'Cancelled by librarian'));

    COMMIT TRANSACTION;
END
GO

/* =============================================================================
   6. usp_DecreaseMovieDiscs — точкове зменшення залишку з перевіркою.
      Використовується BLL у Лаб. 02 як ДРУГИЙ репозиторій транзакційного
      сценарію: штучна помилка тут відкочує зміну статусу оренди.
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_DecreaseMovieDiscs
    @MovieId   BIGINT,
    @Quantity INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @Quantity <= 0
        THROW 50004, 'Quantity must be positive', 1;

    DECLARE @Available INT =
        (SELECT DiscsAvailable FROM dbo.Movies WITH (UPDLOCK) WHERE Id = @MovieId);

    IF @Available IS NULL
        THROW 50001, 'Movie not found', 1;

    IF @Available < @Quantity
        THROW 50003, 'Not enough discs available', 1;

    UPDATE dbo.Movies
    SET    DiscsAvailable = DiscsAvailable - @Quantity
    WHERE  Id = @MovieId;
END
GO

/* =============================================================================
   7. usp_GetCustomerRentalSummary — читання/звіт: оренди клієнта з агрегатами.
      Приклад CRUD-процедури (Read) для DAL на Dapper.
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_GetCustomerRentalSummary
    @CustomerId BIGINT,
    @Status   NVARCHAR(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @CustomerId AND IsDeleted = 0)
        THROW 50001, 'Customer not found', 1;

    SELECT  RentalId, Status, RentalDate, DueDate, ReturnedAt, LateFeeAmount,
            CustomerId, CustomerName, MembershipNumber, PickupStore,
            TitlesCount, UnitsTotal, DailyChargeTotal
    FROM    dbo.vw_RentalSummary
    WHERE   CustomerId = @CustomerId
      AND  (@Status IS NULL OR Status = @Status)
    ORDER BY RentalDate DESC;
END
GO

PRINT N'VideoRentalRentalsDb: procedures.sql виконано успішно (7 процедур).';
GO

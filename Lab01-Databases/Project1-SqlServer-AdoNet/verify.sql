/* =============================================================================
   ПРОЄКТ №1 — VideoRentalRentalsDb : перевірка критеріїв приймання Лаб. 01
   -----------------------------------------------------------------------------
   Виконується ПІСЛЯ schema.sql -> procedures.sql -> seed.sql.
   Кожен блок відповідає пункту розділу «Перед здачею перевірте себе».
   ============================================================================= */

USE VideoRentalRentalsDb;
GO
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

PRINT N'=============================================================';
PRINT N' 1. Жодного FOREIGN KEY за межі цієї бази (database-per-service)';
PRINT N'=============================================================';
GO
SELECT  fk.name                                   AS ForeignKey,
        OBJECT_SCHEMA_NAME(fk.parent_object_id)   AS ParentSchema,
        OBJECT_NAME(fk.parent_object_id)          AS ParentTable,
        OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS RefSchema,
        OBJECT_NAME(fk.referenced_object_id)      AS ReferencedTable
FROM    sys.foreign_keys AS fk
ORDER BY ParentTable, ForeignKey;
GO
PRINT N'>> Очікувано: усі посилання — лише в межах VideoRentalRentalsDb.';
PRINT N'>> dbo.Movies.Id посилається на Каталог ЛОГІЧНО, без FK (його тут немає).';
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 2. Три типи зв''язків: 1:1, 1:N, M:N';
PRINT N'=============================================================';
GO
SELECT N'1:1  Rentals <-> RentalDetails' AS Relationship,
       N'PK таблиці RentalDetails є водночас FK на Rentals' AS Implementation,
       (SELECT COUNT(*) FROM dbo.RentalDetails) AS [Rows]
UNION ALL
SELECT N'1:N  Customers -> Rentals',
       N'FK Rentals.CustomerId на боці «багатьох»',
       (SELECT COUNT(*) FROM dbo.Rentals)
UNION ALL
SELECT N'M:N  Rentals <-> Movies',
       N'проміжна таблиця RentalItems зі складеним PK (RentalId, MovieId)',
       (SELECT COUNT(*) FROM dbo.RentalItems);
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 3. Обмеження: PK / FK / UNIQUE / CHECK / DEFAULT';
PRINT N'=============================================================';
GO
SELECT type_desc AS ConstraintKind, COUNT(*) AS Total
FROM   sys.objects
WHERE  type_desc IN ('PRIMARY_KEY_CONSTRAINT', N'FOREIGN_KEY_CONSTRAINT',
                     'UNIQUE_CONSTRAINT', N'CHECK_CONSTRAINT', N'DEFAULT_CONSTRAINT')
GROUP BY type_desc
ORDER BY type_desc;
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 4. Індекси та запити, під які вони створені';
PRINT N'=============================================================';
GO
SELECT  OBJECT_NAME(i.object_id) AS TableName,
        i.name                   AS IndexName,
        i.type_desc              AS IndexType,
        i.is_unique              AS IsUnique,
        i.has_filter             AS IsFiltered,
        i.filter_definition      AS FilterDefinition
FROM    sys.indexes AS i
WHERE   i.object_id IN (OBJECT_ID('dbo.Customers'), OBJECT_ID('dbo.Movies'),
                        OBJECT_ID('dbo.Rentals'),   OBJECT_ID('dbo.RentalDetails'),
                        OBJECT_ID('dbo.RentalItems'), OBJECT_ID('dbo.RentalStatusHistory'),
                        OBJECT_ID('dbo.LateFeePayments'))
  AND   i.name IS NOT NULL
ORDER BY TableName, IndexName;
GO
PRINT N'>> Призначення індексів задокументовано коментарями в schema.sql.';
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 5. Збережувані процедури (очікувано 7)';
PRINT N'=============================================================';
GO
SELECT  p.name AS Procedure_Name,
        (SELECT COUNT(*) FROM sys.parameters AS pr WHERE pr.object_id = p.object_id) AS Params
FROM    sys.procedures AS p
ORDER BY p.name;
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 6. НЕДОПУСТИМИЙ ПЕРЕХІД СТАТУСУ -> керована помилка, а не дія';
PRINT N'=============================================================';
GO
/* Беремо оренду, яка вже у статусі PickedUp, і намагаємося «підтвердити» її
   повторно. Процедура має кинути помилку 50002 і НЕ змінити стан.          */
DECLARE @PickedUpRental BIGINT = (SELECT TOP 1 Id FROM dbo.Rentals WHERE Status = 'PickedUp' ORDER BY Id);
DECLARE @StatusBefore NVARCHAR(30) = (SELECT Status FROM dbo.Rentals WHERE Id = @PickedUpRental);

PRINT CONCAT(N'Оренда Id = ', @PickedUpRental, N', статус до виклику: ', @StatusBefore);

BEGIN TRY
    EXEC dbo.usp_ConfirmRentalDiscs @RentalId = @PickedUpRental, @ChangedBy = N'verify';
    PRINT N'!!! ПОМИЛКА ТЕСТУ: процедура виконалася, хоча перехід недопустимий';
END TRY
BEGIN CATCH
    /* XACT_ABORT ON у процедурі «приречує» транзакцію: її неможливо
       закомітити, тому відкочуємо її тут явно. Без цього пакет завершився б
       попередженням «Uncommittable transaction is detected». */
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT(N'OK  Процедура відмовила. Код = ', ERROR_NUMBER(),
                 N', повідомлення = ', ERROR_MESSAGE());
END CATCH

DECLARE @StatusAfter NVARCHAR(30) = (SELECT Status FROM dbo.Rentals WHERE Id = @PickedUpRental);
PRINT CONCAT(N'Статус після виклику: ', @StatusAfter);

IF @StatusBefore = @StatusAfter
    PRINT N'OK  Стан НЕ змінився — транзакція відкотилася.';
ELSE
    PRINT N'!!! ПОМИЛКА ТЕСТУ: стан змінився.';
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 7. НЕІСНУЮЧИЙ Id -> помилка 50001 (у Лаб. 02 стане HTTP 404)';
PRINT N'=============================================================';
GO
BEGIN TRY
    EXEC dbo.usp_ConfirmRentalDiscs @RentalId = 999999, @ChangedBy = N'verify';
    PRINT N'!!! ПОМИЛКА ТЕСТУ: процедура не відмовила';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT(N'OK  Код = ', ERROR_NUMBER(), N' (', ERROR_MESSAGE(), N')');
END CATCH
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 8. НЕДОСТАТНЬО ДИСКІВ -> помилка 50003 + відкат';
PRINT N'=============================================================';
GO
DECLARE @Customer BIGINT = (SELECT TOP 1 Id FROM dbo.Customers ORDER BY Id);
DECLARE @Movie   BIGINT = 6;                       -- у seed має лише 1 диск
DECLARE @Before INT    = (SELECT DiscsAvailable FROM dbo.Movies WHERE Id = @Movie);
DECLARE @TmpRental BIGINT;

PRINT CONCAT(N'Фільм Id = ', @Movie, N', дисків доступно: ', @Before);

EXEC dbo.usp_CreateRental
    @CustomerId = @Customer, @DueDate = '2026-12-31',
    @PickupStore = N'verify', @Comment = N'overmovie test',
    @ItemsJson = N'[{"MovieId":6,"Units":99}]',    -- свідомо більше, ніж є
    @CreatedBy = N'verify', @RentalId = @TmpRental OUTPUT;

BEGIN TRY
    EXEC dbo.usp_ConfirmRentalDiscs @RentalId = @TmpRental, @ChangedBy = N'verify';
    PRINT N'!!! ПОМИЛКА ТЕСТУ: підтвердження пройшло при браку дисків';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT(N'OK  Код = ', ERROR_NUMBER(), N' (', ERROR_MESSAGE(), N')');
END CATCH

DECLARE @After INT = (SELECT DiscsAvailable FROM dbo.Movies WHERE Id = @Movie);
PRINT CONCAT(N'Дисків після спроби: ', @After);
IF @Before = @After PRINT N'OK  Залишок не змінився — відкат відпрацював.';

/* прибираємо тестову оренду, щоб verify.sql був повторюваним */
DELETE FROM dbo.Rentals WHERE Id = @TmpRental;
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 9. Спецсимвол в даних (O''Brien) — параметризація, не ін''єкція';
PRINT N'=============================================================';
GO
SELECT Id, MembershipNumber, FullName FROM dbo.Customers WHERE FullName LIKE '%Brien%';
GO
PRINT N'>> Рядок зберігся коректно; у Лаб. 02 те саме значення передається';
PRINT N'>> через SqlParameter / Dapper-параметр, без конкатенації рядків.';
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 10. Знімки полів каталогу в позиціях оренди (політика дублювання)';
PRINT N'=============================================================';
GO
SELECT  i.RentalId, i.MovieId,
        i.MovieTitle  AS SnapshotTitle,
        b.Title      AS CurrentCatalogCopyTitle,
        i.DailyRate   AS SnapshotDailyRate,
        b.DailyRate   AS CurrentCatalogCopyFee
FROM    dbo.RentalItems AS i
INNER JOIN dbo.Movies  AS b ON b.Id = i.MovieId
ORDER BY i.RentalId, i.MovieId;
GO
PRINT N'>> SnapshotTitle / SnapshotDailyRate зафіксовані на момент оформлення';
PRINT N'>> і НЕ оновлюються при зміні Каталогу — так само, як OrderItem в eShop.';
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 11. Аудитні колонки, soft delete та rowversion';
PRINT N'=============================================================';
GO
SELECT  OBJECT_NAME(c.object_id) AS TableName, c.name AS ColumnName, t.name AS DataType
FROM    sys.columns AS c
INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE   c.name IN ('CreatedAt','CreatedBy','UpdatedAt','UpdatedBy','IsDeleted','RowVersion')
  AND   OBJECT_SCHEMA_NAME(c.object_id) = 'dbo'
ORDER BY TableName, ColumnName;
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' 12. Журнал статусів — історія переходів';
PRINT N'=============================================================';
GO
SELECT TOP 20 h.RentalId, h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy, h.Note
FROM   dbo.RentalStatusHistory AS h
ORDER BY h.RentalId, h.ChangedAt;
GO

PRINT N'';
PRINT N'=============================================================';
PRINT N' ПЕРЕВІРКА ЗАВЕРШЕНА';
PRINT N'=============================================================';
GO

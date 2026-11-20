# Лабораторна робота № 03 — Тришарова архітектура на базі Entity Framework Core

**Предметна область:** Система управління відеопрокатом
**Мікросервіс:** «Каталог фільмів» (Catalog)
**База даних:** `VideoRentalCatalogDb` (Проєкт №2 з [Лаб. 01](../Lab01-Databases/))

---

## Структура рішення

Така сама, як у Лаб. 02; відмінність у тому, що доступ до даних будується не
вручну на ADO.NET/Dapper, а на **EF Core**: моделі, Fluent API, міграції,
LINQ to Entities.

| Проєкт | Призначення |
|---|---|
| [`src/VideoRental.Catalog.Domain`](src/VideoRental.Catalog.Domain/) | сутності з **бізнес-правилами в методах**, доменні винятки |
| [`src/VideoRental.Catalog.Dal`](src/VideoRental.Catalog.Dal/) | `DbContext`, конфігурації Fluent API, міграції, репозиторії, Unit of Work, **специфікації** |
| [`src/VideoRental.Catalog.Bll`](src/VideoRental.Catalog.Bll/) | сервіси, DTO, профілі AutoMapper, валідатори FluentValidation |
| [`src/VideoRental.Catalog.Api`](src/VideoRental.Catalog.Api/) | контролери, глобальний обробник помилок, Swagger |
| [`tests/VideoRental.Catalog.Tests`](tests/VideoRental.Catalog.Tests/) | **76 тестів**: інваріанти сутностей, репозиторії, специфікації, валідатори, мапінг |
| [`tests/api-tests.py`](tests/api-tests.py) | **73 перевірки** всіх критеріїв приймання через HTTP |

---

## Запуск

### 1. Підняти SQL Server

```powershell
docker run -d --name netlabs-sql -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=LabsDev!Pass2026" -e "MSSQL_PID=Developer" -p 1433:1433 -v netlabs-sqldata:/var/opt/mssql --restart unless-stopped mcr.microsoft.com/mssql/server:2022-latest
```

### 2. Застосувати міграції

```powershell
dotnet tool install --global dotnet-ef --version 9.0.11
```

```powershell
dotnet ef database update --project src\VideoRental.Catalog.Dal --startup-project src\VideoRental.Catalog.Api
```

> **Версія інструмента має відповідати EF Core.** Проєкт використовує EF Core
> **9.0.11** і націлений на `net9.0`, тому потрібен `dotnet-ef` саме **9.x**:
> `dotnet-ef` 11.x вимагає рантайм .NET 10+ і падає з
> `Could not load file or assembly 'System.Runtime, Version=10.0.0.0'`.

### 3. Запустити API

```powershell
dotnet run --project src\VideoRental.Catalog.Api --urls http://localhost:5290
```

Swagger UI: **http://localhost:5290/swagger** (корінь `/` робить редирект туди).

При старті застосунок сам застосовує міграції (`Database:MigrateOnStartup`)
і виконує **ідемпотентний** сідінг (`Database:SeedOnStartup`), тож кроку 2
можна й не робити окремо.

### Перевизначення рядка підключення

```powershell
$env:ConnectionStrings__CatalogDb = "Server=localhost,1433;Database=VideoRentalCatalogDb;User Id=sa;Password=LabsDev!Pass2026;TrustServerCertificate=True;Encrypt=False"
```

---

## Частина 1 — EF Core (Code-First) і Fluent API

### Моделі з бізнес-правилами в методах

Усі властивості сутностей мають **`private set`**: стан не можна зіпсувати
«збоку». Правила перевіряють конструктори й методи — так само, як
`CatalogItem` в eShop (`RemoveStock` / `AddStock`).

```csharp
public void RemoveStock(int quantity)
{
    if (quantity <= 0)
        throw new DomainException("Quantity must be positive");
    if (AvailableDiscs < quantity)
        throw new DomainException(
            $"Only {AvailableDiscs} discs of '{Title}' are available, {quantity} requested");

    AvailableDiscs -= quantity;
    UpdatedAt = DateTime.UtcNow;
}
```

| Сутність | Інваріанти, які вона боронить сама |
|---|---|
| `Movie` | тариф > 0; достатній залишок; інвентарний номер унікальний у межах фільму; головний жанр лише один; жанр не призначається двічі |
| `Director` | режисера з фільмами видалити не можна |
| `DiscCopy` | стан лише з переліку `New/Good/Worn/Damaged/Lost` |
| `MovieDetail` | опис не порожній; кількість хвилин > 0 |

### Fluent API

Окремий клас `IEntityTypeConfiguration<T>` на кожну сутність; усі підключаються
**централізовано** одним рядком:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
```

### Зв'язки

| Тип | Зв'язок | Реалізація |
|---|---|---|
| **1:1** | `Movie` ↔ `MovieDetail` | `HasOne(b => b.Detail).WithOne().HasForeignKey<MovieDetail>(d => d.MovieId)` — PK залежної сутності є водночас FK |
| **1:N** | `Director` → `Movie` | `HasOne(b => b.Director).WithMany(a => a.Movies)`, `DeleteBehavior.Restrict` |
| **1:N** | `Movie` → `DiscCopy` | `HasMany(b => b.Discs).WithOne()`, `DeleteBehavior.Cascade` |
| **M:N** | `Movie` ↔ `Genre` | явна проміжна сутність `MovieGenre` зі складеним ключем `(MovieId, GenreId)` |

**Чому M:N описано явною сутністю.** Проміжна таблиця має **власні поля**
`AddedAt`, `AddedBy`, `IsPrimary`. Автоматичний M:N
(`HasMany().WithMany()`) створив би «чисту» таблицю лише з двома зовнішніми
ключами і зберегти ці поля не дозволив би. Саме завдяки `AddedAt` працює
LINQ-запит «нові фільми жанру за період».

### Обмеження й індекси

Міграція створює **6 CHECK-обмежень** (`CK_Movies_DailyRate`,
`CK_Movies_AvailableDiscs`, `CK_Movies_ReleaseYear`, `CK_Directors_BirthYear`,
`CK_DiscDiscs_Condition`, `CK_MovieDetails_DurationMinutes`), унікальні індекси
`UQ_Movies_Barcode`, `UQ_Genres_Name`, `UQ_Genres_Slug`,
`UQ_DiscDiscs_DiscNumber` та пошукові індекси — кожен із коментарем,
під який запит його створено (див. [`Configurations/`](src/VideoRental.Catalog.Dal/Configurations/)).

`RowVersion` (`IsRowVersion()`) у `Movie` і `Director` дає **оптимістичну
конкурентність**: конфлікт виявляється під час запису й повертається як **409**.

### Міграції

```powershell
dotnet ef migrations add InitialCreate --project src\VideoRental.Catalog.Dal --startup-project src\VideoRental.Catalog.Api
```

```powershell
dotnet ef migrations script --project src\VideoRental.Catalog.Dal --startup-project src\VideoRental.Catalog.Api --output Docs\InitialCreate.sql --idempotent
```

Згенерований скрипт: [`Docs/InitialCreate.sql`](Docs/InitialCreate.sql).
Папка міграцій: [`src/VideoRental.Catalog.Dal/Migrations/`](src/VideoRental.Catalog.Dal/Migrations/).

---

## Частина 2 — Репозиторії, Unit of Work, три способи завантаження

### Async Generic Repository

`GetByIdAsync`, `GetAllAsync`, `AddAsync`, `Update`, `Delete`,
`SaveChangesAsync` + `ListAsync`/`CountAsync` для специфікацій.
`Update` і `Delete` **синхронні**: вони лише змінюють стан у контексті й до бази
не звертаються.

### Три специфічні репозиторії

| Репозиторій | Спосіб | Метод | Запитів до БД |
|---|---|---|---|
| `MovieRepository` | **Eager Loading** | `GetWithDetailsAsync` — `Include`/`ThenInclude` | **1** |
| `DirectorRepository` | **Explicit Loading** | `GetWithMoviesAsync` — `Entry(...).Collection(...).LoadAsync()` | **2** |
| `MovieGenreRepository` | **LINQ to Entities** | `GetRecentlyAddedAsync` — через проміжну сутність M:N і її поле `AddedAt` | 1 |

**Підтверджено логом SQL-команд EF Core** —
[`../Docs/lab03-eager-vs-explicit.log`](../Docs/lab03-eager-vs-explicit.log):

```
--- EAGER LOADING: GET /api/movies/1 -> ОДИН запит із JOIN-ами -------------
[02:22:42 INF] Executed DbCommand (3ms) [Parameters=[@__id_0='1'], ...]

--- EXPLICIT LOADING: GET /api/directors/1?includeMovies=true -> ДВА запити ---
[02:22:43 INF] Executed DbCommand (2ms) [Parameters=[@__id_0='1'], ...]
[02:22:43 INF] Executed DbCommand (2ms) [Parameters=[@__p_0='1'],  ...]

ВИСНОВОК: Eager = 1 запит, Explicit = 2 запити.
```

> Explicit Loading працює лише для **відстежуваних** сутностей, тому
> `GetWithMoviesAsync` свідомо не викликає `AsNoTracking()` на кореневій сутності.

### Unit of Work

`SaveChangesAsync()` викликається **ОДИН раз** наприкінці бізнес-операції:
репозиторії лише додають/змінюють/видаляють сутності в контексті, а всі зміни
потрапляють у базу однією транзакцією.

На відміну від Лаб. 02, транзакцію вручну відкривати не треба — `DbContext`
сам є одиницею роботи (як `OrderingContext` в eShop). Явна транзакція
(`BeginTransactionAsync`) лишена для рідкісного випадку, коли одна операція
робить кілька послідовних `SaveChangesAsync`.

---

## Частина 3 — DTO, AutoMapper, Web/API

### Ендпоінти

| Метод | Шлях | Призначення | Статуси |
|---|---|---|---|
| GET | `/api/movies` | список із **пагінацією, фільтрацією, сортуванням** | 200, 400 |
| GET | `/api/movies/{id}` | картка фільму (**Eager Loading**) | 200, 404 |
| GET | `/api/movies/out-of-stock` | друга специфікація | 200 |
| GET | `/api/movies/by-genre/{slug}` | третя специфікація | 200 |
| POST | `/api/movies` | створити фільм | **201** + `Location`, 400, 404, 409 |
| PUT | `/api/movies/{id}` | змінити назву й тариф | 200, 400, 404, 409 |
| POST | `/api/movies/{id}/add-stock` | оприбуткувати диски | 200, 404 |
| POST | `/api/movies/{id}/remove-stock` | списати диски | 200, 404, **409** |
| POST | `/api/movies/{id}/discs` | додати диск (1:N) | 200, 400, 404, **409** |
| POST | `/api/movies/{id}/genres` | призначити жанр (M:N) | 200, 404, **409** |
| DELETE | `/api/movies/{id}` | м'яке видалення | **204**, 404 |
| GET | `/api/directors` | перелік режисерів | 200 |
| GET | `/api/directors/{id}?includeMovies=` | режисер (**Explicit Loading**) | 200, 404 |
| POST | `/api/directors` | створити режисера | **201**, 400, 409 |
| DELETE | `/api/directors/{id}` | м'яке видалення | **204**, 404, **409** |
| GET | `/api/genres` | довідник жанрів | 200 |
| GET | `/api/genres/stats` | фільмів у кожному жанрі (`GROUP BY` у БД) | 200 |
| GET | `/api/genres/{name}/recent-movies` | **LINQ через M:N** за полем `AddedAt` | 200, 400 |
| GET | `/health` | доступність бази | 200, 503 |

Контролери **тонкі**: не звертаються до `DbContext`, не містять бізнес-правил.

---

## Частина 4 — Сідінг, пагінація, валідація, помилки, логування, специфікації

### 4.1. Два режими ідемпотентного сідінгу

| Режим | Де | Що саме |
|---|---|---|
| **Модельний (статичний)** | `HasData` у `DirectorConfiguration`, `GenreConfiguration` | довідники з **фіксованими Id** — ними керують міграції |
| **Програмний** | [`DbInitializer.SeedAsync`](src/VideoRental.Catalog.Dal/DbInitializer.cs) після `MigrateAsync()` | фільми з навігаціями (`MovieDetail`, `DiscCopy`, `MovieGenre`) |

Ідемпотентність: перед вставкою перевіряється наявність за бізнес-ключем (Barcode),
тому **повторний запуск дублікатів не створює**. Цілісність FK дотримана:
спочатку довідники (через міграцію), потім залежні записи.
Сідінг вимикається в production (`Database:SeedOnStartup`).

> Значення в `HasData` **статичні** (`SeedConstants.SeedDate`), а не
> `DateTime.UtcNow`: EF Core порівнює seed-дані між міграціями, і динамічне
> значення давало б нову міграцію при кожній генерації.

### 4.2. Пагінація, фільтрація, сортування — на рівні БД

**Контракт запиту:** `page` (з 1), `pageSize` (за замовчуванням 20, максимум
100), `search`, `minRate`, `maxRate`, `directorId`, `genre`, `onlyAvailable`,
`sortBy` (`title|fee|year|discs`), `sortDir` (`asc|desc`).

**Контракт відповіді:** `items`, `totalCount`, `page`, `pageSize`, `totalPages`,
`hasNext`, `hasPrevious`. Метадані дублюються в заголовках
`X-Pagination-TotalCount`, `-Page`, `-PageSize`, `-TotalPages`
(тіло — зручне клієнту, заголовки — для кешування).

Фільтрація, сортування та `Skip`/`Take` виконуються **до** матеріалізації,
тому база повертає лише потрібну хвилину (`OFFSET … FETCH`), а `totalCount`
рахує теж БД окремим `COUNT(*)` **без** пагінації.

Сортування дозволене **лише за полями з білого списку** — довільний рядок
із query відхиляється з 400.

### 4.3. Два рівні валідації

| Рівень | Чим | Що перевіряє |
|---|---|---|
| **Модельний** | `DataAnnotations` на DTO | формат: обов'язковість, довжина, діапазон |
| **Складні правила** | **FluentValidation** | регулярний вираз Barcode, взаємозалежні поля (`studio` без `description` → 400), заборона дублікатів жанрів, білий список полів сортування |

Правила **не дублюються**: DataAnnotations відповідають за формат,
FluentValidation — за складну логіку.

> Автоматична валідація через пакет `FluentValidation.AspNetCore`
> (`AddFluentValidation`) **застаріла** й більше не підтримується. Валідатори
> зареєстровано через `FluentValidation.DependencyInjectionExtensions`
> (`AddValidatorsFromAssemblyContaining`) і викликаються **явно** в контролері:
>
> ```csharp
> var validation = await validator.ValidateAsync(input, ct);
> if (!validation.IsValid)
>     return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
> ```

### 4.4. Централізована обробка помилок (ProblemDetails)

| Виняток | HTTP |
|---|---|
| `EntityNotFoundException` | **404** |
| `ValidationException` | **400** (`ValidationProblemDetails` із розбивкою по полях) |
| `ConflictException` | **409** |
| `DomainException` (інваріант сутності) | **409** |
| `DbUpdateConcurrencyException` | **409** |
| `DbUpdateException` з порушенням UNIQUE (2601/2627) | **409** |
| решта | **500** (деталі — лише в Development) |

Порядок перевірки важливий: похідні типи стоять **перед** базовим
`DomainException`. Два останні рядки — **страховка на рівні БД**: перевірку
інваріанта робить сутність, але в умовах конкурентності двоє користувачів
можуть пройти її одночасно; тоді спрацює UNIQUE-індекс, і це теж конфлікт,
а не внутрішня помилка сервера.

Кожен виняток логується **рівно один раз** — в обробнику.

### 4.5. Логування — Serilog

Структуровані логи, sink-и Console + File (`logs/catalog-api-*.log`),
збагачення `FromLogContext` і властивістю `Service`. Рівень
`Microsoft.EntityFrameworkCore.Database.Command` піднято до `Information` —
саме це дає змогу бачити SQL-запити EF Core і перевірити Eager vs Explicit.

### 4.6. Specification pattern (Ardalis.Specification)

Складна логіка вибірок винесена зі сервісів у класи специфікацій
([`Specifications/`](src/VideoRental.Catalog.Dal/Specifications/)):

| Специфікація | Що описує |
|---|---|
| `MoviesFilterSpec` | фільтрація + сортування + `Include` + пагінація |
| `MoviesByGenreSlugSpec` | топ фільмів жанру за slug |
| `OutOfStockMoviesSpec` | фільми, яких немає в наявності |

Репозиторій лише **приймає** специфікацію й застосовує її
(`Set.WithSpecification(spec)`), нічого не знаючи про критерії. Усі три
специфікації працюють через **той самий** метод `ListAsync` — у репозиторії
не змінено жодного рядка (перевірено тестами).

---

## Тестування

### Unit-тести — 76 тестів

```powershell
dotnet test tests\VideoRental.Catalog.Tests
```

| Група | Що перевіряє |
|---|---|
| `DomainEntityTests` | інваріанти сутностей **без бази й без моків** — головна перевага реалізації правил у методах |
| `RepositoryTests` | Eager / Explicit Loading, LINQ через M:N, усі три специфікації, `CountAsync` ігнорує пагінацію, ідемпотентність сідінгу (провайдер InMemory) |
| `ValidatorTests` | формат Barcode, взаємозалежні поля, білий список сортування, межі `pageSize` |
| `MappingTests` | `AssertConfigurationIsValid()` + проєкції в DTO |
| `PagedResultTests` | `TotalPages`, `HasNext`, `HasPrevious`, обмеження `pageSize` |

### API-тести — 73 перевірки

```powershell
py -I tests\api-tests.py http://localhost:5290
```

Скрипт **самоочисний**: на старті прибирає залишки попередніх прогонів, тому
його можна запускати багато разів поспіль.

| Перевірка з методички | Блок | Результат |
|---|---|---|
| у лозі SQL eager-репозиторій робить 1 запит, explicit — 2 | 7 + окремий лог | підтверджено |
| POST із порожнім `Name` → **400** з переліком помилок по полях, а не 500 | 3 | 400 + `errors` |
| повторне створення з тим самим Barcode → **409** | 5 | 409 |
| неіснуючий Id → **404** у форматі ProblemDetails | 4 | 404 |
| `page=2&pageSize=5` → `totalCount` = **усі** записи в БД, а не на сторінці | 1 | підтверджено |
| повторний запуск застосунку не дублює seed-дані | 0 | 6 фільмів після багатьох рестартів |
| друга специфікація працює через той самий `WithSpecification()` без змін у репозиторії | 9 | підтверджено |

Збережений вивід: [`../Docs/lab03-api-tests.log`](../Docs/lab03-api-tests.log).

### Знімки екрана

- [`../Docs/screenshots/lab03-swagger.png`](../Docs/screenshots/lab03-swagger.png) — Swagger з ендпоінтами
- [`../Docs/screenshots/lab03-pagination.png`](../Docs/screenshots/lab03-pagination.png) — відповідь із пагінацією

---

## Критерії приймання — відповідність

| Критерій Лаб. 03 | Де виконано |
|---|---|
| сутності з ключами, зв'язками та обмеженнями (Code First + Fluent API) | [`Configurations/`](src/VideoRental.Catalog.Dal/Configurations/) |
| конфігурації підключені централізовано в `OnModelCreating` | `ApplyConfigurationsFromAssembly` |
| **є M:N з проміжною сутністю** | `MovieGenre` з полями `AddedAt`, `AddedBy`, `IsPrimary` |
| міграції, **ідемпотентний сідінг**, коректне застосування змін | [`Migrations/`](src/VideoRental.Catalog.Dal/Migrations/), `HasData` + `DbInitializer` |
| **Async Generic Repository** | [`GenericRepository<T>`](src/VideoRental.Catalog.Dal/Repositories/Repositories.cs) |
| **3 специфічні репозиторії** (eager, explicit, LINQ для M:N) | `MovieRepository`, `DirectorRepository`, `MovieGenreRepository` |
| **Unit of Work** | [`UnitOfWork`](src/VideoRental.Catalog.Dal/UnitOfWork.cs), один `SaveChangesAsync` |
| бізнес-сервіси, DTO, AutoMapper; контролери не чіпають `DbContext` | [`Bll/`](src/VideoRental.Catalog.Bll/), [`Controllers/`](src/VideoRental.Catalog.Api/Controllers/) |
| асинхронні контролери, коректні коди, Swagger | усі дії з `CancellationToken`; `/swagger` |
| **пагінація, фільтрація, сортування на рівні SQL** | `MoviesFilterSpec` + `CountAsync` без пагінації |
| валідація DataAnnotations + FluentValidation → `ValidationProblemDetails` | [`Validators/`](src/VideoRental.Catalog.Bll/Validators/) |
| централізована обробка помилок (ProblemDetails) | [`DomainExceptionHandler`](src/VideoRental.Catalog.Api/Infrastructure/DomainExceptionHandler.cs) |
| логування Serilog зі структурованими властивостями | [`appsettings.json`](src/VideoRental.Catalog.Api/appsettings.json) |
| **Specification pattern** | [`Specifications/`](src/VideoRental.Catalog.Dal/Specifications/) |

---

## Попередня робота

**Лаб. 02** — та сама тришарова архітектура, але на **ADO.NET та Dapper** над
базою Проєкту №1 (`VideoRentalRentalsDb`), з Unit of Work на явних транзакціях,
збережуваними процедурами та .NET Aspire →
[`../Lab02-AdoNet-Dapper/`](../Lab02-AdoNet-Dapper/)

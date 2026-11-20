/* =============================================================================
   AppHost — ОРКЕСТРАТОР платформи (Частина 2).

   Одна команда піднімає всю систему: контейнери SQL Server і MongoDB,
   три мікросервіси з Лаб. 02–04, Агрегатор і Gateway:

       dotnet run --project src/VideoRental.Platform.AppHost --launch-profile http

   Після старту в консолі з'являється посилання на Aspire Dashboard — там
   видно статус кожного ресурсу, логи, трасування й метрики.

   ВАЖЛИВО: AppHost не містить бізнес-логіки. Це конфігураційний проєкт,
   який описує РЕСУРСИ та їхні ЗАЛЕЖНОСТІ. Сервіси про AppHost не знають —
   залежність односпрямована.
   -----------------------------------------------------------------------------
   ДВА КЛЮЧОВИХ МЕХАНІЗМИ

   WithReference(db)  — передає сервісу рядок підключення у змінній оточення
                        ConnectionStrings__<ім'я ресурсу>. Імена ресурсів
                        (RentalsDb, CatalogDb) збігаються з іменами, які
                        сервіси вже читають з конфігурації в Лаб. 02–03,
                        тому код сервісів змінювати не потрібно.

   WithReference(svc) — передає адресу іншого СЕРВІСУ для service discovery:
                        "https+http://catalog-api" перетворюється на реальний
                        хост і порт. Без цього Агрегатор не знайшов би сервіси.

   WaitFor(x)         — не стартувати, доки x не готовий. WithReference лише
                        передає адресу, WaitFor керує ПОРЯДКОМ запуску, тому
                        зазвичай потрібні обидва: БД -> сервіси -> Агрегатор
                        -> Gateway.
   ============================================================================= */

var builder = DistributedApplication.CreateBuilder(args);

/* -----------------------------------------------------------------------------
   1. SQL Server у контейнері — для Сервісу оренд (Лаб. 02) і Каталогу (Лаб. 03).

   Пароль — параметр-СЕКРЕТ, а не рядок у коді. Значення береться з секретів
   користувача AppHost:

       dotnet user-secrets set Parameters:sql-password "..." \
           --project src/VideoRental.Platform.AppHost

   WithDataVolume() обов'язковий: без нього дані зникають при кожному
   перезапуску контейнера. ContainerLifetime.Persistent — контейнер не
   гаситься разом з AppHost, тому наступний запуск стартує за секунди.
   ----------------------------------------------------------------------------- */
var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sqlServer = builder.AddSqlServer("sql", password: sqlPassword, port: 14335)
    .WithDataVolume("video-rental-platform-sqldata")
    .WithLifetime(ContainerLifetime.Persistent);

// Database-per-service: у кожного мікросервісу СВОЯ база (Лаб. 01),
// спільний лише процес SQL Server — так дешевше для локальної розробки.
var rentalsDb = sqlServer.AddDatabase("RentalsDb", "VideoRentalRentalsDb");
var catalogDb = sqlServer.AddDatabase("CatalogDb", "VideoRentalCatalogDb");

/* -----------------------------------------------------------------------------
   2. MongoDB у контейнері — для Сервісу відгуків (Лаб. 04).
      Окремий volume: дані відгуків зберігаються незалежно від SQL.
   ----------------------------------------------------------------------------- */
var mongo = builder.AddMongoDB("mongo", port: 27019)
    .WithDataVolume("video-rental-platform-mongodata")
    .WithLifetime(ContainerLifetime.Persistent);

var reviewsDb = mongo.AddDatabase("ReviewsDb", "VideoRentalReviewsDb");

/* -----------------------------------------------------------------------------
   3. Мікросервіси з попередніх робіт.

   Aspire 17 підключає проєкт за ШЛЯХОМ (AddDotnetProject), а не за
   згенерованим типом Projects.* — узагальнений AddProject<T> оголошено
   застарілим. Проєкти лишаються на своїх місцях (Лаб. 02–04): платформа
   їх ЗАПУСКАЄ, а не копіює до себе.

   Імена ресурсів rentals-api / catalog-api / reviews-api — це логічні імена
   для service discovery: саме їх використовують Gateway і Агрегатор.
   ----------------------------------------------------------------------------- */

// --- Сервіс оренд: SQL Server + ADO.NET / Dapper (Лаб. 02) ---
var rentals = builder
    .AddDotnetProject("rentals-api",
        "../../../Lab02-AdoNet-Dapper/src/VideoRental.Rentals.Api/VideoRental.Rentals.Api.csproj")
    .WithReference(rentalsDb)
    .WaitFor(rentalsDb)
    // база в контейнері створюється ПОРОЖНЬОЮ -> API сам накатує
    // schema.sql + procedures.sql + seed.sql з Лаб. 01 (SqlScriptInitializer)
    .WithEnvironment("Database__ApplyScriptsOnStartup", "true")
    .WithExternalHttpEndpoints();

// --- Каталог: SQL Server + EF Core Code-First (Лаб. 03) ---
var catalog = builder
    .AddDotnetProject("catalog-api",
        "../../../Lab03-EfCore/src/VideoRental.Catalog.Api/VideoRental.Catalog.Api.csproj")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    // EF Core сам застосує міграції та ідемпотентний сідінг при старті
    .WithEnvironment("Database__MigrateOnStartup", "true")
    .WithEnvironment("Database__SeedOnStartup", "true")
    .WithExternalHttpEndpoints();

// --- Відгуки: MongoDB + Clean Architecture (Лаб. 04) ---
// Сервіс читає налаштування з секції MongoDb, а не з ConnectionStrings,
// тому рядок підключення передається прямо в потрібну змінну оточення —
// код сервісу знову лишається без змін.
var reviews = builder
    .AddDotnetProject("reviews-api",
        "../../../Lab04-CleanArchitecture-MongoDB/src/VideoRental.Reviews.WebApi/VideoRental.Reviews.WebApi.csproj")
    .WithReference(reviewsDb)
    .WaitFor(reviewsDb)
    .WithEnvironment("MongoDb__ConnectionString", reviewsDb)
    .WithEnvironment("MongoDb__DatabaseName", "VideoRentalReviewsDb")
    .WithExternalHttpEndpoints();

/* -----------------------------------------------------------------------------
   4. Агрегатор — опитує всі три сервіси, тому отримує посилання на кожен
      і стартує лише після них.
   ----------------------------------------------------------------------------- */
var aggregator = builder
    .AddDotnetProject("aggregator",
        "../VideoRental.Platform.Aggregator/VideoRental.Platform.Aggregator.csproj")
    .WithReference(rentals)
    .WithReference(catalog)
    .WithReference(reviews)
    .WaitFor(rentals)
    .WaitFor(catalog)
    .WaitFor(reviews)
    .WithExternalHttpEndpoints();

/* -----------------------------------------------------------------------------
   5. Gateway — єдина точка входу, остання ланка ланцюжка запуску.

   Порт ФІКСОВАНИЙ (5000), щоб адреса клієнта не змінювалася між запусками:
   решті ресурсів порти призначає Aspire автоматично.
   ----------------------------------------------------------------------------- */
builder
    .AddDotnetProject("gateway",
        "../VideoRental.Platform.ApiGateway/VideoRental.Platform.ApiGateway.csproj")
    .WithHttpEndpoint(port: 5001, name: "public")
    .WithReference(rentals)
    .WithReference(catalog)
    .WithReference(reviews)
    .WithReference(aggregator)
    .WaitFor(aggregator)
    .WithExternalHttpEndpoints();

builder.Build().Run();

using System.Reflection;
using VideoRental.Rentals.Api.Infrastructure;
using VideoRental.Rentals.Bll.Abstractions;
using VideoRental.Rentals.Bll.Mapping;
using VideoRental.Rentals.Bll.Services;
using VideoRental.Rentals.Dal.Abstractions;
using VideoRental.Rentals.Dal.Infrastructure;
using VideoRental.Platform.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

/* =============================================================================
   1. Конфігурація: рядок підключення з appsettings.json з можливістю
      перевизначення змінною оточення (подвійне підкреслення замість двокрапки):
         ConnectionStrings__RentalsDb=Server=...;Database=VideoRentalRentalsDb;...
      Саме це ім'я ресурсу використовує .NET Aspire AppHost, тому код API
      при переході на Aspire змінювати не доведеться.
   ============================================================================= */
var connectionString = builder.Configuration.GetConnectionString("RentalsDb")
    ?? throw new InvalidOperationException("Connection string 'RentalsDb' is missing");

/* =============================================================================
   2. Платформні налаштування (Лаб. 05).

   Один рядок замінює власну конфігурацію логування: ServiceDefaults дає
   Serilog зі структурованими логами, OpenTelemetry (трасування вхідних
   запитів, вихідних HTTP-викликів і звернень до БД), health checks,
   service discovery і стійкі HttpClient-и — однаково в усіх компонентах
   платформи.

   Поза платформою сервіс працює так само: рівні й sink-и логування
   по-старому читаються з appsettings.json.
   ============================================================================= */
builder.AddServiceDefaults();

/* =============================================================================
   3. Dapper: одноразове налаштування мапінгу (DateOnly -> DATE тощо).
   ============================================================================= */
DapperConfiguration.Configure();

/* =============================================================================
   4. Шар доступу до даних.
      Unit of Work — SCOPED: один екземпляр і ОДНЕ з'єднання на HTTP-запит.
      Усі репозиторії доступні через нього, тому окремо вони не реєструються:
      це гарантує, що в межах однієї бізнес-операції вони працюють через ту
      саму транзакцію.
   ============================================================================= */
builder.Services.AddScoped<IUnitOfWork>(_ => new UnitOfWork(connectionString));

/* =============================================================================
   5. Бізнес-логіка та мапінг.
      AutoMapper 15.0+ розповсюджується за комерційною ліцензією з безкоштовною
      Community-редакцією (навчальне й некомерційне використання). Ключ
      реєструється на automapper.io і підхоплюється з конфігурації; без нього
      застосунок працює, але пише в лог повідомлення про відсутню ліцензію.
   ============================================================================= */
builder.Services.AddAutoMapper(cfg =>
{
    cfg.LicenseKey = builder.Configuration["AutoMapper:LicenseKey"];
    cfg.AddProfile<RentalsProfile>();
});

builder.Services.AddScoped<IRentalService, RentalService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IMovieService, MovieService>();

/* =============================================================================
   6. Web/API: контролери, ProblemDetails, глобальний обробник винятків.
   ============================================================================= */
builder.Services.AddControllers();

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Extensions["service"] = "VideoRental.Rentals.Api";
    });

builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// автоматичну 400-відповідь на помилки DataAnnotations теж віддаємо
// у форматі ProblemDetails (ValidationProblemDetails)
builder.Services.Configure<ApiBehaviorOptions>(o => o.SuppressMapClientErrors = false);

/* =============================================================================
   7. OpenAPI / Swagger — опис шляхів, DTO та очікуваних статусів.
   ============================================================================= */
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Video Rental API",
        Version = "v1",
        Description = """
            Мікросервіс «Оренда фільмів» системи управління відеопрокатом.
            Тришарова архітектура: Web/API -> BLL -> DAL (ADO.NET + Dapper),
            Unit of Work з транзакціями, збережувані процедури SQL Server.

            Помилки повертаються у форматі ProblemDetails (RFC 9457):
            404 — ресурс відсутній, 409 — порушення бізнес-правила
            (недопустимий перехід статусу, брак дисків), 400 — валідація.
            """
    });

    // XML-комментарії контролерів і DTO -> в Swagger UI
    var xml = Path.Combine(AppContext.BaseDirectory,
        $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml))
        options.IncludeXmlComments(xml, includeControllerXmlComments: true);
});

var app = builder.Build();

/* =============================================================================
   8. Ініціалізація бази (лише коли ввімкнено).
      Потрібна для режиму Aspire «усе однією командою»: Aspire піднімає
      ПОРОЖНІЙ контейнер SQL Server, і схему треба застосувати автоматично.
      Увімкнення — Database:ApplyScriptsOnStartup (за замовчуванням false, щоб
      запуск проти «живої» бази нічого не чіпав).
   ============================================================================= */
if (app.Configuration.GetValue("Database:ApplyScriptsOnStartup", false))
{
    var scriptsDirectory = app.Configuration["Database:ScriptsDirectory"]
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "Lab01-Databases", "Project1-SqlServer-AdoNet"));

    var initializer = new SqlScriptInitializer(
        connectionString,
        app.Services.GetRequiredService<ILogger<SqlScriptInitializer>>());

    await initializer.InitializeAsync(scriptsDirectory);
}

/* =============================================================================
   9. Конвеєр обробки запитів.
   ============================================================================= */
app.UseExceptionHandler();      // має стояти ПЕРЕД маршрутизацією
// CorrelationId + логування запитів (ServiceDefaults): наскрізний
// ідентифікатор потрапляє в LogContext, у заголовок відповіді та у вихідні
// HTTP-виклики — так один запит видно в логах усіх сервісів ланцюжка
app.UseServiceDefaults();

app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "Video Rental API v1");
    o.DocumentTitle = "Video Rental API";
});

app.MapControllers();

// /alive (живучість) і /health/ready (усі перевірки) від ServiceDefaults.
// Власний /health нижче лишається: він перевіряє саме базу цього сервісу,
// а двічі зареєстрований однаковий маршрут дав би AmbiguousMatchException.
app.MapDefaultEndpoints("/health/ready");

// корінь відкриває Swagger UI — зручно під час демонстрації
app.MapGet("/", () => Results.Redirect("/swagger"))
   .ExcludeFromDescription();

// простий health-ендпоінт: перевіряє, що база досяжна
app.MapGet("/health", async (IUnitOfWork uow, CancellationToken ct) =>
{
    _ = await uow.Movies.GetAllAsync(ct);
    return Results.Ok(new { status = "Healthy", service = "VideoRental.Rentals.Api" });
})
.WithName("Health")
.WithTags("Diagnostics");

try
{
    Log.Information("Starting VideoRental.Rentals.Api");
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

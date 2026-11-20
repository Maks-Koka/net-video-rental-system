using VideoRental.Platform.Aggregator.Dtos;

namespace VideoRental.Platform.Aggregator.Clients;

/* =============================================================================
   Typed HttpClients (Частина 6.1) — по одному класу на мікросервіс.

   BaseAddress задається в Program.cs як "https+http://rentals-api":
     • rentals-api — ЛОГІЧНЕ ім'я ресурсу в AppHost, не хост і не порт;
     • схема https+http означає «HTTPS, якщо сервіс його має, інакше HTTP»;
     • реальну адресу підставляє service discovery Aspire.

   CorrelationId у вихідні запити додає CorrelationIdHandler, підключений до
   ВСІХ HttpClient-ів у ServiceDefaults — у клієнтах про нього нічого не знають.

   404 трактується як «немає даних» (null), а не як помилка: інакше відсутня
   фільм в Каталозі «валила» б усю агрегацію.
   ============================================================================= */

/* -----------------------------------------------------------------------------
   Інтерфейси клієнтів існують заради ТЕСТІВ: сервіс агрегації залежить від
   абстракції, тому часткову відмову («сервіс недоступний») у модульному тесті
   можна відтворити підставним клієнтом, без підняття мікросервісів.
   ----------------------------------------------------------------------------- */

public interface IRentalsClient
{
    Task<RentalDto?> GetRentalAsync(long id, CancellationToken ct);
    Task<CustomerDto?> GetCustomerAsync(long id, CancellationToken ct);
    Task<RentalsMovieDto?> GetMovieProjectionAsync(long movieId, CancellationToken ct);
}

public interface ICatalogClient
{
    Task<MovieDto?> GetMovieAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<MovieDto>> GetMoviesAsync(IEnumerable<long> ids, CancellationToken ct);
}

public interface IReviewsClient
{
    Task<IReadOnlyList<ReviewDto>> GetForMovieAsync(long movieId, int limit, CancellationToken ct);
    Task<IReadOnlyList<ReviewDto>> GetForMoviesAsync(
        IEnumerable<long> movieIds, int limitPerMovie, CancellationToken ct);
    Task<IReadOnlyList<MovieRatingDto>> GetRatingsAsync(
        IEnumerable<long> movieIds, CancellationToken ct);
}

/// <summary>Базові дії, спільні для всіх типізованих клієнтів.</summary>
public abstract class ServiceClientBase(HttpClient http, ILogger logger)
{
    protected HttpClient Http { get; } = http;
    protected ILogger Logger { get; } = logger;

    /// <summary>GET, у якому 404 повертає <c>null</c>, а не виняток.</summary>
    protected async Task<T?> GetOrDefaultAsync<T>(string url, CancellationToken ct)
    {
        Logger.LogDebug("GET {Service}{Url}", Http.BaseAddress, url);

        using var response = await Http.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.LogDebug("{Url}: 404 — даних немає", url);
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }
}

/// <summary>Клієнт Сервісу оренд (Лаб. 02: SQL Server + ADO.NET/Dapper).</summary>
public sealed class RentalsClient(HttpClient http, ILogger<RentalsClient> logger)
    : ServiceClientBase(http, logger), IRentalsClient
{
    public Task<RentalDto?> GetRentalAsync(long id, CancellationToken ct)
        => GetOrDefaultAsync<RentalDto>($"/api/rentals/{id}", ct);

    public Task<CustomerDto?> GetCustomerAsync(long id, CancellationToken ct)
        => GetOrDefaultAsync<CustomerDto>($"/api/customers/{id}", ct);

    public Task<RentalsMovieDto?> GetMovieProjectionAsync(long movieId, CancellationToken ct)
        => GetOrDefaultAsync<RentalsMovieDto>($"/api/movies/{movieId}", ct);
}

/// <summary>Клієнт Каталогу (Лаб. 03: SQL Server + EF Core).</summary>
public sealed class CatalogClient(HttpClient http, ILogger<CatalogClient> logger)
    : ServiceClientBase(http, logger), ICatalogClient
{
    public Task<MovieDto?> GetMovieAsync(long id, CancellationToken ct)
        => GetOrDefaultAsync<MovieDto>($"/api/movies/{id}", ct);

    /// <summary>
    /// Фільми за переліком ідентифікаторів.
    /// </summary>
    /// <remarks>
    /// Каталог не має ендпоінта «кілька фільмів за списком id», тому клієнт
    /// робить кілька запитів ОДНОЧАСНО. Відсутні фільми (404) відкидаються.
    /// </remarks>
    public async Task<IReadOnlyList<MovieDto>> GetMoviesAsync(
        IEnumerable<long> ids, CancellationToken ct)
    {
        var movies = await Task.WhenAll(ids.Select(id => GetMovieAsync(id, ct)));

        return movies.Where(b => b is not null).Select(b => b!).ToList();
    }
}

/// <summary>Клієнт Сервісу відгуків (Лаб. 04: MongoDB + Clean Architecture).</summary>
public sealed class ReviewsClient(HttpClient http, ILogger<ReviewsClient> logger)
    : ServiceClientBase(http, logger), IReviewsClient
{
    /// <summary>Останні відгуки на фільм.</summary>
    public async Task<IReadOnlyList<ReviewDto>> GetForMovieAsync(
        long movieId, int limit, CancellationToken ct)
    {
        var page = await GetOrDefaultAsync<PagedResult<ReviewDto>>(
            $"/api/reviews?movieId={movieId}&page=1&pageSize={limit}", ct);

        return page?.Items ?? [];
    }

    /// <summary>Відгуки на кілька фільмів — запити виконуються одночасно.</summary>
    public async Task<IReadOnlyList<ReviewDto>> GetForMoviesAsync(
        IEnumerable<long> movieIds, int limitPerMovie, CancellationToken ct)
    {
        var batches = await Task.WhenAll(
            movieIds.Select(id => GetForMovieAsync(id, limitPerMovie, ct)));

        return batches.SelectMany(b => b).ToList();
    }

    /// <summary>
    /// Зведені рейтинги фільмів — на боці MongoDB це aggregation pipeline (Лаб. 04).
    /// </summary>
    public async Task<IReadOnlyList<MovieRatingDto>> GetRatingsAsync(
        IEnumerable<long> movieIds, CancellationToken ct)
    {
        var all = await GetOrDefaultAsync<List<MovieRatingDto>>("/api/reviews/ratings", ct)
                  ?? [];

        var wanted = movieIds.ToHashSet();
        return all.Where(r => wanted.Contains(r.MovieId)).ToList();
    }
}

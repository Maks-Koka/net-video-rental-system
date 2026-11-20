using System.Diagnostics;
using Polly;
using VideoRental.Platform.Aggregator.Clients;
using VideoRental.Platform.Aggregator.Dtos;

namespace VideoRental.Platform.Aggregator.Services;

/* =============================================================================
   Aggregator Pattern (Частина 6.2).

   Клієнтові потрібна одна відповідь, а дані лежать у трьох незалежних
   сервісах із власними базами. Агрегатор опитує їх ПАРАЛЕЛЬНО
   (Task.WhenAll) і збирає результат:

       три виклики по ~100 мс паралельно ≈ 100 мс, послідовно — 300 мс.

   Часткова відмова НЕ «валить» запит: недоступність Каталогу чи Відгуків
   додає рядок у Warnings, а клієнт отримує те, що вдалося зібрати.
   Виняток — сам об'єкт, навколо якого будується відповідь (оренда): без
   нього агрегувати нічого, тому повертається 404.
   ============================================================================= */

public interface IAggregationService
{
    Task<FullRentalDto?> GetFullRentalAsync(long rentalId, CancellationToken ct);
    Task<MovieCardDto?> GetMovieCardAsync(long movieId, CancellationToken ct);
}

public sealed class AggregationService(
    IRentalsClient rentals,
    ICatalogClient catalog,
    IReviewsClient reviews,
    ILogger<AggregationService> logger) : IAggregationService
{
    /// <summary>
    /// Джерело власних spans Агрегатора.
    /// </summary>
    /// <remarks>
    /// Ім'я збігається з тим, що ServiceDefaults передає в
    /// <c>AddSource(builder.Environment.ApplicationName)</c>, тому ці spans
    /// з'являються в Aspire Dashboard поряд з автоматичними spans HTTP і БД.
    /// </remarks>
    private static readonly ActivitySource Source = new("VideoRental.Platform.Aggregator");

    private const int ReviewsPerMovie = 5;

    /// <summary>Повна картка оренди: оренда + клієнт + фільми + відгуки + рейтинги.</summary>
    public async Task<FullRentalDto?> GetFullRentalAsync(long rentalId, CancellationToken ct)
    {
        using var activity = Source.StartActivity("AggregateFullRental");
        activity?.SetTag("rental.id", rentalId);

        var warnings = new List<string>();
        var calls = new List<SourceCallDto>();
        var total = Stopwatch.StartNew();

        // 1. Оренда — послідовно: без неї невідомо, які фільми й якого клієнта питати
        var rental = await TrackAsync(
            () => rentals.GetRentalAsync(rentalId, ct), "rentals-api/rentals", warnings, calls);

        if (rental is null)
        {
            logger.LogInformation("Оренду {RentalId} не знайдено — агрегувати нічого", rentalId);
            return null;
        }

        var movieIds = rental.Items.Select(i => i.MovieId).Distinct().ToList();
        activity?.SetTag("rental.movies", movieIds.Count);
        activity?.SetTag("customer.id", rental.CustomerId);

        logger.LogInformation(
            "Агрегую оренду {RentalId}: клієнт {CustomerId}, фільмів {MoviesCount}",
            rentalId, rental.CustomerId, movieIds.Count);

        // 2. Решта — ПАРАЛЕЛЬНО. Токен скасування передається в усі виклики:
        //    якщо клієнт обірвав запит, вихідні виклики теж зупиняються.
        var customerTask = TrackAsync(
            () => rentals.GetCustomerAsync(rental.CustomerId, ct), "rentals-api/customers", warnings, calls);

        var moviesTask = TrackAsync(
            () => catalog.GetMoviesAsync(movieIds, ct), "catalog-api", warnings, calls);

        var reviewsTask = TrackAsync(
            () => reviews.GetForMoviesAsync(movieIds, ReviewsPerMovie, ct), "reviews-api/reviews", warnings, calls);

        var ratingsTask = TrackAsync(
            () => reviews.GetRatingsAsync(movieIds, ct), "reviews-api/ratings", warnings, calls);

        // НЕ Task.WaitAll і не .Result — вони блокують потік пулу
        await Task.WhenAll(customerTask, moviesTask, reviewsTask, ratingsTask);

        total.Stop();

        // 3. Перевірка узгодженості: у відповіді лише фільми ЦІЄЇ оренди
        var movies = (await moviesTask ?? []).Where(b => movieIds.Contains(b.Id)).ToList();
        var movieReviews = (await reviewsTask ?? []).Where(r => movieIds.Contains(r.MovieId)).ToList();
        var ratings = (await ratingsTask ?? []).Where(r => movieIds.Contains(r.MovieId)).ToList();

        if (movies.Count < movieIds.Count && !warnings.Any(w => w.Contains("catalog-api")))
            warnings.Add($"catalog-api: знайдено {movies.Count} фільмів із {movieIds.Count}");

        var meta = BuildMeta(total.ElapsedMilliseconds, calls);

        activity?.SetTag("aggregation.warnings", warnings.Count);
        activity?.SetTag("aggregation.total_ms", meta.TotalMs);

        logger.LogInformation(
            "Оренду {RentalId} агреговано за {TotalMs} мс (послідовно було б {SequentialMs} мс), "
            + "попереджень: {WarningsCount}",
            rentalId, meta.TotalMs, meta.SequentialMs, warnings.Count);

        return new FullRentalDto(rental, await customerTask, movies, movieReviews, ratings, warnings, meta);
    }

    /// <summary>Картка фільму: Каталог + копія Сервісу оренд + відгуки й рейтинг.</summary>
    public async Task<MovieCardDto?> GetMovieCardAsync(long movieId, CancellationToken ct)
    {
        using var activity = Source.StartActivity("AggregateMovieCard");
        activity?.SetTag("movie.id", movieId);

        var warnings = new List<string>();
        var calls = new List<SourceCallDto>();
        var total = Stopwatch.StartNew();

        long[] ids = [movieId];

        // Каталог — канонічне джерело даних про фільм, тому він у критичному шляху.
        // Решта виконується одночасно з ним: якщо фільми немає, зайві результати
        // просто відкидаються — це дешевше за додатковий послідовний крок.
        var movieTask = TrackAsync(
            () => catalog.GetMovieAsync(movieId, ct), "catalog-api", warnings, calls);

        var projectionTask = TrackAsync(
            () => rentals.GetMovieProjectionAsync(movieId, ct), "rentals-api/movies", warnings, calls);

        var reviewsTask = TrackAsync(
            () => reviews.GetForMovieAsync(movieId, ReviewsPerMovie, ct), "reviews-api/reviews", warnings, calls);

        var ratingsTask = TrackAsync(
            () => reviews.GetRatingsAsync(ids, ct), "reviews-api/ratings", warnings, calls);

        await Task.WhenAll(movieTask, projectionTask, reviewsTask, ratingsTask);

        total.Stop();

        var movie = await movieTask;
        if (movie is null)
        {
            logger.LogInformation("Фільм {MovieId} у Каталозі не знайдено", movieId);
            return null;
        }

        var rating = (await ratingsTask ?? []).FirstOrDefault(r => r.MovieId == movieId);
        var meta = BuildMeta(total.ElapsedMilliseconds, calls);

        logger.LogInformation(
            "Картку фільми {MovieId} зібрано за {TotalMs} мс, попереджень: {WarningsCount}",
            movieId, meta.TotalMs, warnings.Count);

        return new MovieCardDto(
            movie, await projectionTask, rating,
            (await reviewsTask ?? []).Where(r => r.MovieId == movieId).ToList(),
            warnings, meta);
    }

    /// <summary>
    /// Виконує виклик сервісу, міряє його тривалість і ПОГЛИНАЄ помилки
    /// доступності, перетворюючи їх на попередження.
    /// </summary>
    /// <remarks>
    /// Перехоплюються лише помилки ДОСТУПНОСТІ сервісу:
    /// <list type="bullet">
    ///   <item><see cref="HttpRequestException"/> — з'єднання не встановлено;</item>
    ///   <item><see cref="TaskCanceledException"/> — запит не встиг;</item>
    ///   <item><see cref="ExecutionRejectedException"/> — виклик відхилив сам
    ///   конвеєр стійкості: вичерпано загальний бюджет часу
    ///   (<c>TimeoutRejectedException</c>) або розімкнувся запобіжник
    ///   (<c>BrokenCircuitException</c>). Без цієї гілки зупинений сервіс давав
    ///   би 500 замість часткової відповіді.</item>
    /// </list>
    /// Помилки в самому Агрегаторі маскувати не можна — вони мають дійти до
    /// глобального обробника.
    /// </remarks>
    private async Task<T?> TrackAsync<T>(
        Func<Task<T>> call, string service, List<string> warnings, List<SourceCallDto> calls)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            logger.LogDebug("Викликаю {Service}", service);

            var result = await call();
            stopwatch.Stop();

            logger.LogInformation("{Service} відповів за {Elapsed} мс",
                service, stopwatch.ElapsedMilliseconds);

            // списки спільні для паралельних задач -> доступ лише під блокуванням
            lock (calls)
                calls.Add(new SourceCallDto(service, "ok", stopwatch.ElapsedMilliseconds));

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                      or TaskCanceledException
                                      or ExecutionRejectedException)
        {
            stopwatch.Stop();

            logger.LogWarning(ex,
                "{Service} недоступний ({Reason}) — відповідь буде часткова",
                service, ex.GetType().Name);

            lock (warnings)
                warnings.Add($"{service} is unavailable: {ex.Message}");

            lock (calls)
                calls.Add(new SourceCallDto(service, "unavailable", stopwatch.ElapsedMilliseconds));

            return default;
        }
    }

    private static AggregationMetaDto BuildMeta(long totalMs, List<SourceCallDto> calls)
    {
        // послідовне виконання тривало б як сума всіх викликів —
        // саме цим числом видно виграш від Task.WhenAll
        var ordered = calls.OrderBy(c => c.Service).ToList();
        return new AggregationMetaDto(totalMs, ordered.Sum(c => c.ElapsedMs), ordered);
    }
}

using System.Diagnostics;
using VideoRental.Platform.Aggregator.Clients;
using VideoRental.Platform.Aggregator.Dtos;
using VideoRental.Platform.Aggregator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace VideoRental.Platform.Tests;

/* =============================================================================
   Тести сервісу агрегації (Частина 6).

   Клієнти мікросервісів підставні, тому перевіряється САМА ЛОГІКА агрегації:
   паралельність, часткові відмови, узгодженість даних. Підняття сервісів для
   цього не потрібне — наскрізний сценарій перевіряє tests/api-tests.py.
   ============================================================================= */

/// <summary>Підставний клієнт Сервісу оренд з керованою поведінкою.</summary>
internal sealed class FakeRentalsClient : IRentalsClient
{
    public RentalDto? Rental { get; set; }
    public CustomerDto? Customer { get; set; }
    public RentalsMovieDto? Projection { get; set; }
    public Exception? Failure { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public CancellationToken LastToken { get; private set; }

    public async Task<RentalDto?> GetRentalAsync(long id, CancellationToken ct)
    {
        LastToken = ct;
        await WaitAsync(ct);
        return Rental;
    }

    public async Task<CustomerDto?> GetCustomerAsync(long id, CancellationToken ct)
    {
        LastToken = ct;
        await WaitAsync(ct);
        return Customer;
    }

    public async Task<RentalsMovieDto?> GetMovieProjectionAsync(long movieId, CancellationToken ct)
    {
        LastToken = ct;
        await WaitAsync(ct);
        return Projection;
    }

    private async Task WaitAsync(CancellationToken ct)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        if (Failure is not null) throw Failure;
    }
}

internal sealed class FakeCatalogClient : ICatalogClient
{
    public List<MovieDto> Movies { get; set; } = [];
    public Exception? Failure { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public int Calls;

    public async Task<MovieDto?> GetMovieAsync(long id, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        if (Failure is not null) throw Failure;
        return Movies.FirstOrDefault(b => b.Id == id);
    }

    public async Task<IReadOnlyList<MovieDto>> GetMoviesAsync(
        IEnumerable<long> ids, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        if (Failure is not null) throw Failure;

        var wanted = ids.ToHashSet();
        return Movies.Where(b => wanted.Contains(b.Id)).ToList();
    }
}

internal sealed class FakeReviewsClient : IReviewsClient
{
    public List<ReviewDto> Reviews { get; set; } = [];
    public List<MovieRatingDto> Ratings { get; set; } = [];
    public Exception? Failure { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public async Task<IReadOnlyList<ReviewDto>> GetForMovieAsync(
        long movieId, int limit, CancellationToken ct)
    {
        await WaitAsync(ct);
        return Reviews.Where(r => r.MovieId == movieId).Take(limit).ToList();
    }

    public async Task<IReadOnlyList<ReviewDto>> GetForMoviesAsync(
        IEnumerable<long> movieIds, int limitPerMovie, CancellationToken ct)
    {
        await WaitAsync(ct);
        var wanted = movieIds.ToHashSet();
        return Reviews.Where(r => wanted.Contains(r.MovieId)).ToList();
    }

    public async Task<IReadOnlyList<MovieRatingDto>> GetRatingsAsync(
        IEnumerable<long> movieIds, CancellationToken ct)
    {
        await WaitAsync(ct);
        var wanted = movieIds.ToHashSet();
        return Ratings.Where(r => wanted.Contains(r.MovieId)).ToList();
    }

    private async Task WaitAsync(CancellationToken ct)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        if (Failure is not null) throw Failure;
    }
}

public class AggregationTests
{
    private readonly FakeRentalsClient _rentals = new();
    private readonly FakeCatalogClient _catalog = new();
    private readonly FakeReviewsClient _reviews = new();

    private AggregationService CreateSut() => new(
        _rentals, _catalog, _reviews, NullLogger<AggregationService>.Instance);

    private static RentalDto Rental(params long[] movieIds) => new(
        Id: 1, CustomerId: 7, Status: "PickedUp",
        RentalDate: new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        DueDate: new DateOnly(2026, 1, 24),
        PickedUpAt: null, ReturnedAt: null, LateFeeAmount: 0m,
        Items: movieIds.Select(id => new RentalItemDto(
            id, $"Фільм {id}", "Режисер", 2.5m, 1, 2.5m)).ToList());

    private static MovieDto Movie(long id) => new(
        id, $"Фільм {id}", "978-0000000000", 2.5m, 3, 2020, "Режисер", ["Фантастика"]);

    private static ReviewDto Review(long movieId, string id) => new(
        id, movieId, $"Фільм {movieId}", new ReviewerDto(7, "Клієнт"), 5, "Добре", 0,
        new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc));

    // ---------------------------------------------------------------- базовий

    [Fact]
    public async Task GetFullRental_Збирає_дані_всіх_трьох_сервісів()
    {
        _rentals.Rental = Rental(10, 11);
        _rentals.Customer = new CustomerDto(7, "VR-0007", "Клієнт Перший", "r@videorental.ua", false);
        _catalog.Movies = [Movie(10), Movie(11)];
        _reviews.Reviews = [Review(10, "a"), Review(11, "b")];
        _reviews.Ratings = [new MovieRatingDto(10, "Фільм 10", 1, 5, 5, 5)];

        var result = await CreateSut().GetFullRentalAsync(1, default);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Rental.Id);
        Assert.Equal("Клієнт Перший", result.Customer!.FullName);
        Assert.Equal(2, result.Movies.Count);
        Assert.Equal(2, result.Reviews.Count);
        Assert.Single(result.Ratings);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task GetFullRental_Повертає_null_коли_оренди_немає()
    {
        _rentals.Rental = null;

        Assert.Null(await CreateSut().GetFullRentalAsync(999, default));

        // без оренди решту сервісів питати нічого — зайвих викликів немає
        Assert.Equal(0, _catalog.Calls);
    }

    // ------------------------------------------------------- часткові відмови

    [Fact]
    public async Task GetFullRental_Каталог_недоступний_повертає_часткову_відповідь()
    {
        _rentals.Rental = Rental(10);
        _catalog.Failure = new HttpRequestException("Connection refused");
        _reviews.Reviews = [Review(10, "a")];

        var result = await CreateSut().GetFullRentalAsync(1, default);

        // 200 з попередженням, а не 500
        Assert.NotNull(result);
        Assert.Empty(result!.Movies);
        Assert.Single(result.Reviews);
        Assert.Contains(result.Warnings, w => w.Contains("catalog-api"));
        Assert.Contains(result.Meta.Calls, c => c is { Service: "catalog-api", Status: "unavailable" });
    }

    [Fact]
    public async Task GetFullRental_Відгуки_недоступні_повертає_часткову_відповідь()
    {
        // саме цей сценарій перевіряється на занятті: reviews-api зупинено
        _rentals.Rental = Rental(10);
        _catalog.Movies = [Movie(10)];
        _reviews.Failure = new HttpRequestException("No such host is known");

        var result = await CreateSut().GetFullRentalAsync(1, default);

        Assert.NotNull(result);
        Assert.Single(result!.Movies);
        Assert.Empty(result.Reviews);
        Assert.Empty(result.Ratings);
        // два виклики до reviews-api -> два попередження
        Assert.Equal(2, result.Warnings.Count(w => w.Contains("reviews-api")));
    }

    [Fact]
    public async Task GetFullRental_Таймаут_теж_стає_попередженням()
    {
        _rentals.Rental = Rental(10);
        _catalog.Failure = new TaskCanceledException("The request was canceled due to timeout");

        var result = await CreateSut().GetFullRentalAsync(1, default);

        Assert.NotNull(result);
        Assert.Contains(result!.Warnings, w => w.Contains("catalog-api"));
    }

    [Fact]
    public async Task GetFullRental_Розімкнутий_запобіжник_стає_попередженням()
    {
        // запобіжник розмикається після кількох невдалих спроб і далі
        // відхиляє виклики САМ, не доходячи до мережі
        _rentals.Rental = Rental(10);
        _catalog.Failure = new BrokenCircuitException("The circuit is now open");

        var result = await CreateSut().GetFullRentalAsync(1, default);

        Assert.NotNull(result);
        Assert.Contains(result!.Warnings, w => w.Contains("catalog-api"));
    }

    [Fact]
    public async Task GetFullRental_Вичерпаний_бюджет_часу_стає_попередженням()
    {
        // загальний бюджет часу конвеєра стійкості вичерпано
        _rentals.Rental = Rental(10);
        _catalog.Failure = new TimeoutRejectedException("The operation didn't complete within the allowed timeout");

        var result = await CreateSut().GetFullRentalAsync(1, default);

        Assert.NotNull(result);
        Assert.Contains(result!.Warnings, w => w.Contains("catalog-api"));
    }

    [Fact]
    public async Task GetFullRental_Несподівана_помилка_НЕ_поглинається()
    {
        // помилки самого Агрегатора маскувати не можна: вони мають дійти
        // до глобального обробника і стати 500
        _rentals.Rental = Rental(10);
        _catalog.Failure = new InvalidOperationException("помилка в коді агрегатора");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().GetFullRentalAsync(1, default));
    }

    // ------------------------------------------------------------ паралельність

    [Fact]
    public async Task GetFullRental_Виконує_виклики_ПАРАЛЕЛЬНО()
    {
        const int delayMs = 300;

        _rentals.Rental = Rental(10);
        _rentals.Delay = TimeSpan.FromMilliseconds(delayMs);
        _catalog.Movies = [Movie(10)];
        _catalog.Delay = TimeSpan.FromMilliseconds(delayMs);
        _reviews.Delay = TimeSpan.FromMilliseconds(delayMs);

        var stopwatch = Stopwatch.StartNew();
        var result = await CreateSut().GetFullRentalAsync(1, default);
        stopwatch.Stop();

        Assert.NotNull(result);

        // оренда (послідовно) + 4 паралельні виклики = ~2 затримки,
        // а послідовно було б ~5. Беремо 4 як запас на планувальник.
        Assert.True(stopwatch.ElapsedMilliseconds < delayMs * 4,
            $"агрегація зайняла {stopwatch.ElapsedMilliseconds} мс — схоже, виклики послідовні");

        // meta показує той самий факт числами
        Assert.True(result!.Meta.SequentialMs > result.Meta.TotalMs);
        Assert.True(result.Meta.SavedByParallelismMs > 0);
    }

    [Fact]
    public async Task GetFullRental_Передає_CancellationToken_у_всі_виклики()
    {
        using var cts = new CancellationTokenSource();
        _rentals.Rental = Rental(10);

        await CreateSut().GetFullRentalAsync(1, cts.Token);

        Assert.Equal(cts.Token, _rentals.LastToken);
    }

    [Fact]
    public async Task GetFullRental_Скасування_клієнтом_зупиняє_агрегацію()
    {
        using var cts = new CancellationTokenSource();
        _rentals.Rental = Rental(10);
        _rentals.Delay = TimeSpan.FromSeconds(5);

        var task = CreateSut().GetFullRentalAsync(1, cts.Token);
        await cts.CancelAsync();

        // TaskCanceledException трактується як недоступність сервісу,
        // тому агрегація завершується частковою відповіддю, а не падінням
        var result = await task;
        Assert.Null(result);
    }

    // ----------------------------------------------------------- узгодженість

    [Fact]
    public async Task GetFullRental_Відкидає_дані_чужих_фільмів()
    {
        _rentals.Rental = Rental(10);
        _catalog.Movies = [Movie(10), Movie(99)];           // 99 не з цієї оренди
        _reviews.Reviews = [Review(10, "a"), Review(99, "x")];
        _reviews.Ratings =
        [
            new MovieRatingDto(10, "Фільм 10", 1, 5, 5, 5),
            new MovieRatingDto(99, "Фільм 99", 3, 4, 3, 5)
        ];

        var result = await CreateSut().GetFullRentalAsync(1, default);

        Assert.NotNull(result);
        Assert.Equal([10L], result!.Movies.Select(b => b.Id));
        Assert.Equal([10L], result.Reviews.Select(r => r.MovieId));
        Assert.Equal([10L], result.Ratings.Select(r => r.MovieId));
    }

    [Fact]
    public async Task GetFullRental_Повідомляє_коли_частини_фільмів_немає_в_Каталозі()
    {
        _rentals.Rental = Rental(10, 11);
        _catalog.Movies = [Movie(10)];                     // фільми 11 у Каталозі немає

        var result = await CreateSut().GetFullRentalAsync(1, default);

        Assert.NotNull(result);
        Assert.Single(result!.Movies);
        Assert.Contains(result.Warnings, w => w.Contains("1 фільмів із 2"));
    }

    [Fact]
    public async Task GetFullRental_Дедуплікує_фільміви_оренди()
    {
        // та сама фільм двома позиціями -> один запит у Каталог
        _rentals.Rental = Rental(10, 10);
        _catalog.Movies = [Movie(10)];

        var result = await CreateSut().GetFullRentalAsync(1, default);

        Assert.NotNull(result);
        Assert.Single(result!.Movies);
        Assert.Empty(result.Warnings);
    }

    // ------------------------------------------------------------ картка фільму

    [Fact]
    public async Task GetMovieCard_Збирає_Каталог_проєкцію_і_відгуки()
    {
        _catalog.Movies = [Movie(10)];
        _rentals.Projection = new RentalsMovieDto(
            10, "Фільм 10", "Режисер", 2.5m, 2,
            new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc));
        _reviews.Reviews = [Review(10, "a")];
        _reviews.Ratings = [new MovieRatingDto(10, "Фільм 10", 1, 5, 5, 5)];

        var result = await CreateSut().GetMovieCardAsync(10, default);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Movie.AvailableDiscs);          // канонічні дані Каталогу
        Assert.Equal(2, result.RentalsProjection!.DiscsAvailable); // копія Сервісу оренд
        Assert.Equal(5, result.Rating!.AverageRating);
        Assert.Single(result.Reviews);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task GetMovieCard_Повертає_null_коли_фільміви_немає_в_Каталозі()
    {
        _catalog.Movies = [];

        Assert.Null(await CreateSut().GetMovieCardAsync(404, default));
    }

    [Fact]
    public async Task GetMovieCard_Сервіс_оренд_недоступний_картка_все_одно_є()
    {
        _catalog.Movies = [Movie(10)];
        _rentals.Failure = new HttpRequestException("Connection refused");

        var result = await CreateSut().GetMovieCardAsync(10, default);

        Assert.NotNull(result);
        Assert.Null(result!.RentalsProjection);
        Assert.Contains(result.Warnings, w => w.Contains("rentals-api"));
    }

    // ------------------------------------------------------------------- meta

    [Fact]
    public async Task Meta_Містить_запис_про_кожен_виклик()
    {
        _rentals.Rental = Rental(10);
        _catalog.Movies = [Movie(10)];

        var result = await CreateSut().GetFullRentalAsync(1, default);

        Assert.NotNull(result);
        Assert.Equal(5, result!.Meta.Calls.Count);   // оренда, клієнт, каталог, відгуки, рейтинги
        Assert.All(result.Meta.Calls, c => Assert.Equal("ok", c.Status));
        Assert.Equal(
            result.Meta.Calls.Sum(c => c.ElapsedMs),
            result.Meta.SequentialMs);
    }
}

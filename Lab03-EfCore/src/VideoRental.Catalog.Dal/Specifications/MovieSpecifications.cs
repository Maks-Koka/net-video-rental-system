using Ardalis.Specification;
using VideoRental.Catalog.Domain.Entities;

namespace VideoRental.Catalog.Dal.Specifications;

/// <summary>
/// Параметри запиту списку фільмів. Використовуються і в API (biding з query-рядка),
/// і в специфікації.
/// </summary>
public sealed record MovieQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    decimal? MinRate = null,
    decimal? MaxRate = null,
    int? DirectorId = null,
    string? Genre = null,
    bool? OnlyAvailable = null,
    string SortBy = "title",
    string SortDir = "asc")
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    /// <summary>Розмір хвилини, обмежений зверху й знизу.</summary>
    public int SafePageSize => Math.Clamp(PageSize <= 0 ? DefaultPageSize : PageSize, 1, MaxPageSize);

    public int SafePage => Page < 1 ? 1 : Page;

    /// <summary>Дозволені поля сортування — захист від довільного вводу.</summary>
    public static readonly IReadOnlyCollection<string> AllowedSortFields =
        new[] { "title", "fee", "year", "discs" };
}

/// <summary>
/// Специфікація списку фільмів: фільтрація, сортування та посторінковість.
/// </summary>
/// <remarks>
/// <para>
/// Специфікація описує КРИТЕРІЇ вибірки окремо від репозиторію й не залежить
/// від EF, тому її можна тестувати ізольовано. Репозиторій лише приймає її як
/// параметр, інтерпретує (<c>WithSpecification</c>) і будує LINQ-вираз.
/// </para>
/// <para>
/// <b>Важливо:</b> усе — <c>Where</c>, <c>OrderBy</c>, <c>Skip</c>/<c>Take</c> —
/// потрапляє в один SQL-запит і виконується на рівні БД (<c>OFFSET … FETCH</c>).
/// Повні таблиці в памʼять не завантажуються.
/// </para>
/// </remarks>
public sealed class MoviesFilterSpec : Specification<Movie>
{
    public MoviesFilterSpec(MovieQuery q, bool applyPaging = true)
    {
        Query.Where(b => !b.IsDeleted);

        Query.Include(b => b.Director);
        Query.Include(b => b.MovieGenres).ThenInclude(bg => bg.Genre);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim();
            Query.Where(b => b.Title.Contains(search) || b.Barcode.Contains(search));
        }

        if (q.MinRate is not null)
            Query.Where(b => b.DailyRate >= q.MinRate);

        if (q.MaxRate is not null)
            Query.Where(b => b.DailyRate <= q.MaxRate);

        if (q.DirectorId is not null)
            Query.Where(b => b.DirectorId == q.DirectorId);

        if (!string.IsNullOrWhiteSpace(q.Genre))
        {
            var genre = q.Genre.Trim();
            Query.Where(b => b.MovieGenres.Any(bg => bg.Genre.Name == genre || bg.Genre.Slug == genre));
        }

        if (q.OnlyAvailable == true)
            Query.Where(b => b.AvailableDiscs > 0);

        // сортування лише за дозволеними полями
        var desc = string.Equals(q.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        switch (q.SortBy?.ToLowerInvariant())
        {
            case "fee":
                if (desc) Query.OrderByDescending(b => b.DailyRate); else Query.OrderBy(b => b.DailyRate);
                break;
            case "year":
                if (desc) Query.OrderByDescending(b => b.ReleaseYear); else Query.OrderBy(b => b.ReleaseYear);
                break;
            case "discs":
                if (desc) Query.OrderByDescending(b => b.AvailableDiscs); else Query.OrderBy(b => b.AvailableDiscs);
                break;
            default:
                if (desc) Query.OrderByDescending(b => b.Title); else Query.OrderBy(b => b.Title);
                break;
        }

        if (applyPaging)
            Query.Skip((q.SafePage - 1) * q.SafePageSize).Take(q.SafePageSize);

        Query.AsNoTracking();
    }
}

/// <summary>
/// ДРУГА специфікація з іншими критеріями — доводить, що репозиторій
/// працює з будь-якою специфікацією без змін у своєму коді.
/// </summary>
public sealed class MoviesByGenreSlugSpec : Specification<Movie>
{
    public MoviesByGenreSlugSpec(string genreSlug, int take = 10)
    {
        Query.Where(b => !b.IsDeleted && b.MovieGenres.Any(bg => bg.Genre.Slug == genreSlug))
             .Include(b => b.Director)
             .OrderByDescending(b => b.AvailableDiscs)
             .Take(take)
             .AsNoTracking();
    }
}

/// <summary>Специфікація «фільми, яких немає в наявності» — для звіту комплектування.</summary>
public sealed class OutOfStockMoviesSpec : Specification<Movie>
{
    public OutOfStockMoviesSpec()
    {
        Query.Where(b => !b.IsDeleted && b.AvailableDiscs == 0)
             .Include(b => b.Director)
             .OrderBy(b => b.Title)
             .AsNoTracking();
    }
}

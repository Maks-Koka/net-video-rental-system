using VideoRental.Catalog.Bll.Dtos;
using VideoRental.Catalog.Dal.Specifications;

namespace VideoRental.Catalog.Bll.Abstractions;

/// <summary>Бізнес-операції над фільмами каталогу.</summary>
public interface IMovieService
{
    /// <summary>Список фільмів із пагінацією, фільтрацією та сортуванням на рівні БД.</summary>
    Task<PagedResult<MovieListItemDto>> GetListAsync(MovieQuery query, CancellationToken ct = default);

    /// <summary>Картка фільму — Eager Loading (один SQL-запит).</summary>
    Task<MovieDto> GetByIdAsync(long id, CancellationToken ct = default);

    Task<MovieDto> CreateAsync(CreateMovieDto input, CancellationToken ct = default);

    Task<MovieDto> UpdateAsync(long id, UpdateMovieDto input, CancellationToken ct = default);

    /// <summary>Списання дисків. Інваріант перевіряє сама сутність.</summary>
    Task<MovieDto> RemoveStockAsync(long id, ChangeStockDto input, CancellationToken ct = default);

    Task<MovieDto> AddStockAsync(long id, ChangeStockDto input, CancellationToken ct = default);

    Task<MovieDto> AddDiscAsync(long id, AddDiscDto input, CancellationToken ct = default);

    Task<MovieDto> AssignGenreAsync(long id, AssignGenreDto input, CancellationToken ct = default);

    /// <summary>М'яке видалення фільму.</summary>
    Task DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>Фільми, яких немає в наявності (друга специфікація).</summary>
    Task<IReadOnlyList<MovieListItemDto>> GetOutOfStockAsync(CancellationToken ct = default);

    /// <summary>Топ фільмів жанру за slug (третя специфікація).</summary>
    Task<IReadOnlyList<MovieListItemDto>> GetTopByGenreSlugAsync(
        string slug, int take, CancellationToken ct = default);
}

/// <summary>Бізнес-операції над режисерами.</summary>
public interface IDirectorService
{
    Task<IReadOnlyList<DirectorDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Режисер із фільмами — Explicit Loading (два SQL-запити при loadMovies=true).</summary>
    Task<DirectorWithMoviesDto> GetByIdAsync(int id, bool includeMovies, CancellationToken ct = default);

    Task<DirectorDto> CreateAsync(CreateDirectorDto input, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}

/// <summary>Бізнес-операції над жанрами та зв'язками M:N.</summary>
public interface IGenreService
{
    Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Скільки фільмів у кожному жанрі — агрегат рахує БД.</summary>
    Task<IReadOnlyList<GenreStatsDto>> GetStatsAsync(CancellationToken ct = default);

    /// <summary>
    /// «Нові фільми жанру за період» — LINQ-запит через проміжну сутність M:N
    /// з використанням її власного поля <c>AddedAt</c>.
    /// </summary>
    Task<IReadOnlyList<MovieListItemDto>> GetRecentlyAddedAsync(
        string genreName, DateTime since, CancellationToken ct = default);
}

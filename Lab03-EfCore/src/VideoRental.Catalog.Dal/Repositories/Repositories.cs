using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using VideoRental.Catalog.Dal.Abstractions;
using VideoRental.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace VideoRental.Catalog.Dal.Repositories;

/// <summary>
/// Базова реалізація асинхронного узагальненого репозиторію на EF Core.
/// </summary>
/// <remarks>
/// Читання за замовчуванням виконуються з <c>AsNoTracking()</c>: сутності,
/// які ми лише показуємо, не потрібно відстежувати — це економить памʼять
/// і час на побудову снапшотів змін.
/// </remarks>
public class GenericRepository<T> : IGenericRepository<T> where T : class
{
    protected readonly CatalogDbContext Context;
    protected readonly DbSet<T> Set;

    public GenericRepository(CatalogDbContext context)
    {
        Context = context;
        Set = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(object id, CancellationToken ct = default)
        => await Set.FindAsync([id], ct);

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => await Set.AsNoTracking().ToListAsync(ct);

    public virtual async Task AddAsync(T entity, CancellationToken ct = default)
        => await Set.AddAsync(entity, ct);

    // Update і Delete лише міняють стан у контексті — до БД не звертаються,
    // тому синхронні. У базу все піде одним SaveChangesAsync.
    public virtual void Update(T entity) => Set.Update(entity);

    public virtual void Delete(T entity) => Set.Remove(entity);

    public virtual Task<int> SaveChangesAsync(CancellationToken ct = default)
        => Context.SaveChangesAsync(ct);

    /// <summary>
    /// Застосовує специфікацію (Ardalis) до <c>DbSet</c> і виконує ОДИН запит.
    /// Репозиторій лише інтерпретує критерії — він про них нічого не знає.
    /// </summary>
    public virtual async Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T> specification, CancellationToken ct = default)
        => await Set.WithSpecification(specification).ToListAsync(ct);

    /// <summary>
    /// Кількість записів за критеріями специфікації БЕЗ пагінації:
    /// потрібна для <c>totalCount</c> у посторінковій відповіді.
    /// </summary>
    public virtual async Task<int> CountAsync(
        ISpecification<T> specification, CancellationToken ct = default)
        => await SpecificationEvaluator.Default
            .GetQuery(Set.AsQueryable(), specification, evaluateCriteriaOnly: true)
            .CountAsync(ct);
}

/// <summary>
/// Репозиторій фільмів — демонструє <b>EAGER LOADING</b>.
/// </summary>
public sealed class MovieRepository : GenericRepository<Movie>, IMovieRepository
{
    public MovieRepository(CatalogDbContext context) : base(context) { }

    /// <summary>
    /// <b>Eager Loading:</b> опис, диски та жанри тягнуться РАЗОМ із фільмом
    /// одним запитом (<c>Include</c> / <c>ThenInclude</c>).
    /// </summary>
    /// <remarks>
    /// У лозі SQL (Serilog, рівень Information для
    /// <c>Microsoft.EntityFrameworkCore.Database.Command</c>) видно, що цей
    /// метод виконує <b>ОДИН</b> запит — на відміну від Explicit Loading, яке
    /// виконує два. <c>AsSplitQuery()</c> не вмикаємо свідомо: завдання —
    /// показати саме один запит із JOIN-ами.
    /// </remarks>
    public Task<Movie?> GetWithDetailsAsync(long id, CancellationToken ct = default)
        => Set.Include(b => b.Director)
              .Include(b => b.Detail)
              .Include(b => b.Discs)
              .Include(b => b.MovieGenres)
                  .ThenInclude(bg => bg.Genre)
              .AsNoTracking()
              .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);

    /// <summary>Фільм з жанрами — відстежувана, бо її змінюватимуть.</summary>
    public Task<Movie?> GetWithGenresAsync(long id, CancellationToken ct = default)
        => Set.Include(b => b.MovieGenres)
              .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);

    /// <summary>
    /// Фільм РАЗОМ із дисками — відстежувана.
    /// Колекція <c>Discs</c> обовʼязкова: саме за нею <see cref="Movie.AddDisc"/>
    /// перевіряє, що інвентарний номер ще не використано.
    /// </summary>
    public Task<Movie?> GetWithDiscsAsync(long id, CancellationToken ct = default)
        => Set.Include(b => b.Discs)
              .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct);

    /// <summary>Перевірка унікальності Barcode — виконується в БД через <c>EXISTS</c>.</summary>
    public Task<bool> ExistsByBarcodeAsync(string barcode, CancellationToken ct = default)
        => Set.AnyAsync(b => b.Barcode == barcode, ct);

    public Task<bool> ExistsByBarcodeAsync(string barcode, long excludeMovieId, CancellationToken ct = default)
        => Set.AnyAsync(b => b.Barcode == barcode && b.Id != excludeMovieId, ct);

    public override async Task<IReadOnlyList<Movie>> GetAllAsync(CancellationToken ct = default)
        => await Set.Where(b => !b.IsDeleted)
                    .OrderBy(b => b.Title)
                    .AsNoTracking()
                    .ToListAsync(ct);
}

/// <summary>
/// Репозиторій режисерів — демонструє <b>EXPLICIT LOADING</b>.
/// </summary>
public sealed class DirectorRepository : GenericRepository<Director>, IDirectorRepository
{
    public DirectorRepository(CatalogDbContext context) : base(context) { }

    /// <summary>
    /// <b>Explicit Loading:</b> спочатку вантажимо лише режисера (1-й запит), а
    /// фільми догружаємо ОКРЕМИМ запитом (2-й) — і лише тоді, коли вони справді
    /// потрібні (<paramref name="loadMovies"/>).
    /// </summary>
    /// <remarks>
    /// Сутність має бути ВІДСТЕЖУВАНОЮ (без <c>AsNoTracking</c>), інакше
    /// <c>Context.Entry(...).Collection(...).LoadAsync()</c> не спрацює:
    /// explicit loading працює лише для відстежуваних сутностей.
    /// У лозі SQL видно рівно два запити, якщо <paramref name="loadMovies"/> = true,
    /// і один — якщо false.
    /// </remarks>
    public async Task<Director?> GetWithMoviesAsync(int id, bool loadMovies, CancellationToken ct = default)
    {
        var director = await Set.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, ct); // 1-й запит
        if (director is null) return null;

        if (loadMovies)
        {
            await Context.Entry(director)
                .Collection(a => a.Movies)
                .Query()
                .Where(b => !b.IsDeleted)
                .OrderBy(b => b.Title)
                .LoadAsync(ct);                                                           // 2-й запит
        }

        return director;
    }

    public Task<bool> ExistsAsync(int id, CancellationToken ct = default)
        => Set.AnyAsync(a => a.Id == id && !a.IsDeleted, ct);

    public override async Task<IReadOnlyList<Director>> GetAllAsync(CancellationToken ct = default)
        => await Set.Where(a => !a.IsDeleted)
                    .OrderBy(a => a.FullName)
                    .AsNoTracking()
                    .ToListAsync(ct);
}

/// <summary>
/// Репозиторій зв'язків «фільм–жанр» — демонструє <b>LINQ to Entities</b>
/// через проміжну сутність зв'язку M:N.
/// </summary>
public sealed class MovieGenreRepository : GenericRepository<MovieGenre>, IMovieGenreRepository
{
    public MovieGenreRepository(CatalogDbContext context) : base(context) { }

    /// <summary>
    /// «Нові фільми жанру за період». Запит іде ЧЕРЕЗ проміжну сутність і
    /// використовує її ВЛАСНЕ поле <c>AddedAt</c> — те, чого автоматичний M:N
    /// в EF Core зробити не дозволив би.
    /// </summary>
    /// <remarks>
    /// Фільтрація, сортування та проєкція виконуються на рівні БД
    /// (обслуговує індекс <c>IX_MovieGenres_GenreId_AddedAt</c>);
    /// у памʼять потрапляє лише результат.
    /// </remarks>
    public async Task<IReadOnlyList<Movie>> GetRecentlyAddedAsync(
        string genreName, DateTime since, CancellationToken ct = default)
        => await Set
            .Where(bg => bg.Genre.Name == genreName && bg.AddedAt >= since && !bg.Movie.IsDeleted)
            .OrderByDescending(bg => bg.AddedAt)
            .Select(bg => bg.Movie)
            .AsNoTracking()
            .ToListAsync(ct);

    /// <summary>Скільки фільмів у кожному жанрі. <c>GROUP BY</c> виконує БД.</summary>
    public async Task<IReadOnlyList<(int GenreId, string GenreName, int MovieCount)>> GetGenreStatsAsync(
        CancellationToken ct = default)
    {
        var rows = await Set
            .Where(bg => !bg.Movie.IsDeleted)
            .GroupBy(bg => new { bg.GenreId, bg.Genre.Name })
            .Select(g => new { g.Key.GenreId, g.Key.Name, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name)
            .AsNoTracking()
            .ToListAsync(ct);

        return rows.Select(r => (r.GenreId, r.Name, r.Count)).ToList();
    }

    public Task<MovieGenre?> FindAsync(long movieId, int genreId, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(bg => bg.MovieId == movieId && bg.GenreId == genreId, ct);
}

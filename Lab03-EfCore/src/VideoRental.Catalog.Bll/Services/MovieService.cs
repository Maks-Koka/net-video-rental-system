using AutoMapper;
using VideoRental.Catalog.Bll.Abstractions;
using VideoRental.Catalog.Bll.Dtos;
using VideoRental.Catalog.Dal.Abstractions;
using VideoRental.Catalog.Dal.Specifications;
using VideoRental.Catalog.Domain.Entities;
using VideoRental.Catalog.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace VideoRental.Catalog.Bll.Services;

/// <summary>
/// Сервіс фільмів. Працює з даними ЛИШЕ через репозиторії та Unit of Work —
/// з <c>DbContext</c> напряму не спілкується.
/// </summary>
/// <remarks>
/// <b>Розподіл відповідальності за правила:</b>
/// <list type="bullet">
///   <item>правила САМОЇ сутності (ціна &gt; 0, достатній залишок) перевіряє
///         сутність своїми методами — <c>Movie.ChangeDailyRate</c>,
///         <c>Movie.RemoveStock</c>;</item>
///   <item>правила, для яких потрібні ІНШІ дані з БД (унікальність Barcode,
///         існування режисера й жанрів), перевіряє цей сервіс.</item>
/// </list>
/// <c>SaveChangesAsync</c> викликається ОДИН раз наприкінці операції.
/// </remarks>
public sealed class MovieService : IMovieService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<MovieService> _logger;

    public MovieService(IUnitOfWork uow, IMapper mapper, ILogger<MovieService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    /// <summary>
    /// Список фільмів. Фільтрація, сортування і <c>Skip/Take</c> виконуються
    /// в LINQ-виразі ДО матеріалізації, тому база повертає лише потрібну
    /// хвилину (<c>OFFSET … FETCH</c>), а <c>totalCount</c> рахує теж БД.
    /// </summary>
    public async Task<PagedResult<MovieListItemDto>> GetListAsync(
        MovieQuery query, CancellationToken ct = default)
    {
        var pageSize = query.SafePageSize;
        var page = query.SafePage;

        // одна специфікація — для хвилини, та сама без пагінації — для лічильника
        var pagedSpec = new MoviesFilterSpec(query, applyPaging: true);
        var countSpec = new MoviesFilterSpec(query, applyPaging: false);

        var total = await _uow.Movies.CountAsync(countSpec, ct);
        var items = await _uow.Movies.ListAsync(pagedSpec, ct);

        return new PagedResult<MovieListItemDto>(
            items.Select(_mapper.Map<MovieListItemDto>).ToList(), total, page, pageSize);
    }

    /// <summary>Картка фільму — <b>Eager Loading</b>: усе пов'язане одним запитом.</summary>
    public async Task<MovieDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var movie = await _uow.Movies.GetWithDetailsAsync(id, ct)
                   ?? throw EntityNotFoundException.For("Movie", id);

        return _mapper.Map<MovieDto>(movie);
    }

    public async Task<MovieDto> CreateAsync(CreateMovieDto input, CancellationToken ct = default)
    {
        // правило, для якого потрібні інші дані з БД: унікальність Barcode
        if (await _uow.Movies.ExistsByBarcodeAsync(input.Barcode, ct))
            throw new ConflictException($"Movie with Barcode '{input.Barcode}' already exists");

        if (!await _uow.Directors.ExistsAsync(input.DirectorId, ct))
            throw EntityNotFoundException.For("Director", input.DirectorId);

        foreach (var genreId in input.GenreIds.Distinct())
            if (await _uow.Genres.GetByIdAsync(genreId, ct) is null)
                throw EntityNotFoundException.For("Genre", genreId);

        // конструктор сутності сам перевіряє свої інваріанти
        var movie = new Movie(input.Title, input.Barcode, input.DailyRate, input.DirectorId, input.ReleaseYear);

        if (!string.IsNullOrWhiteSpace(input.Description))
            movie.SetDetail(new MovieDetail(input.Description, input.Studio, input.DurationMinutes));

        var first = true;
        foreach (var genreId in input.GenreIds.Distinct())
        {
            movie.AssignGenre(genreId, isPrimary: first, addedBy: "catalog-api");
            first = false;
        }

        await _uow.Movies.AddAsync(movie, ct);
        await _uow.SaveChangesAsync(ct);           // ОДИН раз наприкінці операції

        _logger.LogInformation("Movie {MovieId} '{Title}' created", movie.Id, movie.Title);

        return await GetByIdAsync(movie.Id, ct);
    }

    public async Task<MovieDto> UpdateAsync(long id, UpdateMovieDto input, CancellationToken ct = default)
    {
        var movie = await _uow.Movies.GetByIdAsync(id, ct)
                   ?? throw EntityNotFoundException.For("Movie", id);

        // зміни проходять лише через методи сутності — setter-ів немає
        movie.Rename(input.Title);
        movie.ChangeDailyRate(input.DailyRate);

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Movie {MovieId} updated", id);
        return await GetByIdAsync(id, ct);
    }

    /// <summary>
    /// Списання дисків. Якщо залишку бракує, <see cref="Movie.RemoveStock"/>
    /// кидає <see cref="DomainException"/> → HTTP 409, і <c>SaveChangesAsync</c>
    /// навіть не викликається: у базі нічого не змінюється.
    /// </summary>
    public async Task<MovieDto> RemoveStockAsync(long id, ChangeStockDto input, CancellationToken ct = default)
    {
        var movie = await _uow.Movies.GetByIdAsync(id, ct)
                   ?? throw EntityNotFoundException.For("Movie", id);

        movie.RemoveStock(input.Quantity);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Movie {MovieId}: {Quantity} discs removed from stock", id, input.Quantity);
        return await GetByIdAsync(id, ct);
    }

    public async Task<MovieDto> AddStockAsync(long id, ChangeStockDto input, CancellationToken ct = default)
    {
        var movie = await _uow.Movies.GetByIdAsync(id, ct)
                   ?? throw EntityNotFoundException.For("Movie", id);

        movie.AddStock(input.Quantity);
        await _uow.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<MovieDto> AddDiscAsync(long id, AddDiscDto input, CancellationToken ct = default)
    {
        // фільм потрібна РАЗОМ із дисками: інваріант «інвентарний номер
        // унікальний у межах фільму» перевіряє сама сутність в AddDisc, і без
        // завантаженої колекції Discs перевірка «не побачила б» наявні рядки
        var movie = await _uow.Movies.GetWithDiscsAsync(id, ct)
                   ?? throw EntityNotFoundException.For("Movie", id);

        var copy = new DiscCopy(input.DiscNumber, input.Shelf, input.Condition, input.AcquiredAt);
        movie.AddDisc(copy);                        // + AddStock(1) всередині

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Movie {MovieId}: copy {Inventory} added", id, input.DiscNumber);
        return await GetByIdAsync(id, ct);
    }

    public async Task<MovieDto> AssignGenreAsync(long id, AssignGenreDto input, CancellationToken ct = default)
    {
        // потрібні вже призначені жанри, щоб сутність могла перевірити дублікат
        var movie = await _uow.Movies.GetWithGenresAsync(id, ct)
                   ?? throw EntityNotFoundException.For("Movie", id);

        if (await _uow.Genres.GetByIdAsync(input.GenreId, ct) is null)
            throw EntityNotFoundException.For("Genre", input.GenreId);

        movie.AssignGenre(input.GenreId, input.IsPrimary, addedBy: "catalog-api");
        await _uow.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var movie = await _uow.Movies.GetByIdAsync(id, ct)
                   ?? throw EntityNotFoundException.For("Movie", id);

        movie.MarkDeleted();                        // м'яке видалення
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Movie {MovieId} soft-deleted", id);
    }

    /// <summary>Друга специфікація через той самий <c>ListAsync</c> — без змін у репозиторії.</summary>
    public async Task<IReadOnlyList<MovieListItemDto>> GetOutOfStockAsync(CancellationToken ct = default)
    {
        var items = await _uow.Movies.ListAsync(new OutOfStockMoviesSpec(), ct);
        return items.Select(_mapper.Map<MovieListItemDto>).ToList();
    }

    /// <summary>Третя специфікація — теж без змін у репозиторії.</summary>
    public async Task<IReadOnlyList<MovieListItemDto>> GetTopByGenreSlugAsync(
        string slug, int take, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ValidationException(nameof(slug), "Slug жанру не може бути порожнім");

        var items = await _uow.Movies.ListAsync(
            new MoviesByGenreSlugSpec(slug.Trim().ToLowerInvariant(), Math.Clamp(take, 1, 50)), ct);

        return items.Select(_mapper.Map<MovieListItemDto>).ToList();
    }
}

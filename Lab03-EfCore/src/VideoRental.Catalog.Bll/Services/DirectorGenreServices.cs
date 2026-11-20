using AutoMapper;
using VideoRental.Catalog.Bll.Abstractions;
using VideoRental.Catalog.Bll.Dtos;
using VideoRental.Catalog.Dal.Abstractions;
using VideoRental.Catalog.Domain.Entities;
using VideoRental.Catalog.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace VideoRental.Catalog.Bll.Services;

/// <summary>Сервіс режисерів. Демонструє <b>Explicit Loading</b> у дії.</summary>
public sealed class DirectorService : IDirectorService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<DirectorService> _logger;

    public DirectorService(IUnitOfWork uow, IMapper mapper, ILogger<DirectorService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DirectorDto>> GetAllAsync(CancellationToken ct = default)
    {
        var directors = await _uow.Directors.GetAllAsync(ct);
        return directors.Select(_mapper.Map<DirectorDto>).ToList();
    }

    /// <summary>
    /// Режисер за ідентифікатором. Коли <paramref name="includeMovies"/> = true,
    /// фільми довантажуються ОКРЕМИМ (другим) запитом — Explicit Loading.
    /// У лозі SQL-команд EF Core це видно як два запити замість одного.
    /// </summary>
    public async Task<DirectorWithMoviesDto> GetByIdAsync(
        int id, bool includeMovies, CancellationToken ct = default)
    {
        var director = await _uow.Directors.GetWithMoviesAsync(id, includeMovies, ct)
                     ?? throw EntityNotFoundException.For("Director", id);

        return _mapper.Map<DirectorWithMoviesDto>(director);
    }

    public async Task<DirectorDto> CreateAsync(CreateDirectorDto input, CancellationToken ct = default)
    {
        var existing = await _uow.Directors.GetAllAsync(ct);
        if (existing.Any(a => string.Equals(a.FullName, input.FullName.Trim(),
                                            StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException($"Director '{input.FullName}' already exists");

        var director = new Director(input.FullName, input.Country, input.BirthYear);

        await _uow.Directors.AddAsync(director, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Director {DirectorId} '{Name}' created", director.Id, director.FullName);
        return _mapper.Map<DirectorDto>(director);
    }

    /// <summary>
    /// М'яке видалення режисера. Інваріант «режисера з фільмами видаляти не можна»
    /// перевіряє САМА сутність (<see cref="Director.MarkDeleted"/>), тому фільми
    /// треба спершу завантажити — Explicit Loading з <c>loadMovies: true</c>.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var director = await _uow.Directors.GetWithMoviesAsync(id, loadMovies: true, ct)
                     ?? throw EntityNotFoundException.For("Director", id);

        director.MarkDeleted();                      // кидає DomainException -> 409, якщо є фільми
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Director {DirectorId} soft-deleted", id);
    }
}

/// <summary>
/// Сервіс жанрів. Демонструє <b>LINQ to Entities</b> через проміжну
/// сутність зв'язку M:N та її власне поле <c>AddedAt</c>.
/// </summary>
public sealed class GenreService : IGenreService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public GenreService(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct = default)
    {
        var genres = await _uow.Genres.GetAllAsync(ct);
        return genres.Where(g => !g.IsDeleted)
                     .OrderBy(g => g.Name)
                     .Select(_mapper.Map<GenreDto>)
                     .ToList();
    }

    public async Task<IReadOnlyList<GenreStatsDto>> GetStatsAsync(CancellationToken ct = default)
    {
        var rows = await _uow.MovieGenres.GetGenreStatsAsync(ct);
        return rows.Select(r => new GenreStatsDto(r.GenreId, r.GenreName, r.MovieCount)).ToList();
    }

    /// <summary>
    /// «Нові фільми жанру за період». Запит іде через проміжну сутність
    /// <c>MovieGenre</c> і фільтрує за її ВЛАСНИМ полем <c>AddedAt</c> —
    /// автоматичний M:N в EF Core такого поля не мав би взагалі.
    /// </summary>
    public async Task<IReadOnlyList<MovieListItemDto>> GetRecentlyAddedAsync(
        string genreName, DateTime since, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(genreName))
            throw new ValidationException(nameof(genreName), "Назва жанру обовʼязкова");

        var movies = await _uow.MovieGenres.GetRecentlyAddedAsync(genreName.Trim(), since, ct);
        return movies.Select(_mapper.Map<MovieListItemDto>).ToList();
    }
}

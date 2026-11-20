using System.Data;
using AutoMapper;
using VideoRental.Rentals.Bll.Abstractions;
using VideoRental.Rentals.Bll.Dtos;
using VideoRental.Rentals.Dal.Abstractions;
using VideoRental.Rentals.Domain.Entities;
using VideoRental.Rentals.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace VideoRental.Rentals.Bll.Services;

/// <summary>
/// Сервіс локальної копії довідника фільмів.
/// </summary>
/// <remarks>
/// Цей контекст НЕ є власником даних про фільми — власник БД Каталогу
/// (Проєкт №2). Тому тут немає операцій створення чи видалення фільму:
/// є лише <see cref="SyncFromCatalogAsync"/>, яка оновлює локальну копію.
/// У Лаб. 08 цей метод викликатиметься обробником події з Каталогу.
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

    public async Task<MovieDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var movie = await _uow.Movies.GetByIdAsync(id, ct)
                   ?? throw NotFoundException.For("Movie", id);
        return _mapper.Map<MovieDto>(movie);
    }

    public async Task<IReadOnlyList<MovieDto>> SearchAsync(string? title, CancellationToken ct = default)
    {
        var movies = await _uow.Movies.SearchAsync(title, ct);
        return movies.Select(_mapper.Map<MovieDto>).ToList();
    }

    /// <summary>
    /// Оновлення локальної копії довідника даними з Каталогу.
    /// <c>DiscsAvailable</c> зберігається лише при ПЕРШІЙ вставці: далі це
    /// власні дані контексту Оренди (таблиця політики дублювання, рядок 3).
    /// </summary>
    public async Task<MovieDto> SyncFromCatalogAsync(SyncMovieDto input, CancellationToken ct = default)
    {
        var entity = _mapper.Map<Movie>(input);

        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await _uow.Movies.UpsertFromCatalogAsync(entity, ct);
            await _uow.CommitAsync(ct);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }

        _logger.LogInformation(
            "Local catalog copy for movie {MovieId} synced from Catalog service", input.Id);

        var saved = await _uow.Movies.GetByIdAsync(input.Id, ct)
                    ?? throw NotFoundException.For("Movie", input.Id);
        return _mapper.Map<MovieDto>(saved);
    }
}

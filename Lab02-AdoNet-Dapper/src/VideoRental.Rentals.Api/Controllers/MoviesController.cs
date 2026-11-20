using VideoRental.Rentals.Bll.Abstractions;
using VideoRental.Rentals.Bll.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace VideoRental.Rentals.Api.Controllers;

/// <summary>
/// Локальна копія довідника фільмів цього мікросервісу.
/// </summary>
/// <remarks>
/// Власник даних про фільми — сервіс <b>Каталогу</b> (Проєкт №2, Лаб. 03).
/// Тому тут немає створення чи видалення фільму: лише читання та
/// <c>PUT /api/movies/sync</c>, яка оновлює локальну копію. У Лаб. 08 цей
/// ендпоінт замінить обробник події <c>MovieUpdated</c> з Каталогу.
/// </remarks>
[ApiController]
[Route("api/movies")]
[Produces("application/json")]
public sealed class MoviesController : ControllerBase
{
    private readonly IMovieService _movies;

    public MoviesController(IMovieService movies) => _movies = movies;

    /// <summary>Пошук фільмів у локальній копії довідника за частиною назви.</summary>
    /// <param name="title">Частина назви; якщо не вказано — повертаються всі фільми.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MovieDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MovieDto>>> Search(
        [FromQuery] string? title, CancellationToken ct)
        => Ok(await _movies.SearchAsync(title, ct));

    /// <summary>Фільм за ідентифікатором (той самий Id, що й у БД Каталогу).</summary>
    /// <response code="404">Фільми з таким Id немає в локальній копії.</response>
    [HttpGet("{id:long}")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieDto>> GetById(long id, CancellationToken ct)
        => Ok(await _movies.GetByIdAsync(id, ct));

    /// <summary>
    /// Синхронізує локальну копію довідника даними з сервісу Каталогу.
    /// Залишок дисків (<c>discsAvailable</c>) НЕ перетирається для
    /// існуючих фільмів: це власні дані контексту Оренди.
    /// </summary>
    /// <response code="200">Копію оновлено або створено.</response>
    /// <response code="400">Некоректні вхідні дані.</response>
    [HttpPut("sync")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MovieDto>> Sync(SyncMovieDto input, CancellationToken ct)
        => Ok(await _movies.SyncFromCatalogAsync(input, ct));
}

using FluentValidation;
using VideoRental.Catalog.Bll.Abstractions;
using VideoRental.Catalog.Bll.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace VideoRental.Catalog.Api.Controllers;

/// <summary>Режисери каталогу. Демонструє <b>Explicit Loading</b>.</summary>
[ApiController]
[Route("api/directors")]
[Produces("application/json")]
public sealed class DirectorsController : ControllerBase
{
    private readonly IDirectorService _directors;

    public DirectorsController(IDirectorService directors) => _directors = directors;

    /// <summary>Перелік активних режисерів.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DirectorDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DirectorDto>>> GetAll(CancellationToken ct)
        => Ok(await _directors.GetAllAsync(ct));

    /// <summary>
    /// Режисер за ідентифікатором. Параметр <paramref name="includeMovies"/>
    /// керує <b>Explicit Loading</b>: при <c>true</c> фільми довантажуються
    /// ДРУГИМ окремим запитом, при <c>false</c> виконується лише один.
    /// </summary>
    /// <param name="id">Ідентифікатор режисера.</param>
    /// <param name="includeMovies">Чи довантажувати фільми другим запитом.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    /// <response code="404">Режисера з таким Id не існує.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType<DirectorWithMoviesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DirectorWithMoviesDto>> GetById(
        int id, [FromQuery] bool includeMovies, CancellationToken ct)
        => Ok(await _directors.GetByIdAsync(id, includeMovies, ct));

    /// <summary>Додає режисера.</summary>
    /// <response code="201">Режисера створено.</response>
    /// <response code="409">Режисер із таким імʼям уже є.</response>
    [HttpPost]
    [ProducesResponseType<DirectorDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DirectorDto>> Create(
        CreateDirectorDto input,
        [FromServices] IValidator<CreateDirectorDto> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var created = await _directors.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// М'яке видалення режисера. Інваріант «режисера з фільмами видаляти не можна»
    /// перевіряє сама сутність.
    /// </summary>
    /// <response code="204">Режисера позначено видаленим.</response>
    /// <response code="409">В режисера ще є фільми.</response>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _directors.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Жанри та статистика за зв'язком M:N.</summary>
[ApiController]
[Route("api/genres")]
[Produces("application/json")]
public sealed class GenresController : ControllerBase
{
    private readonly IGenreService _genres;

    public GenresController(IGenreService genres) => _genres = genres;

    /// <summary>Довідник жанрів (наповнений статичним сідінгом HasData).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GenreDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> GetAll(CancellationToken ct)
        => Ok(await _genres.GetAllAsync(ct));

    /// <summary>Скільки фільмів у кожному жанрі. <c>GROUP BY</c> виконує база даних.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<IReadOnlyList<GenreStatsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GenreStatsDto>>> GetStats(CancellationToken ct)
        => Ok(await _genres.GetStatsAsync(ct));

    /// <summary>
    /// Нові фільми жанру за період — <b>LINQ-запит через проміжну сутність M:N</b>
    /// з використанням її власного поля <c>AddedAt</c>.
    /// </summary>
    /// <param name="name">Назва жанру, напр. «Комедія».</param>
    /// <param name="since">Від якої дати (UTC). За замовчуванням — рік тому.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    /// <response code="400">Назву жанру не вказано.</response>
    [HttpGet("{name}/recent-movies")]
    [ProducesResponseType<IReadOnlyList<MovieListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<MovieListItemDto>>> GetRecentMovies(
        string name, [FromQuery] DateTime? since, CancellationToken ct)
        => Ok(await _genres.GetRecentlyAddedAsync(
                name, since ?? DateTime.UtcNow.AddYears(-1), ct));
}

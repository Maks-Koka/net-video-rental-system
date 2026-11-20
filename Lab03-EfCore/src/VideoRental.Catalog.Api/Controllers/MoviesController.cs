using FluentValidation;
using VideoRental.Catalog.Bll.Abstractions;
using VideoRental.Catalog.Bll.Dtos;
using VideoRental.Catalog.Dal.Specifications;
using Microsoft.AspNetCore.Mvc;

namespace VideoRental.Catalog.Api.Controllers;

/// <summary>
/// Фільми каталогу. Тонкий контролер: не звертається до <c>DbContext</c>
/// і не містить бізнес-правил — лише адаптує HTTP до <see cref="IMovieService"/>.
/// </summary>
[ApiController]
[Route("api/movies")]
[Produces("application/json")]
public sealed class MoviesController : ControllerBase
{
    private readonly IMovieService _movies;

    public MoviesController(IMovieService movies) => _movies = movies;

    /// <summary>
    /// Список фільмів із пагінацією, фільтрацією та сортуванням.
    /// Усе виконується на рівні БД (<c>OFFSET … FETCH</c>), повні таблиці
    /// в памʼять не завантажуються.
    /// </summary>
    /// <param name="query">
    /// page (з 1), pageSize (1–100), search, minRate, maxRate, directorId, genre,
    /// onlyAvailable, sortBy (title|fee|year|discs), sortDir (asc|desc).
    /// </param>
    /// <param name="validator">Валідатор параметрів запиту (FluentValidation).</param>
    /// <param name="ct">Токен скасування запиту.</param>
    /// <response code="200">Хвилина результатів; метадані — у тілі та в заголовках X-Pagination-*.</response>
    /// <response code="400">Некоректні параметри (напр. недозволене поле сортування).</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<MovieListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<MovieListItemDto>>> GetList(
        [FromQuery] MovieQuery query,
        [FromServices] IValidator<MovieQuery> validator,
        CancellationToken ct)
    {
        // FluentValidation викликається ЯВНО: автоматична валідація через
        // FluentValidation.AspNetCore застаріла й більше не підтримується
        var validation = await validator.ValidateAsync(query, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var result = await _movies.GetListAsync(query, ct);

        // метадані також у заголовках — зручно для кешування й для клієнтів,
        // які не парсять тіло
        Response.Headers["X-Pagination-TotalCount"] = result.TotalCount.ToString();
        Response.Headers["X-Pagination-Page"] = result.Page.ToString();
        Response.Headers["X-Pagination-PageSize"] = result.PageSize.ToString();
        Response.Headers["X-Pagination-TotalPages"] = result.TotalPages.ToString();

        return Ok(result);
    }

    /// <summary>Картка фільму з описом, дисками та жанрами (Eager Loading, один SQL-запит).</summary>
    /// <response code="404">Фільми з таким Id не існує.</response>
    [HttpGet("{id:long}")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieDto>> GetById(long id, CancellationToken ct)
        => Ok(await _movies.GetByIdAsync(id, ct));

    /// <summary>Фільми, яких немає в наявності (окрема специфікація).</summary>
    [HttpGet("out-of-stock")]
    [ProducesResponseType<IReadOnlyList<MovieListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MovieListItemDto>>> GetOutOfStock(CancellationToken ct)
        => Ok(await _movies.GetOutOfStockAsync(ct));

    /// <summary>Топ фільмів жанру за slug — третя специфікація через той самий репозиторій.</summary>
    /// <param name="slug">Slug жанру, напр. <c>comedy</c>.</param>
    /// <param name="take">Скільки повернути (1–50).</param>
    /// <param name="ct">Токен скасування запиту.</param>
    [HttpGet("by-genre/{slug}")]
    [ProducesResponseType<IReadOnlyList<MovieListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<MovieListItemDto>>> GetByGenreSlug(
        string slug, [FromQuery] int take, CancellationToken ct)
        => Ok(await _movies.GetTopByGenreSlugAsync(slug, take <= 0 ? 10 : take, ct));

    /// <summary>Додає фільм до каталогу.</summary>
    /// <response code="201">Фільм створено; посилання — у заголовку Location.</response>
    /// <response code="400">Помилки валідації (DataAnnotations або FluentValidation).</response>
    /// <response code="404">Режисера або жанру не існує.</response>
    /// <response code="409">Фільм з таким Barcode уже є.</response>
    [HttpPost]
    [ProducesResponseType<MovieDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> Create(
        CreateMovieDto input,
        [FromServices] IValidator<CreateMovieDto> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        var created = await _movies.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Змінює назву й тариф фільму.</summary>
    /// <response code="409">Порушено інваріант сутності (напр. тариф ≤ 0).</response>
    [HttpPut("{id:long}")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> Update(
        long id, UpdateMovieDto input,
        [FromServices] IValidator<UpdateMovieDto> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        return Ok(await _movies.UpdateAsync(id, input, ct));
    }

    /// <summary>Списує диски. Інваріант «достатній залишок» перевіряє сутність.</summary>
    /// <response code="409">Дисків бракує.</response>
    [HttpPost("{id:long}/remove-stock")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> RemoveStock(
        long id, ChangeStockDto input, CancellationToken ct)
        => Ok(await _movies.RemoveStockAsync(id, input, ct));

    /// <summary>Оприбутковує диски.</summary>
    [HttpPost("{id:long}/add-stock")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieDto>> AddStock(
        long id, ChangeStockDto input, CancellationToken ct)
        => Ok(await _movies.AddStockAsync(id, input, ct));

    /// <summary>Додає фізичний диск (зв'язок 1:N) і збільшує залишок.</summary>
    /// <response code="409">Інвентарний номер уже використовується.</response>
    [HttpPost("{id:long}/discs")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> AddDisc(
        long id, AddDiscDto input,
        [FromServices] IValidator<AddDiscDto> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid)
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        return Ok(await _movies.AddDiscAsync(id, input, ct));
    }

    /// <summary>Призначає фільму жанр (зв'язок M:N із власним полем AddedAt).</summary>
    /// <response code="409">Жанр уже призначено цьому фільму.</response>
    [HttpPost("{id:long}/genres")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> AssignGenre(
        long id, AssignGenreDto input, CancellationToken ct)
        => Ok(await _movies.AssignGenreAsync(id, input, ct));

    /// <summary>М'яке видалення фільму (<c>IsDeleted = 1</c>).</summary>
    /// <response code="204">Фільм позначено видаленою.</response>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _movies.DeleteAsync(id, ct);
        return NoContent();
    }
}

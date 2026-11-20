using VideoRental.Reviews.Application.Common.Interfaces;
using VideoRental.Reviews.Application.Common.Models;
using VideoRental.Reviews.Application.Discussions;
using VideoRental.Reviews.Application.Reviews.Commands;
using VideoRental.Reviews.Application.Reviews.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace VideoRental.Reviews.WebApi.Controllers;

/// <summary>
/// Базовий контролер: дає доступ до <see cref="ISender"/> і більше нічого.
/// </summary>
/// <remarks>
/// У Clean Architecture контролер — найтонший шар: він лише перетворює
/// HTTP-запит на команду або запит MediatR. Жодної бізнес-логіки й жодного
/// звернення до <c>IMongoCollection</c> тут немає.
/// </remarks>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase(ISender mediator) : ControllerBase
{
    protected ISender Mediator { get; } = mediator;
}

/// <summary>Відгуки на фільми.</summary>
[Route("api/reviews")]
public sealed class ReviewsController(ISender mediator) : ApiControllerBase(mediator)
{
    /// <summary>Список відгуків із пагінацією та фільтрацією (виконуються в MongoDB).</summary>
    /// <param name="movieId">Фільтр за фільмом.</param>
    /// <param name="minRating">Мінімальна оцінка (1–5).</param>
    /// <param name="page">Номер хвилини, з 1.</param>
    /// <param name="pageSize">Розмір хвилини, 1–100.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    /// <response code="200">Хвилина результатів.</response>
    /// <response code="400">Некоректні параметри (відхилено ValidationBehavior).</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ReviewDto>>> GetList(
        [FromQuery] long? movieId, [FromQuery] int? minRating,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await Mediator.Send(new GetReviewsListQuery(movieId, minRating, page, pageSize), ct));

    /// <summary>Курсорна пагінація — ефективніша за skip на великих наборах.</summary>
    /// <param name="movieId">Фільтр за фільмом.</param>
    /// <param name="cursor">ObjectId останнього документа попередньої порції.</param>
    /// <param name="pageSize">Розмір порції.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    [HttpGet("by-cursor")]
    [ProducesResponseType<CursorPage<ReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CursorPage<ReviewDto>>> GetByCursor(
        [FromQuery] long? movieId, [FromQuery] string? cursor,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await Mediator.Send(new GetReviewsByCursorQuery(movieId, cursor, pageSize), ct));

    /// <summary>Рейтинги фільмів — aggregation pipeline на боці MongoDB.</summary>
    [HttpGet("ratings")]
    [ProducesResponseType<IReadOnlyList<MovieRatingDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MovieRatingDto>>> GetRatings(CancellationToken ct)
        => Ok(await Mediator.Send(new GetMovieRatingsQuery(), ct));

    /// <summary>Повнотекстовий пошук по відгуках (text-індекс).</summary>
    /// <param name="term">Пошуковий запит, щонайменше 2 символи.</param>
    /// <param name="limit">Скільки результатів повернути.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    [HttpGet("search")]
    [ProducesResponseType<IReadOnlyList<ReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ReviewDto>>> Search(
        [FromQuery] string term, [FromQuery] int limit = 20, CancellationToken ct = default)
        => Ok(await Mediator.Send(new SearchReviewsQuery(term, limit), ct));

    /// <summary>
    /// Відгук за ідентифікатором. Версія документа повертається в заголовку
    /// <c>ETag</c> — її можна передати назад у <c>If-Match</c> при оновленні.
    /// </summary>
    /// <param name="id">ObjectId відгуку.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    /// <response code="404">Відгуку з таким Id не існує.</response>
    [HttpGet("{id}")]
    [ProducesResponseType<ReviewDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDetailsDto>> GetById(string id, CancellationToken ct)
    {
        var review = await Mediator.Send(new GetReviewByIdQuery(id), ct);
        Response.Headers.ETag = $"\"{review.Version}\"";
        return Ok(review);
    }

    /// <summary>Створює відгук.</summary>
    /// <response code="201">Створено; Id повертається рядком, а не сирим BSON.</response>
    /// <response code="400">Помилка валідації (обробник не викликався).</response>
    /// <response code="409">Цей клієнт уже залишав відгук на цю фільм.</response>
    [HttpPost]
    [ProducesResponseType<string>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<string>> Create(CreateReviewCommand command, CancellationToken ct)
    {
        var id = await Mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, id);
    }

    /// <summary>
    /// Оновлює відгук. Очікувану версію можна передати заголовком
    /// <c>If-Match</c> — тоді зміна застосується, лише якщо документ
    /// відтоді ніхто не правив.
    /// </summary>
    /// <param name="id">ObjectId відгуку.</param>
    /// <param name="body">Нова оцінка й текст.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    /// <response code="204">Оновлено.</response>
    /// <response code="409">Конфлікт версій: документ змінив інший користувач.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string id, UpdateReviewBody body, CancellationToken ct)
    {
        int? expectedVersion = body.ExpectedVersion;

        // If-Match має пріоритет над полем у тілі
        if (Request.Headers.IfMatch.Count > 0)
        {
            var raw = Request.Headers.IfMatch.ToString().Trim('"', ' ', 'W', '/');
            if (int.TryParse(raw, out var fromHeader))
                expectedVersion = fromHeader;
        }

        await Mediator.Send(new UpdateReviewCommand(id, body.Rating, body.Text, expectedVersion), ct);
        return NoContent();
    }

    /// <summary>Видаляє відгук.</summary>
    /// <response code="204">Видалено.</response>
    /// <response code="404">Відгуку не існує.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await Mediator.Send(new DeleteReviewCommand(id), ct);
        return NoContent();
    }

    /// <summary>Додає вбудований коментар до відгуку.</summary>
    /// <response code="204">Коментар додано.</response>
    /// <response code="400">Порушено доменне правило (наприклад, ліміт коментарів).</response>
    [HttpPost("{id}/comments")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(string id, AddCommentBody body, CancellationToken ct)
    {
        await Mediator.Send(new AddCommentCommand(id, body.CustomerId, body.ReviewerName, body.Text), ct);
        return NoContent();
    }

    /// <summary>Прикріплює обговорення до відгуку (гібридна модель MongoDB).</summary>
    /// <response code="204">Прикріплено.</response>
    [HttpPut("{id}/discussion/{discussionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AttachDiscussion(
        string id, string discussionId, CancellationToken ct)
    {
        await Mediator.Send(new AttachDiscussionCommand(id, discussionId), ct);
        return NoContent();
    }

    /// <summary>
    /// Оновлює денормалізовану назву фільму в усіх відгуках.
    /// У Лаб. 08 цей виклик замінить обробник події з сервісу Каталогу.
    /// </summary>
    /// <response code="200">Кількість оновлених документів.</response>
    [HttpPost("refresh-movie-title")]
    [ProducesResponseType<long>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<long>> RefreshMovieTitle(
        RefreshMovieTitleCommand command, CancellationToken ct)
        => Ok(await Mediator.Send(command, ct));
}

/// <summary>Тіло запиту оновлення відгуку (Id береться з маршруту).</summary>
public record UpdateReviewBody(int Rating, string? Text, int? ExpectedVersion);

/// <summary>Тіло запиту додавання коментаря.</summary>
public record AddCommentBody(long? CustomerId, string ReviewerName, string Text);

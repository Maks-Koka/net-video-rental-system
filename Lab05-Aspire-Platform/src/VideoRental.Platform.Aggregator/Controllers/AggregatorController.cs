using VideoRental.Platform.Aggregator.Dtos;
using VideoRental.Platform.Aggregator.Services;
using Microsoft.AspNetCore.Mvc;

namespace VideoRental.Platform.Aggregator.Controllers;

/// <summary>
/// Агрегувальні ендпоінти платформи.
/// </summary>
/// <remarks>
/// Префікс <c>/api/aggregator</c> збігається з маршрутом Gateway, тому
/// зовнішня адреса не змінюється: <c>http://localhost:5001/api/aggregator/...</c>.
/// </remarks>
[ApiController]
[Route("api/aggregator")]
[Produces("application/json")]
public sealed class AggregatorController(IAggregationService aggregation) : ControllerBase
{
    /// <summary>
    /// Повна картка оренди: сама оренда, клієнт, фільми з Каталогу,
    /// свіжі відгуки та зведені рейтинги цих фільмів.
    /// </summary>
    /// <param name="id">Ідентифікатор оренди.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    /// <response code="200">
    /// Агрегована відповідь. Якщо якийсь сервіс був недоступний, відповідь
    /// повертається ЧАСТКОВОЮ, а причина — у масиві <c>warnings</c>.
    /// </response>
    /// <response code="404">Оренди з таким ідентифікатором немає.</response>
    [HttpGet("rentals/{id:long}/full")]
    [ProducesResponseType<FullRentalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FullRentalDto>> GetFullRental(long id, CancellationToken ct)
    {
        var result = await aggregation.GetFullRentalAsync(id, ct);

        return result is null
            ? NotFound(new ProblemDetails
            {
                Title = "Оренду не знайдено",
                Detail = $"Оренди з ідентифікатором {id} немає в сервісі оренд",
                Status = StatusCodes.Status404NotFound
            })
            : Ok(result);
    }

    /// <summary>
    /// Картка фільму: дані Каталогу, локальна копія Сервісу оренд,
    /// рейтинг і останні відгуки.
    /// </summary>
    /// <param name="id">Ідентифікатор фільми в Каталозі.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    /// <response code="200">Агрегована картка (можливо, часткова).</response>
    /// <response code="404">Фільми з таким ідентифікатором немає в Каталозі.</response>
    [HttpGet("movies/{id:long}/card")]
    [ProducesResponseType<MovieCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieCardDto>> GetMovieCard(long id, CancellationToken ct)
    {
        var result = await aggregation.GetMovieCardAsync(id, ct);

        return result is null
            ? NotFound(new ProblemDetails
            {
                Title = "Фільм не знайдено",
                Detail = $"Фільми з ідентифікатором {id} немає в каталозі",
                Status = StatusCodes.Status404NotFound
            })
            : Ok(result);
    }
}

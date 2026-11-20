using VideoRental.Rentals.Bll.Abstractions;
using VideoRental.Rentals.Bll.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace VideoRental.Rentals.Api.Controllers;

/// <summary>
/// Оренди фільмів. <b>Тонкий контролер</b>: жодного SQL і жодного бізнес-правила —
/// лише адаптація HTTP до методів <see cref="IRentalService"/>.
/// </summary>
/// <remarks>
/// <see cref="CancellationToken"/> передається з контролера в BLL і далі в DAL;
/// у ланцюжку немає жодного <c>.Result</c> чи <c>.Wait()</c>.
/// Винятки НЕ перехоплюються тут — їх централізовано перетворює
/// <c>DomainExceptionHandler</c> на ProblemDetails із потрібним статусом.
/// </remarks>
[ApiController]
[Route("api/rentals")]
[Produces("application/json")]
public sealed class RentalsController : ControllerBase
{
    private readonly IRentalService _rentals;

    public RentalsController(IRentalService rentals) => _rentals = rentals;

    /// <summary>Повертає оренду разом із позиціями та деталями.</summary>
    /// <response code="200">Оренду знайдено.</response>
    /// <response code="404">Оренди з таким Id не існує.</response>
    [HttpGet("{id:long}")]
    [ProducesResponseType<RentalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RentalDto>> GetById(long id, CancellationToken ct)
        => Ok(await _rentals.GetByIdAsync(id, ct));

    /// <summary>Повертає прострочені оренди (термін вийшов, фільми на руках).</summary>
    [HttpGet("overdue")]
    [ProducesResponseType<IReadOnlyList<RentalDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RentalDto>>> GetOverdue(CancellationToken ct)
        => Ok(await _rentals.GetOverdueAsync(ct));

    /// <summary>Журнал переходів статусів оренди.</summary>
    [HttpGet("{id:long}/history")]
    [ProducesResponseType<IReadOnlyList<RentalStatusHistoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RentalStatusHistoryDto>>> GetHistory(
        long id, CancellationToken ct)
        => Ok(await _rentals.GetStatusHistoryAsync(id, ct));

    /// <summary>
    /// Оформлює нову оренду. Назва, режисер і тариф фільму фіксуються в позиціях
    /// як ЗНІМОК на момент оформлення.
    /// </summary>
    /// <response code="201">Оренду створено; посилання на неї — у заголовку Location.</response>
    /// <response code="400">Некоректні вхідні дані.</response>
    /// <response code="404">Клієнта або фільми не існує.</response>
    /// <response code="409">Клієнт заблокований або бракує дисків.</response>
    [HttpPost]
    [ProducesResponseType<RentalDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RentalDto>> Create(CreateRentalDto input, CancellationToken ct)
    {
        var created = await _rentals.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Підтверджує наявність дисків і видає фільми на руки
    /// в ОДНІЙ транзакції (головний транзакційний сценарій роботи).
    /// </summary>
    /// <response code="200">Оренду підтверджено й видано.</response>
    /// <response code="404">Оренди не існує.</response>
    /// <response code="409">Недопустимий перехід статусу або бракує дисків — зміни відкочено.</response>
    [HttpPost("{id:long}/issue")]
    [ProducesResponseType<RentalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RentalDto>> PickUp(long id, CancellationToken ct)
        => Ok(await _rentals.ConfirmAndPickUpAsync(id, ct));

    /// <summary>Підтверджує наявність дисків без оренди дисків.</summary>
    /// <response code="409">Недопустимий перехід статусу або бракує дисків.</response>
    [HttpPost("{id:long}/confirm")]
    [ProducesResponseType<RentalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RentalDto>> Confirm(long id, CancellationToken ct)
        => Ok(await _rentals.ConfirmDiscsAsync(id, ct));

    /// <summary>Приймає фільми назад; процедура нараховує пеню за прострочені дні.</summary>
    /// <response code="409">Повернути можна лише видану на руки оренду.</response>
    [HttpPost("{id:long}/return")]
    [ProducesResponseType<RentalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RentalDto>> Return(long id, ReturnRentalDto input, CancellationToken ct)
        => Ok(await _rentals.ReturnAsync(id, input, ct));

    /// <summary>Скасовує оренду. Допустимо ЛИШЕ до оренди фільмів на руки.</summary>
    /// <response code="409">Фільми вже видані на руки — скасувати не можна.</response>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType<RentalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RentalDto>> Cancel(long id, CancelRentalDto input, CancellationToken ct)
        => Ok(await _rentals.CancelAsync(id, input, ct));

    /// <summary>Оплати пені по оренди.</summary>
    [HttpGet("{id:long}/late-fees")]
    [ProducesResponseType<IReadOnlyList<LateFeePaymentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<LateFeePaymentDto>>> GetLateFeePayments(
        long id, CancellationToken ct)
        => Ok(await _rentals.GetLateFeePaymentsAsync(id, ct));

    /// <summary>
    /// Оплачує пеню. ІДЕМПОТЕНТНА операція: повторний запит із тим самим
    /// <c>idempotencyKey</c> не створює другий платіж, а повертає наявний.
    /// </summary>
    /// <response code="201">Платіж створено.</response>
    /// <response code="409">Пені немає або сума перевищує залишок до сплати.</response>
    [HttpPost("{id:long}/late-fees")]
    [ProducesResponseType<LateFeePaymentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LateFeePaymentDto>> PayLateFee(
        long id, PayLateFeeDto input, CancellationToken ct)
    {
        var payment = await _rentals.PayLateFeeAsync(id, input, ct);
        return CreatedAtAction(nameof(GetLateFeePayments), new { id }, payment);
    }
}

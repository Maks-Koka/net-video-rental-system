using VideoRental.Rentals.Bll.Abstractions;
using VideoRental.Rentals.Bll.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace VideoRental.Rentals.Api.Controllers;

/// <summary>
/// Клієнти відеопрокату. Обслуговується репозиторієм на ЧИСТОМУ ADO.NET.
/// </summary>
[ApiController]
[Route("api/customers")]
[Produces("application/json")]
public sealed class CustomersController : ControllerBase
{
    private readonly ICustomerService _customers;
    private readonly IRentalService _rentals;

    public CustomersController(ICustomerService customers, IRentalService rentals)
    {
        _customers = customers;
        _rentals = rentals;
    }

    /// <summary>Перелік активних клієнтів (без м'яко видалених).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CustomerDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CustomerDto>>> GetAll(CancellationToken ct)
        => Ok(await _customers.GetAllAsync(ct));

    /// <summary>Клієнт за ідентифікатором.</summary>
    /// <response code="404">Клієнта з таким Id не існує.</response>
    [HttpGet("{id:long}")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> GetById(long id, CancellationToken ct)
        => Ok(await _customers.GetByIdAsync(id, ct));

    /// <summary>Оренди клієнта, за потреби відфільтровані за статусом.</summary>
    /// <param name="id">Ідентифікатор клієнта.</param>
    /// <param name="status">Статус оренди: Submitted, AwaitingValidation, DiscsConfirmed, PickedUp, Returned, Cancelled.</param>
    /// <param name="ct">Токен скасування запиту.</param>
    /// <response code="400">Невідомий статус.</response>
    /// <response code="404">Клієнта не існує.</response>
    [HttpGet("{id:long}/rentals")]
    [ProducesResponseType<IReadOnlyList<RentalDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RentalDto>>> GetRentals(
        long id, [FromQuery] string? status, CancellationToken ct)
        => Ok(await _rentals.GetByCustomerAsync(id, status, ct));

    /// <summary>Звіт по орендах клієнта (денормалізоване представлення з БД).</summary>
    /// <response code="404">Клієнта не існує (помилка 50001 з процедури).</response>
    [HttpGet("{id:long}/rental-summary")]
    [ProducesResponseType<IReadOnlyList<RentalSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RentalSummaryDto>>> GetRentalSummary(
        long id, [FromQuery] string? status, CancellationToken ct)
        => Ok(await _rentals.GetCustomerSummaryAsync(id, status, ct));

    /// <summary>Реєструє нового клієнта.</summary>
    /// <response code="201">Клієнта створено.</response>
    /// <response code="400">Некоректні вхідні дані.</response>
    /// <response code="409">Email або номер картки вже використовується.</response>
    [HttpPost]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerDto input, CancellationToken ct)
    {
        var created = await _customers.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Блокує клієнта: заблокований не може оформлювати нові оренди.</summary>
    /// <response code="204">Прапорець установлено.</response>
    [HttpPut("{id:long}/blocked")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetBlocked(
        long id, [FromQuery] bool value, CancellationToken ct)
    {
        await _customers.SetBlockedAsync(id, value, ct);
        return NoContent();
    }

    /// <summary>
    /// М'яке видалення клієнта (<c>IsDeleted = 1</c>): історія оренд зберігається.
    /// </summary>
    /// <response code="204">Клієнта позначено видаленим.</response>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _customers.DeleteAsync(id, ct);
        return NoContent();
    }
}

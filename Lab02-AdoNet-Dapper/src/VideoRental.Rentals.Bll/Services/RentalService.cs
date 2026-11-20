using System.Data;
using AutoMapper;
using VideoRental.Rentals.Bll.Abstractions;
using VideoRental.Rentals.Bll.Dtos;
using VideoRental.Rentals.Dal.Abstractions;
using VideoRental.Rentals.Domain.Constants;
using VideoRental.Rentals.Domain.Entities;
using VideoRental.Rentals.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace VideoRental.Rentals.Bll.Services;

/// <summary>
/// Сервіс оренд — ЄДИНИЙ власник бізнес-логіки домену.
/// Контролери API лише адаптують HTTP до цих методів і не ухвалюють рішень.
/// </summary>
public sealed class RentalService : IRentalService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<RentalService> _logger;

    public RentalService(IUnitOfWork uow, IMapper mapper, ILogger<RentalService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    // ------------------------------------------------------------- Читання ---

    public async Task<RentalDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var rental = await _uow.Rentals.GetWithItemsAsync(id, ct)
                   ?? throw NotFoundException.For("Rental", id);
        return _mapper.Map<RentalDto>(rental);
    }

    public async Task<IReadOnlyList<RentalDto>> GetByCustomerAsync(
        long customerId, string? status, CancellationToken ct = default)
    {
        if (status is not null && !RentalStatus.IsKnown(status))
            throw new ValidationException(nameof(status),
                $"Unknown status '{status}'. Allowed: {string.Join(", ", RentalStatus.All)}");

        // перевіряємо існування клієнта, щоб повернути 404, а не порожній список
        _ = await _uow.Customers.GetByIdAsync(customerId, ct)
            ?? throw NotFoundException.For("Customer", customerId);

        var rentals = await _uow.Rentals.GetByCustomerAsync(customerId, status, ct);
        return rentals.Select(_mapper.Map<RentalDto>).ToList();
    }

    public async Task<IReadOnlyList<RentalDto>> GetOverdueAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rentals = await _uow.Rentals.GetOverdueAsync(today, ct);
        return rentals.Select(_mapper.Map<RentalDto>).ToList();
    }

    public async Task<IReadOnlyList<RentalStatusHistoryDto>> GetStatusHistoryAsync(
        long rentalId, CancellationToken ct = default)
    {
        _ = await _uow.Rentals.GetByIdAsync(rentalId, ct)
            ?? throw NotFoundException.For("Rental", rentalId);

        var history = await _uow.Rentals.GetStatusHistoryAsync(rentalId, ct);
        return history.Select(_mapper.Map<RentalStatusHistoryDto>).ToList();
    }

    public async Task<IReadOnlyList<RentalSummaryDto>> GetCustomerSummaryAsync(
        long customerId, string? status, CancellationToken ct = default)
    {
        // процедура сама кидає 50001 -> NotFoundException, якщо клієнта немає
        var rows = await _uow.Rentals.GetCustomerSummaryAsync(customerId, status, ct);
        return rows.Select(_mapper.Map<RentalSummaryDto>).ToList();
    }

    // --------------------------------------------------------- Створення ---

    /// <summary>
    /// Створення оренди. Бізнес-валідація, для якої потрібен стан із БД:
    /// клієнт існує й не заблокований, фільми існують, дисків достатньо.
    /// Назва, режисер і тариф фільму беруться з <c>dbo.Movies</c> і записуються в
    /// позицію як ЗНІМОК — цю частину виконує процедура <c>usp_CreateRental</c>.
    /// </summary>
    public async Task<RentalDto> CreateAsync(CreateRentalDto input, CancellationToken ct = default)
    {
        var customer = await _uow.Customers.GetByIdAsync(input.CustomerId, ct)
                     ?? throw NotFoundException.For("Customer", input.CustomerId);

        if (customer.IsBlocked)
            throw new BusinessConflictException(
                $"Customer {customer.Id} is blocked and cannot borrow movies");

        if (input.DueDate < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationException(nameof(input.DueDate),
                "DueDate cannot be in the past");

        // склеюємо дублікати фільмів у вхідному запиті
        var items = input.Items
            .GroupBy(i => i.MovieId)
            .Select(g => (MovieId: g.Key, Units: g.Sum(x => x.Units)))
            .ToList();

        foreach (var (movieId, units) in items)
        {
            var movie = await _uow.Movies.GetByIdAsync(movieId, ct)
                       ?? throw NotFoundException.For("Movie", movieId);

            if (movie.DiscsAvailable < units)
                throw new BusinessConflictException(
                    $"Movie '{movie.Title}' has only {movie.DiscsAvailable} discs available, {units} requested");
        }

        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var rentalId = await _uow.Rentals.CreateViaProcedureAsync(
                input.CustomerId, input.DueDate, input.PickupStore, input.Comment,
                items, CurrentUser, ct);

            await _uow.CommitAsync(ct);

            _logger.LogInformation(
                "Rental {RentalId} created for customer {CustomerId} with {ItemCount} item(s)",
                rentalId, input.CustomerId, items.Count);

            var created = await _uow.Rentals.GetWithItemsAsync(rentalId, ct)
                          ?? throw NotFoundException.For("Rental", rentalId);
            return _mapper.Map<RentalDto>(created);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }
    }

    // ------------------------------------------- Транзакційні бізнес-операції ---

    /// <summary>
    /// <b>ГОЛОВНИЙ транзакційний сценарій роботи.</b>
    /// </summary>
    /// <remarks>
    /// У ОДНІЙ транзакції зустрічаються Unit of Work, ДВА репозиторії та
    /// збережувані процедури з Лаб. 01:
    /// <list type="number">
    ///   <item><c>Rentals.ConfirmDiscsAsync</c> → <c>usp_ConfirmRentalDiscs</c>
    ///         (допустимість переходу перевіряє процедура);</item>
    ///   <item><c>Rentals.PickUpAsync</c> → <c>usp_PickUpRental</c>;</item>
    ///   <item><c>Movies.DecreaseDiscsAsync</c> → <c>usp_DecreaseMovieDiscs</c>
    ///         для кожної позиції.</item>
    /// </list>
    /// Якщо на будь-якому кроці бракує дисків, процедура кидає 50003 →
    /// <see cref="BusinessConflictException"/> → <c>RollbackAsync</c>, і статус
    /// оренди повертається назад. Або все, або нічого.
    /// </remarks>
    public async Task<RentalDto> ConfirmAndPickUpAsync(long rentalId, CancellationToken ct = default)
    {
        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var rental = await _uow.Rentals.GetWithItemsAsync(rentalId, ct)
                       ?? throw NotFoundException.For("Rental", rentalId);

            if (rental.Items.Count == 0)
                throw new BusinessConflictException($"Rental {rentalId} has no items");

            // крок 1: -> DiscsConfirmed (перехід перевіряє процедура з Лаб. 01)
            if (rental.Status is RentalStatus.Submitted or RentalStatus.AwaitingValidation)
                await _uow.Rentals.ConfirmDiscsAsync(rentalId, CurrentUser, ct);

            // крок 2: -> PickedUp; процедура сама зменшує залишок дисків
            await _uow.Rentals.PickUpAsync(rentalId, CurrentUser, ct);

            await _uow.CommitAsync(ct);

            _logger.LogInformation("Rental {RentalId} confirmed and picked up", rentalId);
        }
        catch (Exception ex)
        {
            await _uow.RollbackAsync(ct);       // статус повертається назад
            _logger.LogWarning(ex, "Failed to confirm and pick up rental {RentalId}; transaction rolled back", rentalId);
            throw;
        }

        return await GetByIdAsync(rentalId, ct);
    }

    public async Task<RentalDto> ConfirmDiscsAsync(long rentalId, CancellationToken ct = default)
    {
        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var rental = await _uow.Rentals.GetWithItemsAsync(rentalId, ct)
                       ?? throw NotFoundException.For("Rental", rentalId);

            if (rental.Items.Count == 0)
                throw new BusinessConflictException($"Rental {rentalId} has no items");

            await _uow.Rentals.ConfirmDiscsAsync(rentalId, CurrentUser, ct);
            await _uow.CommitAsync(ct);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }

        return await GetByIdAsync(rentalId, ct);
    }

    /// <summary>
    /// Повернення фільмів. Процедура <c>usp_ReturnRental</c> в одній транзакції
    /// повертає диски в довідник і рахує пеню за прострочені дні,
    /// використовуючи ЗНІМОК тарифу з позицій оренди.
    /// </summary>
    public async Task<RentalDto> ReturnAsync(long rentalId, ReturnRentalDto input, CancellationToken ct = default)
    {
        if (input.ReturnDate is { } date && date > DateTime.UtcNow.AddDays(1))
            throw new ValidationException(nameof(input.ReturnDate),
                "ReturnDate cannot be in the future");

        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        decimal fine;
        try
        {
            fine = await _uow.Rentals.ReturnAsync(rentalId, input.ReturnDate, CurrentUser, ct);
            await _uow.CommitAsync(ct);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }

        if (fine > 0)
            _logger.LogInformation("Rental {RentalId} returned with fine {Fine}", rentalId, fine);

        return await GetByIdAsync(rentalId, ct);
    }

    public async Task<RentalDto> CancelAsync(long rentalId, CancelRentalDto input, CancellationToken ct = default)
    {
        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await _uow.Rentals.CancelAsync(rentalId, input.Reason, CurrentUser, ct);
            await _uow.CommitAsync(ct);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }

        return await GetByIdAsync(rentalId, ct);
    }

    // -------------------------------------------------------- Оплата пені ---

    /// <summary>
    /// Оплата пені. ІДЕМПОТЕНТНА: якщо платіж із таким
    /// <c>IdempotencyKey</c> уже є, повертається він, а другий не створюється.
    /// </summary>
    public async Task<LateFeePaymentDto> PayLateFeeAsync(
        long rentalId, PayLateFeeDto input, CancellationToken ct = default)
    {
        var rental = await _uow.Rentals.GetByIdAsync(rentalId, ct)
                   ?? throw NotFoundException.For("Rental", rentalId);

        if (!string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            var existing = await _uow.LateFeePayments.FindByIdempotencyKeyAsync(input.IdempotencyKey, ct);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Idempotent replay for key {Key}: returning existing payment {PaymentId}",
                    input.IdempotencyKey, existing.Id);
                return _mapper.Map<LateFeePaymentDto>(existing);
            }
        }

        if (rental.LateFeeAmount <= 0)
            throw new BusinessConflictException($"Rental {rentalId} has no outstanding fine");

        var alreadyPaid = await _uow.LateFeePayments.GetTotalPaidAsync(rentalId, ct);
        var outstanding = rental.LateFeeAmount - alreadyPaid;

        if (outstanding <= 0)
            throw new BusinessConflictException($"Fine for rental {rentalId} is already fully paid");

        if (input.Amount > outstanding)
            throw new BusinessConflictException(
                $"Payment {input.Amount} exceeds the outstanding fine {outstanding}");

        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        long paymentId;
        try
        {
            paymentId = await _uow.LateFeePayments.AddAsync(new LateFeePayment
            {
                RentalId = rentalId,
                Amount = input.Amount,
                Method = input.Method,
                IdempotencyKey = input.IdempotencyKey
            }, ct);

            await _uow.CommitAsync(ct);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }

        var saved = await _uow.LateFeePayments.GetByIdAsync(paymentId, ct)
                    ?? throw NotFoundException.For("LateFeePayment", paymentId);
        return _mapper.Map<LateFeePaymentDto>(saved);
    }

    public async Task<IReadOnlyList<LateFeePaymentDto>> GetLateFeePaymentsAsync(
        long rentalId, CancellationToken ct = default)
    {
        _ = await _uow.Rentals.GetByIdAsync(rentalId, ct)
            ?? throw NotFoundException.For("Rental", rentalId);

        var payments = await _uow.LateFeePayments.GetByRentalAsync(rentalId, ct);
        return payments.Select(_mapper.Map<LateFeePaymentDto>).ToList();
    }

    /// <summary>
    /// Поточний користувач для аудитних колонок. Автентифікація з'явиться
    /// в наступних роботах; доки її немає — технічний маркер сервісу.
    /// </summary>
    private const string CurrentUser = "rentals-api";
}

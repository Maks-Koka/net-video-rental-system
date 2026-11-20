using VideoRental.Rentals.Bll.Dtos;

namespace VideoRental.Rentals.Bll.Abstractions;

/// <summary>Бізнес-операції над клієнтами.</summary>
public interface ICustomerService
{
    Task<CustomerDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken ct = default);
    Task SetBlockedAsync(long id, bool isBlocked, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

/// <summary>Бізнес-операції над локальною копією довідника фільмів.</summary>
public interface IMovieService
{
    Task<MovieDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<MovieDto>> SearchAsync(string? title, CancellationToken ct = default);
    Task<MovieDto> SyncFromCatalogAsync(SyncMovieDto input, CancellationToken ct = default);
}

/// <summary>
/// Бізнес-операції над орендами — ядро домену.
/// Усі операції, що змінюють кілька таблиць, виконуються в ОДНІЙ транзакції
/// через Unit of Work.
/// </summary>
public interface IRentalService
{
    Task<RentalDto> GetByIdAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<RentalDto>> GetByCustomerAsync(long customerId, string? status, CancellationToken ct = default);

    Task<IReadOnlyList<RentalDto>> GetOverdueAsync(CancellationToken ct = default);

    Task<RentalDto> CreateAsync(CreateRentalDto input, CancellationToken ct = default);

    /// <summary>
    /// ТРАНЗАКЦІЙНА бізнес-операція: підтвердження наявності дисків
    /// і оренда фільмів на руки зі зменшенням залишку — або все, або нічого.
    /// </summary>
    Task<RentalDto> ConfirmAndPickUpAsync(long rentalId, CancellationToken ct = default);

    Task<RentalDto> ConfirmDiscsAsync(long rentalId, CancellationToken ct = default);

    Task<RentalDto> ReturnAsync(long rentalId, ReturnRentalDto input, CancellationToken ct = default);

    Task<RentalDto> CancelAsync(long rentalId, CancelRentalDto input, CancellationToken ct = default);

    Task<IReadOnlyList<RentalStatusHistoryDto>> GetStatusHistoryAsync(long rentalId, CancellationToken ct = default);

    Task<IReadOnlyList<RentalSummaryDto>> GetCustomerSummaryAsync(long customerId, string? status, CancellationToken ct = default);

    /// <summary>Оплата пені з підтримкою ідемпотентності.</summary>
    Task<LateFeePaymentDto> PayLateFeeAsync(long rentalId, PayLateFeeDto input, CancellationToken ct = default);

    Task<IReadOnlyList<LateFeePaymentDto>> GetLateFeePaymentsAsync(long rentalId, CancellationToken ct = default);
}

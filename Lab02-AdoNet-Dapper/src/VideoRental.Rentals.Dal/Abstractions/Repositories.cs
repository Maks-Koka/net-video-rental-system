using VideoRental.Rentals.Domain.Entities;

namespace VideoRental.Rentals.Dal.Abstractions;

/// <summary>
/// Репозиторій клієнтів.
/// <b>Свідомо НЕ успадковує <see cref="IGenericRepository{T}"/></b>: його
/// реалізація написана на чистому ADO.NET і демонструє ручну роботу з
/// підключенням, параметрами та клієнтом даних без сторонніх відеопрокатів.
/// </summary>
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(long id, CancellationToken ct = default);

    Task<Customer?> GetByMembershipNumberAsync(string membershipNumber, CancellationToken ct = default);

    Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct = default);

    Task<long> AddAsync(Customer customer, CancellationToken ct = default);

    Task<bool> SetBlockedAsync(long id, bool isBlocked, string? changedBy, CancellationToken ct = default);

    /// <summary>М'яке видалення: <c>IsDeleted = 1</c>, запис лишається в історії.</summary>
    Task<bool> SoftDeleteAsync(long id, CancellationToken ct = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
}

/// <summary>
/// Репозиторій оренд. Успадковує узагальнений CRUD і додає лише
/// вузькоспеціалізовані запити: JOIN з позиціями та виклики збережуваних
/// процедур із Лаб. 01.
/// </summary>
public interface IRentalRepository : IGenericRepository<Rental>
{
    /// <summary>Оренда разом із позиціями — ОДНИМ запитом (Dapper multi-mapping).</summary>
    Task<Rental?> GetWithItemsAsync(long rentalId, CancellationToken ct = default);

    Task<IReadOnlyList<Rental>> GetByCustomerAsync(long customerId, string? status, CancellationToken ct = default);

    Task<IReadOnlyList<Rental>> GetOverdueAsync(DateOnly today, CancellationToken ct = default);

    /// <summary>Створення оренди з позиціями: <c>usp_CreateRental</c>.</summary>
    Task<long> CreateViaProcedureAsync(
        long customerId, DateOnly dueDate, string pickupStore, string? comment,
        IEnumerable<(long MovieId, int Units)> items, string? createdBy,
        CancellationToken ct = default);

    /// <summary><c>usp_ConfirmRentalDiscs</c>: → <c>DiscsConfirmed</c>.</summary>
    Task ConfirmDiscsAsync(long rentalId, string? changedBy, CancellationToken ct = default);

    /// <summary><c>usp_PickUpRental</c>: → <c>PickedUp</c>.</summary>
    Task PickUpAsync(long rentalId, string? changedBy, CancellationToken ct = default);

    /// <summary><c>usp_ReturnRental</c>: → <c>Returned</c>, повертає нараховану пеню.</summary>
    Task<decimal> ReturnAsync(long rentalId, DateTime? returnDate, string? changedBy, CancellationToken ct = default);

    /// <summary><c>usp_CancelRental</c>: → <c>Cancelled</c> (лише до видачі дисків).</summary>
    Task CancelAsync(long rentalId, string? reason, string? changedBy, CancellationToken ct = default);

    /// <summary><c>usp_GetCustomerRentalSummary</c>: звіт по орендах клієнта.</summary>
    Task<IReadOnlyList<RentalSummary>> GetCustomerSummaryAsync(long customerId, string? status, CancellationToken ct = default);

    Task<IReadOnlyList<RentalStatusHistoryEntry>> GetStatusHistoryAsync(long rentalId, CancellationToken ct = default);
}

/// <summary>Репозиторій локальної копії довідника фільмів.</summary>
public interface IMovieRepository : IGenericRepository<Movie>
{
    Task<IReadOnlyList<Movie>> SearchAsync(string? title, CancellationToken ct = default);

    /// <summary>
    /// Зменшення залишку дисків через <c>usp_DecreaseMovieDiscs</c>.
    /// Це ДРУГИЙ репозиторій транзакційного сценарію: помилка тут
    /// відкочує зміну статусу оренди, зроблену першим репозиторієм.
    /// </summary>
    Task DecreaseDiscsAsync(long movieId, int quantity, CancellationToken ct = default);

    Task IncreaseDiscsAsync(long movieId, int quantity, CancellationToken ct = default);

    /// <summary>Синхронізація копії довідника з Каталогу (у Лаб. 08 — подією).</summary>
    Task UpsertFromCatalogAsync(Movie movie, CancellationToken ct = default);
}

/// <summary>Репозиторій оплат пені.</summary>
public interface ILateFeePaymentRepository : IGenericRepository<LateFeePayment>
{
    Task<IReadOnlyList<LateFeePayment>> GetByRentalAsync(long rentalId, CancellationToken ct = default);

    Task<decimal> GetTotalPaidAsync(long rentalId, CancellationToken ct = default);

    /// <summary>Повертає вже існуючий платіж за токеном ідемпотентності, якщо він є.</summary>
    Task<LateFeePayment?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default);
}

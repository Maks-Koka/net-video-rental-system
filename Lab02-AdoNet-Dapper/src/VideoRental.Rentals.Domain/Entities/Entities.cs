using VideoRental.Rentals.Domain.Constants;

namespace VideoRental.Rentals.Domain.Entities;

/// <summary>
/// Клієнт відеопрокату. Власна сутність контексту «Оренда».
/// Відповідає таблиці <c>dbo.Customers</c> з Лаб. 01.
/// </summary>
public class Customer
{
    public long Id { get; set; }
    public string MembershipNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public bool IsBlocked { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>
/// Локальна копія довідника фільмів із БД Каталогу (Проєкт №2).
/// <see cref="Id"/> — ТОЙ САМИЙ ідентифікатор, що й <c>Movies.Id</c> у
/// <c>VideoRentalCatalogDb</c>, але FOREIGN KEY між базами не існує.
/// Оновлюється подіями з Каталогу (таблиця політики дублювання, Лаб. 01).
/// </summary>
public class Movie
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Director { get; set; } = "";
    public string? Barcode { get; set; }
    public decimal DailyRate { get; set; }

    /// <summary>
    /// Залишок дисків, доступних для оренди. ВЛАСНІ дані цього контексту:
    /// змінюються операціями оренди/повернення, а не подіями з Каталогу.
    /// </summary>
    public int DiscsAvailable { get; set; }

    public DateTime SyncedAt { get; set; }
}

/// <summary>
/// Оренда фільмів — коренева сутність агрегату (аналог <c>Order</c> в eShop).
/// </summary>
public class Rental
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; } = RentalStatus.Submitted;
    public DateTime RentalDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public decimal LateFeeAmount { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>Позиції оренди. Частина агрегату: не існують без оренди.</summary>
    public List<RentalItem> Items { get; } = [];

    /// <summary>Деталі оренди (зв'язок 1:1).</summary>
    public RentalDetail? Detail { get; set; }

    public bool IsOverdue(DateOnly today) =>
        Status == RentalStatus.PickedUp && today > DueDate;
}

/// <summary>Деталі оренди. Зв'язок 1:1 — PK таблиці є водночас FK на <c>Rentals</c>.</summary>
public class RentalDetail
{
    public long RentalId { get; set; }
    public string PickupStore { get; set; } = "";
    public string? ClerkName { get; set; }
    public string? Comment { get; set; }
}

/// <summary>
/// Позиція оренди — проміжна сутність зв'язку M:N між <c>Rentals</c> і <c>Movies</c>.
/// <see cref="MovieTitle"/>, <see cref="MovieDirector"/> та <see cref="DailyRate"/> —
/// ЗНІМОК полів каталогу на момент оформлення оренди: вони свідомо НЕ оновлюються
/// при зміні каталогу (так само, як <c>OrderItem</c> в eShop зберігає
/// <c>ProductName</c> і <c>UnitPrice</c>).
/// </summary>
public class RentalItem
{
    public long RentalId { get; set; }
    public long MovieId { get; set; }
    public string MovieTitle { get; set; } = "";
    public string MovieDirector { get; set; } = "";
    public decimal DailyRate { get; set; }
    public int Units { get; set; }
    public decimal Discount { get; set; }

    public decimal DailyCharge => DailyRate * Units - Discount;
}

/// <summary>Запис журналу переходів статусів оренди (1:N від <c>Rentals</c>).</summary>
public class RentalStatusHistoryEntry
{
    public long Id { get; set; }
    public long RentalId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public DateTime ChangedAt { get; set; }
    public string? ChangedBy { get; set; }
    public string? Note { get; set; }
}

/// <summary>Оплата пені за прострочення (1:N від <c>Rentals</c>).</summary>
public class LateFeePayment
{
    public long Id { get; set; }
    public long RentalId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    public DateTime PaidAt { get; set; }

    /// <summary>Токен ідемпотентності: повторна відправка не створює другий платіж.</summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Рядок денормалізованого представлення <c>dbo.vw_RentalSummary</c> —
/// читальна модель для звітів (повертається процедурою
/// <c>usp_GetCustomerRentalSummary</c>).
/// </summary>
public class RentalSummary
{
    public long RentalId { get; set; }
    public string Status { get; set; } = "";
    public DateTime RentalDate { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public decimal LateFeeAmount { get; set; }
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string MembershipNumber { get; set; } = "";
    public string? PickupStore { get; set; }
    public int TitlesCount { get; set; }
    public int UnitsTotal { get; set; }
    public decimal DailyChargeTotal { get; set; }
}

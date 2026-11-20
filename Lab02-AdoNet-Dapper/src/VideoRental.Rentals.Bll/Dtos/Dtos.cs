using System.ComponentModel.DataAnnotations;

namespace VideoRental.Rentals.Bll.Dtos;

/* =============================================================================
   DTO — ЛИШЕ дані, без логіки. Атрибути DataAnnotations дають перший рівень
   валідації: некоректний запит відхиляється ще ДО виклику сервісу
   (ApiController автоматично повертає 400 ValidationProblemDetails).
   Другий рівень — бізнес-валідація в BLL, де потрібен стан із БД.
   ============================================================================= */

// ----------------------------------------------------------------- Customers ---

public record CustomerDto(
    long Id,
    string MembershipNumber,
    string Email,
    string FullName,
    string? Phone,
    bool IsBlocked,
    DateTime CreatedAt);

public class CreateCustomerDto
{
    [Required, StringLength(20, MinimumLength = 3)]
    public string MembershipNumber { get; set; } = "";

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, StringLength(200, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    [Phone, StringLength(30)]
    public string? Phone { get; set; }
}

// ------------------------------------------------------------------- Movies ---

public record MovieDto(
    long Id,
    string Title,
    string Director,
    string? Barcode,
    decimal DailyRate,
    int DiscsAvailable,
    DateTime SyncedAt);

/// <summary>Синхронізація локальної копії довідника з БД Каталогу (Проєкт №2).</summary>
public class SyncMovieDto
{
    [Range(1, long.MaxValue)]
    public long Id { get; set; }

    [Required, StringLength(300)]
    public string Title { get; set; } = "";

    [Required, StringLength(200)]
    public string Director { get; set; } = "";

    [StringLength(20)]
    public string? Barcode { get; set; }

    [Range(0.01, 100_000)]
    public decimal DailyRate { get; set; }

    [Range(0, int.MaxValue)]
    public int DiscsAvailable { get; set; }
}

// ------------------------------------------------------------------- Rentals ---

public record RentalItemDto(
    long MovieId,
    string MovieTitle,
    string MovieDirector,
    decimal DailyRate,
    int Units,
    decimal Discount,
    decimal DailyCharge);

public record RentalDetailDto(
    string PickupStore,
    string? ClerkName,
    string? Comment);

public record RentalDto(
    long Id,
    long CustomerId,
    string Status,
    DateTime RentalDate,
    DateOnly DueDate,
    DateTime? PickedUpAt,
    DateTime? ReturnedAt,
    decimal LateFeeAmount,
    RentalDetailDto? Detail,
    IReadOnlyList<RentalItemDto> Items);

public class CreateRentalItemDto
{
    [Range(1, long.MaxValue)]
    public long MovieId { get; set; }

    [Range(1, 100)]
    public int Units { get; set; } = 1;
}

public class CreateRentalDto
{
    [Range(1, long.MaxValue)]
    public long CustomerId { get; set; }

    [Required]
    public DateOnly DueDate { get; set; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string PickupStore { get; set; } = "";

    [StringLength(1000)]
    public string? Comment { get; set; }

    [Required, MinLength(1, ErrorMessage = "A rental must contain at least one item")]
    public List<CreateRentalItemDto> Items { get; set; } = [];
}

public class ReturnRentalDto
{
    /// <summary>Дата повернення. Якщо не вказана — поточний момент UTC.</summary>
    public DateTime? ReturnDate { get; set; }
}

public class CancelRentalDto
{
    [StringLength(400)]
    public string? Reason { get; set; }
}

public record RentalStatusHistoryDto(
    long Id,
    string? FromStatus,
    string ToStatus,
    DateTime ChangedAt,
    string? ChangedBy,
    string? Note);

public record RentalSummaryDto(
    long RentalId,
    string Status,
    DateTime RentalDate,
    DateOnly DueDate,
    DateTime? ReturnedAt,
    decimal LateFeeAmount,
    long CustomerId,
    string CustomerName,
    string MembershipNumber,
    string? PickupStore,
    int TitlesCount,
    int UnitsTotal,
    decimal DailyChargeTotal);

// ------------------------------------------------------------ LateFeePayments ---

public record LateFeePaymentDto(
    long Id,
    long RentalId,
    decimal Amount,
    string Method,
    DateTime PaidAt,
    string? IdempotencyKey);

public class PayLateFeeDto
{
    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }

    [Required]
    [RegularExpression("^(Cash|Card|Online)$", ErrorMessage = "Method must be Cash, Card or Online")]
    public string Method { get; set; } = "Cash";

    /// <summary>
    /// Токен ідемпотентності: повторна відправка того самого запиту
    /// не створює другий платіж.
    /// </summary>
    [StringLength(64, MinimumLength = 8)]
    public string? IdempotencyKey { get; set; }
}

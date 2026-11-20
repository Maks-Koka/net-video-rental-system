namespace VideoRental.Platform.Aggregator.Dtos;

/* =============================================================================
   DTO Агрегатора (Частина 6.3).

   ВАЖЛИВО: це ВЛАСНІ контракти Агрегатора, а не посилання на збірки
   мікросервісів. Агрегатор спілкується з ними лише по HTTP, тому спільних
   типів у коді бути не повинно — інакше зникає незалежність сервісів і
   кожна зміна в мікросервісі ламала б збірку Агрегатора.

   Описані лише ті поля, які потрібні для бізнес-сценарію; решту
   System.Text.Json просто проігнорує.
   ============================================================================= */

// ----- те, що віддає Сервіс оренд (Лаб. 02) -------------------------------

public record RentalItemDto(
    long MovieId,
    string MovieTitle,
    string MovieDirector,
    decimal DailyRate,
    int Units,
    decimal DailyCharge);

public record RentalDto(
    long Id,
    long CustomerId,
    string Status,
    DateTime RentalDate,
    DateOnly DueDate,
    DateTime? PickedUpAt,
    DateTime? ReturnedAt,
    decimal LateFeeAmount,
    IReadOnlyList<RentalItemDto> Items);

public record CustomerDto(
    long Id,
    string MembershipNumber,
    string FullName,
    string Email,
    bool IsBlocked);

// ----- те, що віддає Каталог (Лаб. 03) ------------------------------------

public record MovieDto(
    long Id,
    string Title,
    string Barcode,
    decimal DailyRate,
    int AvailableDiscs,
    int? ReleaseYear,
    string DirectorName,
    IReadOnlyList<string> Genres);

// ----- те, що віддає Сервіс відгуків (Лаб. 04) ----------------------------

public record ReviewerDto(long? CustomerId, string DisplayName);

public record ReviewDto(
    string Id,
    long MovieId,
    string MovieTitle,
    ReviewerDto Reviewer,
    int Rating,
    string? Text,
    int CommentsCount,
    DateTime CreatedAt);

public record MovieRatingDto(
    long MovieId,
    string MovieTitle,
    long ReviewsCount,
    double AverageRating,
    int MinRating,
    int MaxRating);

/// <summary>Хвилина результатів — спільний формат списків у Лаб. 03–04.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize);

// ----- КОМПОЗИТНІ DTO самого Агрегатора -----------------------------------

/// <summary>
/// Скільки часу зайняв кожен виклик і чим він завершився.
/// </summary>
/// <remarks>
/// Поле існує, щоб паралельність було ВИДНО: сума тривалостей викликів
/// помітно більша за <c>TotalMs</c> усієї агрегації.
/// </remarks>
public record SourceCallDto(string Service, string Status, long ElapsedMs);

public record AggregationMetaDto(
    long TotalMs,
    long SequentialMs,
    IReadOnlyList<SourceCallDto> Calls)
{
    /// <summary>Скільки часу зекономила паралельність.</summary>
    public long SavedByParallelismMs => Math.Max(0, SequentialMs - TotalMs);
}

/// <summary>
/// Повна картка оренди — бізнес-сценарій «усе про цю оренду на одному екрані».
/// </summary>
/// <remarks>
/// Структура описує САМ СЦЕНАРІЙ, а не копіює структури окремих сервісів:
/// клієнт, фільми з актуальними даними Каталогу, свіжі відгуки на ці фільми
/// та їхні зведені рейтинги в одній відповіді.
/// </remarks>
public record FullRentalDto(
    RentalDto Rental,
    CustomerDto? Customer,
    IReadOnlyList<MovieDto> Movies,
    IReadOnlyList<ReviewDto> Reviews,
    IReadOnlyList<MovieRatingDto> Ratings,
    IReadOnlyList<string> Warnings,
    AggregationMetaDto Meta);

/// <summary>Локальна проєкція довідника фільмів у Сервісі оренд (Лаб. 02).</summary>
/// <remarks>
/// Денормалізована копія: Сервіс оренд тримає власний довідник, щоб не
/// звертатися до Каталогу на кожну операцію. <c>SyncedAt</c> показує, коли
/// копію синхронізували останній раз, тому картка фільму дає побачити
/// розходження між канонічними даними Каталогу та копією.
/// </remarks>
public record RentalsMovieDto(
    long Id,
    string Title,
    string Director,
    decimal DailyRate,
    int DiscsAvailable,
    DateTime SyncedAt);

/// <summary>Картка фільму: дані Каталогу + копія Сервісу оренд + відгуки й рейтинг.</summary>
public record MovieCardDto(
    MovieDto Movie,
    RentalsMovieDto? RentalsProjection,
    MovieRatingDto? Rating,
    IReadOnlyList<ReviewDto> Reviews,
    IReadOnlyList<string> Warnings,
    AggregationMetaDto Meta);

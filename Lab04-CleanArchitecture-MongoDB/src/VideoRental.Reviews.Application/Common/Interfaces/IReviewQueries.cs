using VideoRental.Reviews.Application.Common.Models;

namespace VideoRental.Reviews.Application.Common.Interfaces;

/* =============================================================================
   DTO читання. Лежать в Application, бо є частиною контракту сценаріїв
   використання, а не деталлю зберігання.
   ============================================================================= */

public record ReviewerDto(long? CustomerId, string DisplayName);

public record CommentDto(string ReviewerName, string Text, DateTime CreatedAt, int Likes);

/// <summary>Коротке представлення відгуку для списків.</summary>
public record ReviewDto(
    string Id,
    long MovieId,
    string MovieTitle,
    ReviewerDto Reviewer,
    int Rating,
    string? Text,
    int CommentsCount,
    string? DiscussionId,
    DateTime CreatedAt,
    int Version);

/// <summary>Повне представлення відгуку з вбудованими коментарями.</summary>
public record ReviewDetailsDto(
    string Id,
    long MovieId,
    string MovieTitle,
    ReviewerDto Reviewer,
    int Rating,
    string? Text,
    IReadOnlyList<CommentDto> Comments,
    IReadOnlyList<string> Photos,
    IReadOnlyList<string> Tags,
    string? DiscussionId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int Version);

/// <summary>Агрегат: середня оцінка та розподіл оцінок фільму.</summary>
public record MovieRatingDto(
    long MovieId,
    string MovieTitle,
    long ReviewsCount,
    double AverageRating,
    int MinRating,
    int MaxRating,
    long CommentsTotal);

/// <summary>
/// Сторона ЧИТАННЯ в CQRS.
/// </summary>
/// <remarks>
/// Обробники запитів з Application звертаються лише до цього інтерфейсу й
/// нічого не знають про MongoDB. Реалізація в Infrastructure використовує
/// проєкції та aggregation pipeline — із бази вибираються тільки потрібні поля.
/// Так само в eShop запити до замовлень оголошено інтерфейсом
/// <c>IOrderQueries</c>.
/// </remarks>
public interface IReviewQueries
{
    Task<ReviewDetailsDto?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Список із пагінацією та фільтрацією засобами MongoDB (skip/limit, $match).</summary>
    Task<PagedResult<ReviewDto>> GetListAsync(
        long? movieId, int? minRating, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Курсорна пагінація — ефективніша за skip на великих наборах.</summary>
    Task<CursorPage<ReviewDto>> GetPageByCursorAsync(
        long? movieId, string? cursor, int pageSize, CancellationToken ct = default);

    /// <summary>Рейтинги фільмів — aggregation pipeline ($group).</summary>
    Task<IReadOnlyList<MovieRatingDto>> GetMovieRatingsAsync(CancellationToken ct = default);

    /// <summary>Повнотекстовий пошук через text-індекс.</summary>
    Task<IReadOnlyList<ReviewDto>> SearchAsync(string term, int limit, CancellationToken ct = default);
}

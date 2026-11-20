using VideoRental.Reviews.Application.Common.Interfaces;
using VideoRental.Reviews.Application.Common.Models;
using VideoRental.Reviews.Application.Discussions;
using VideoRental.Reviews.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace VideoRental.Reviews.Infrastructure.Persistence;

/// <summary>
/// Сторона ЧИТАННЯ в CQRS: реалізація <see cref="IReviewQueries"/>.
/// </summary>
/// <remarks>
/// Пагінація та фільтрація виконуються <b>засобами MongoDB</b> (<c>$match</c>,
/// <c>skip</c>/<c>limit</c>), а не в памʼяті; агрегати рахує
/// aggregation pipeline. Проєкція в DTO виконується після вибірки — так
/// зберігається коректна десеріалізація Value Objects через BsonClassMap.
/// </remarks>
public sealed class ReviewQueries(MongoDbContext context) : IReviewQueries
{
    private readonly IMongoCollection<Review> _reviews = context.Reviews;

    public async Task<ReviewDetailsDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var review = await _reviews.Find(r => r.Id == objectId).FirstOrDefaultAsync(ct);
        return review is null ? null : ToDetails(review);
    }

    public async Task<PagedResult<ReviewDto>> GetListAsync(
        long? movieId, int? minRating, int page, int pageSize, CancellationToken ct = default)
    {
        var filter = BuildFilter(movieId, minRating);

        // лічильник і хвилину рахує БАЗА: totalCount — це всі документи
        // за критеріями, а не розмір хвилини
        var total = await _reviews.CountDocumentsAsync(filter, cancellationToken: ct);

        var items = await _reviews
            .Find(filter)
            .SortByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return new PagedResult<ReviewDto>(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    /// <summary>
    /// Курсорна пагінація: наступна порція береться після останнього
    /// отриманого <c>_id</c>. <see cref="ObjectId"/> монотонно зростає разом
    /// із часом створення, тому сортування за ним еквівалентне сортуванню
    /// за часом — і при цьому не потрібен <c>skip</c>, який сповільнюється
    /// зі зростанням номера хвилини.
    /// </summary>
    public async Task<CursorPage<ReviewDto>> GetPageByCursorAsync(
        long? movieId, string? cursor, int pageSize, CancellationToken ct = default)
    {
        var filter = BuildFilter(movieId, null);

        if (!string.IsNullOrEmpty(cursor) && ObjectId.TryParse(cursor, out var lastId))
            filter &= Builders<Review>.Filter.Lt(r => r.Id, lastId);

        var items = await _reviews
            .Find(filter)
            .SortByDescending(r => r.Id)
            .Limit(pageSize + 1)                     // +1 — аби дізнатися, чи є ще хвилина
            .ToListAsync(ct);

        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);

        return new CursorPage<ReviewDto>(
            items.Select(ToDto).ToList(),
            hasMore && items.Count > 0 ? items[^1].Id.ToString() : null);
    }

    /// <summary>Середня оцінка та розподіл по фільмах — <c>$group</c> на боці БД.</summary>
    public async Task<IReadOnlyList<MovieRatingDto>> GetMovieRatingsAsync(
        CancellationToken ct = default)
    {
        var pipeline = new[]
        {
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$movieId" },
                { "movieTitle",     new BsonDocument("$first", "$movieTitle") },
                { "reviewsCount",  new BsonDocument("$sum", 1) },
                { "averageRating", new BsonDocument("$avg", "$rating") },
                { "minRating",     new BsonDocument("$min", "$rating") },
                { "maxRating",     new BsonDocument("$max", "$rating") },
                { "commentsTotal", new BsonDocument("$sum",
                    new BsonDocument("$size",
                        new BsonDocument("$ifNull", new BsonArray { "$comments", new BsonArray() }))) }
            }),
            new BsonDocument("$sort", new BsonDocument { { "averageRating", -1 }, { "movieTitle", 1 } })
        };

        var rows = await _reviews
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .ToListAsync(ct);

        return rows.Select(d => new MovieRatingDto(
            MovieId: d["_id"].ToInt64(),
            MovieTitle: d.GetValue("movieTitle", "").AsString,
            ReviewsCount: d["reviewsCount"].ToInt64(),
            AverageRating: Math.Round(d["averageRating"].ToDouble(), 2),
            MinRating: d["minRating"].ToInt32(),
            MaxRating: d["maxRating"].ToInt32(),
            CommentsTotal: d["commentsTotal"].ToInt64())).ToList();
    }

    /// <summary>Повнотекстовий пошук через text-індекс (<c>ix_reviews_fulltext</c>).</summary>
    public async Task<IReadOnlyList<ReviewDto>> SearchAsync(
        string term, int limit, CancellationToken ct = default)
    {
        var filter = Builders<Review>.Filter.Text(term);

        var items = await _reviews.Find(filter).Limit(limit).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    private static FilterDefinition<Review> BuildFilter(long? movieId, int? minRating)
    {
        var filter = Builders<Review>.Filter.Empty;

        if (movieId is { } id)
            filter &= Builders<Review>.Filter.Eq("movieId", id);

        if (minRating is { } rating)
            filter &= Builders<Review>.Filter.Gte("rating", rating);

        return filter;
    }

    private static ReviewDto ToDto(Review r) => new(
        r.Id.ToString(),
        r.Movie.MovieId,
        r.Movie.MovieTitle,
        new ReviewerDto(r.Reviewer.CustomerId, r.Reviewer.DisplayName),
        r.Rating.Value,
        r.Text,
        r.Comments.Count,
        r.DiscussionId?.ToString(),
        r.CreatedAt,
        r.Version);

    private static ReviewDetailsDto ToDetails(Review r) => new(
        r.Id.ToString(),
        r.Movie.MovieId,
        r.Movie.MovieTitle,
        new ReviewerDto(r.Reviewer.CustomerId, r.Reviewer.DisplayName),
        r.Rating.Value,
        r.Text,
        r.Comments.Select(c => new CommentDto(
            c.Reviewer.DisplayName, c.Text, c.CreatedAt, c.Likes)).ToList(),
        r.Photos.ToList(),
        r.Tags.ToList(),
        r.DiscussionId?.ToString(),
        r.CreatedAt,
        r.UpdatedAt,
        r.Version);
}

/// <summary>Сторона читання для обговорень.</summary>
public sealed class DiscussionQueries(MongoDbContext context) : IDiscussionQueries
{
    private readonly IMongoCollection<Discussion> _discussions = context.Discussions;

    public async Task<DiscussionDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var discussion = await _discussions.Find(d => d.Id == objectId).FirstOrDefaultAsync(ct);
        return discussion is null ? null : ToDto(discussion);
    }

    public async Task<IReadOnlyList<DiscussionDto>> GetByMovieAsync(
        long movieId, CancellationToken ct = default)
    {
        var items = await _discussions
            .Find(Builders<Discussion>.Filter.Eq("movieId", movieId))
            .SortByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return items.Select(ToDto).ToList();
    }

    private static DiscussionDto ToDto(Discussion d) => new(
        d.Id.ToString(),
        d.Movie.MovieId,
        d.Movie.MovieTitle,
        d.Topic,
        d.IsClosed,
        d.ClosedReason,
        d.Messages.Select(m => new DiscussionMessageDto(
            m.Reviewer.DisplayName, m.Text, m.PostedAt, m.ReplyTo)).ToList(),
        d.CreatedAt,
        d.LastActivityAt,
        d.Version);
}

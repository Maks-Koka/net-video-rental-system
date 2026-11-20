using FluentValidation;
using VideoRental.Reviews.Application.Common.Interfaces;
using VideoRental.Reviews.Application.Common.Messaging;
using VideoRental.Reviews.Application.Common.Models;
using VideoRental.Reviews.Domain.Exceptions;
using VideoRental.Reviews.Domain.ValueObjects;
using MediatR;
using MongoDB.Bson;

namespace VideoRental.Reviews.Application.Reviews.Queries;

/* =============================================================================
   ЗАПИТИ (сторона читання CQRS).

   Обробник запиту НЕ звертається до MongoDB напряму — Application не залежить
   від Infrastructure. Він викликає IReviewQueries, а реалізація в Infrastructure
   використовує проєкції та aggregation pipeline.
   ============================================================================= */

// ----------------------------------------------------------- GetReviewById ---

public record GetReviewByIdQuery(string Id) : IQuery<ReviewDetailsDto>;

public sealed class GetReviewByIdQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetReviewByIdQuery, ReviewDetailsDto>
{
    public async Task<ReviewDetailsDto> Handle(GetReviewByIdQuery q, CancellationToken ct)
        => await queries.GetByIdAsync(q.Id, ct)
           ?? throw NotFoundException.For("Review", q.Id);
}

public sealed class GetReviewByIdQueryValidator : AbstractValidator<GetReviewByIdQuery>
{
    public GetReviewByIdQueryValidator()
        => RuleFor(x => x.Id)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("Id must be a valid ObjectId");
}

// --------------------------------------------------------- GetReviewsList ---

public record GetReviewsListQuery(
    long? MovieId = null,
    int? MinRating = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ReviewDto>>;

public sealed class GetReviewsListQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetReviewsListQuery, PagedResult<ReviewDto>>
{
    public Task<PagedResult<ReviewDto>> Handle(GetReviewsListQuery q, CancellationToken ct)
        => queries.GetListAsync(
            q.MovieId, q.MinRating, Math.Max(q.Page, 1), Math.Clamp(q.PageSize, 1, 100), ct);
}

public sealed class GetReviewsListQueryValidator : AbstractValidator<GetReviewsListQuery>
{
    public GetReviewsListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.MovieId).GreaterThan(0).When(x => x.MovieId.HasValue);
        RuleFor(x => x.MinRating)
            .InclusiveBetween(Rating.Min, Rating.Max)
            .When(x => x.MinRating.HasValue);
    }
}

// ------------------------------------------------------ GetReviewsByCursor ---

public record GetReviewsByCursorQuery(long? MovieId, string? Cursor, int PageSize = 20)
    : IQuery<CursorPage<ReviewDto>>;

public sealed class GetReviewsByCursorQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetReviewsByCursorQuery, CursorPage<ReviewDto>>
{
    public Task<CursorPage<ReviewDto>> Handle(GetReviewsByCursorQuery q, CancellationToken ct)
        => queries.GetPageByCursorAsync(q.MovieId, q.Cursor, Math.Clamp(q.PageSize, 1, 100), ct);
}

public sealed class GetReviewsByCursorQueryValidator : AbstractValidator<GetReviewsByCursorQuery>
{
    public GetReviewsByCursorQueryValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Cursor)
            .Must(c => ObjectId.TryParse(c, out _))
            .When(x => !string.IsNullOrEmpty(x.Cursor))
            .WithMessage("Cursor must be a valid ObjectId");
    }
}

// ---------------------------------------------------------- GetMovieRatings ---

public record GetMovieRatingsQuery : IQuery<IReadOnlyList<MovieRatingDto>>;

public sealed class GetMovieRatingsQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetMovieRatingsQuery, IReadOnlyList<MovieRatingDto>>
{
    public Task<IReadOnlyList<MovieRatingDto>> Handle(GetMovieRatingsQuery q, CancellationToken ct)
        => queries.GetMovieRatingsAsync(ct);
}

// ------------------------------------------------------------ SearchReviews ---

public record SearchReviewsQuery(string Term, int Limit = 20) : IQuery<IReadOnlyList<ReviewDto>>;

public sealed class SearchReviewsQueryHandler(IReviewQueries queries)
    : IRequestHandler<SearchReviewsQuery, IReadOnlyList<ReviewDto>>
{
    public Task<IReadOnlyList<ReviewDto>> Handle(SearchReviewsQuery q, CancellationToken ct)
        => queries.SearchAsync(q.Term, Math.Clamp(q.Limit, 1, 100), ct);
}

public sealed class SearchReviewsQueryValidator : AbstractValidator<SearchReviewsQuery>
{
    public SearchReviewsQueryValidator()
    {
        RuleFor(x => x.Term).NotEmpty().MinimumLength(2).MaximumLength(100);
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

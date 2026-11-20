using FluentValidation;
using VideoRental.Reviews.Application.Common.Messaging;
using VideoRental.Reviews.Domain.Entities;
using VideoRental.Reviews.Domain.Exceptions;
using VideoRental.Reviews.Domain.Interfaces;
using VideoRental.Reviews.Domain.ValueObjects;
using MediatR;
using MongoDB.Bson;

namespace VideoRental.Reviews.Application.Discussions;

/* =============================================================================
   Фіча «Обговорення»: команди й запит в одному файлі, бо вона компактна.
   Демонструє referencing — відгук підключає обговорення за ObjectId.
   ============================================================================= */

public record DiscussionMessageDto(string ReviewerName, string Text, DateTime PostedAt, int? ReplyTo);

public record DiscussionDto(
    string Id,
    long MovieId,
    string MovieTitle,
    string Topic,
    bool IsClosed,
    string? ClosedReason,
    IReadOnlyList<DiscussionMessageDto> Messages,
    DateTime CreatedAt,
    DateTime? LastActivityAt,
    int Version);

// ------------------------------------------------------- CreateDiscussion ---

public record CreateDiscussionCommand(long MovieId, string MovieTitle, string Topic)
    : ICommand<string>;

public sealed class CreateDiscussionCommandHandler(IDiscussionRepository discussions)
    : IRequestHandler<CreateDiscussionCommand, string>
{
    public async Task<string> Handle(CreateDiscussionCommand cmd, CancellationToken ct)
    {
        var discussion = new Discussion(new MovieRef(cmd.MovieId, cmd.MovieTitle), cmd.Topic);
        await discussions.AddAsync(discussion, ct);
        return discussion.Id.ToString();
    }
}

public sealed class CreateDiscussionCommandValidator : AbstractValidator<CreateDiscussionCommand>
{
    public CreateDiscussionCommandValidator()
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.MovieTitle).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Topic).NotEmpty().MaximumLength(300);
    }
}

// ------------------------------------------------------------ PostMessage ---

public record PostDiscussionMessageCommand(
    string DiscussionId, long? CustomerId, string ReviewerName, string Text, int? ReplyTo) : ICommand;

public sealed class PostDiscussionMessageCommandHandler(IDiscussionRepository discussions)
    : IRequestHandler<PostDiscussionMessageCommand>
{
    public async Task Handle(PostDiscussionMessageCommand cmd, CancellationToken ct)
    {
        var id = ObjectId.Parse(cmd.DiscussionId);
        var discussion = await discussions.GetByIdAsync(id, ct)
                         ?? throw NotFoundException.For("Discussion", cmd.DiscussionId);

        discussion.Post(new Reviewer(cmd.CustomerId, cmd.ReviewerName), cmd.Text, cmd.ReplyTo);
        await discussions.UpdateAsync(discussion, ct);
    }
}

public sealed class PostDiscussionMessageCommandValidator
    : AbstractValidator<PostDiscussionMessageCommand>
{
    public PostDiscussionMessageCommandValidator()
    {
        RuleFor(x => x.DiscussionId)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("DiscussionId must be a valid ObjectId");
        RuleFor(x => x.ReviewerName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.ReplyTo).GreaterThanOrEqualTo(0).When(x => x.ReplyTo.HasValue);
    }
}

// -------------------------------------------------------- CloseDiscussion ---

public record CloseDiscussionCommand(string DiscussionId, string? Reason) : ICommand;

public sealed class CloseDiscussionCommandHandler(IDiscussionRepository discussions)
    : IRequestHandler<CloseDiscussionCommand>
{
    public async Task Handle(CloseDiscussionCommand cmd, CancellationToken ct)
    {
        var discussion = await discussions.GetByIdAsync(ObjectId.Parse(cmd.DiscussionId), ct)
                         ?? throw NotFoundException.For("Discussion", cmd.DiscussionId);

        discussion.Close(cmd.Reason);
        await discussions.UpdateAsync(discussion, ct);
    }
}

public sealed class CloseDiscussionCommandValidator : AbstractValidator<CloseDiscussionCommand>
{
    public CloseDiscussionCommandValidator()
    {
        RuleFor(x => x.DiscussionId)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("DiscussionId must be a valid ObjectId");
        RuleFor(x => x.Reason).MaximumLength(400);
    }
}

// --------------------------------------------- AttachDiscussion (гібрид) ---

/// <summary>
/// Прикріплює обговорення до відгуку — саме цей зв'язок робить модель
/// <b>гібридною</b>: кілька коментарів вбудовано у відгук, а довга ветка
/// лежить в окремій колекції й підключається за посиланням.
/// </summary>
public record AttachDiscussionCommand(string ReviewId, string DiscussionId) : ICommand;

public sealed class AttachDiscussionCommandHandler(
    IReviewRepository reviews, IDiscussionRepository discussions)
    : IRequestHandler<AttachDiscussionCommand>
{
    public async Task Handle(AttachDiscussionCommand cmd, CancellationToken ct)
    {
        var discussionId = ObjectId.Parse(cmd.DiscussionId);

        _ = await discussions.GetByIdAsync(discussionId, ct)
            ?? throw NotFoundException.For("Discussion", cmd.DiscussionId);

        var review = await reviews.GetByIdAsync(ObjectId.Parse(cmd.ReviewId), ct)
                     ?? throw NotFoundException.For("Review", cmd.ReviewId);

        review.AttachDiscussion(discussionId);
        await reviews.UpdateAsync(review, ct);
    }
}

public sealed class AttachDiscussionCommandValidator : AbstractValidator<AttachDiscussionCommand>
{
    public AttachDiscussionCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("ReviewId must be a valid ObjectId");
        RuleFor(x => x.DiscussionId)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("DiscussionId must be a valid ObjectId");
    }
}

// ------------------------------------------------------ GetDiscussionById ---

public interface IDiscussionQueries
{
    Task<DiscussionDto?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<DiscussionDto>> GetByMovieAsync(long movieId, CancellationToken ct = default);
}

public record GetDiscussionByIdQuery(string Id) : IQuery<DiscussionDto>;

public sealed class GetDiscussionByIdQueryHandler(IDiscussionQueries queries)
    : IRequestHandler<GetDiscussionByIdQuery, DiscussionDto>
{
    public async Task<DiscussionDto> Handle(GetDiscussionByIdQuery q, CancellationToken ct)
        => await queries.GetByIdAsync(q.Id, ct)
           ?? throw NotFoundException.For("Discussion", q.Id);
}

public sealed class GetDiscussionByIdQueryValidator : AbstractValidator<GetDiscussionByIdQuery>
{
    public GetDiscussionByIdQueryValidator()
        => RuleFor(x => x.Id)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("Id must be a valid ObjectId");
}

public record GetDiscussionsByMovieQuery(long MovieId) : IQuery<IReadOnlyList<DiscussionDto>>;

public sealed class GetDiscussionsByMovieQueryHandler(IDiscussionQueries queries)
    : IRequestHandler<GetDiscussionsByMovieQuery, IReadOnlyList<DiscussionDto>>
{
    public Task<IReadOnlyList<DiscussionDto>> Handle(
        GetDiscussionsByMovieQuery q, CancellationToken ct)
        => queries.GetByMovieAsync(q.MovieId, ct);
}

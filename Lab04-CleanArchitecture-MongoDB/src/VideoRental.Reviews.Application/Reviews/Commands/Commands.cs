using FluentValidation;
using VideoRental.Reviews.Application.Common.Messaging;
using VideoRental.Reviews.Domain.Entities;
using VideoRental.Reviews.Domain.Exceptions;
using VideoRental.Reviews.Domain.Interfaces;
using VideoRental.Reviews.Domain.ValueObjects;
using MediatR;
using MongoDB.Bson;

namespace VideoRental.Reviews.Application.Reviews.Commands;

/* =============================================================================
   КОМАНДИ (сторона запису CQRS).
   Кожна операція — окремий об'єкт-запит з окремим обробником і валідатором.
   Обробники працюють ЛИШЕ через доменні інтерфейси (IReviewRepository);
   жоден із них не бачить IMongoCollection.
   ============================================================================= */

// ------------------------------------------------------------ CreateReview ---

public record CreateReviewCommand(
    long MovieId,
    string MovieTitle,
    long? CustomerId,
    string ReviewerName,
    int Rating,
    string? Text) : ICommand<string>;

public sealed class CreateReviewCommandHandler(IReviewRepository reviews)
    : IRequestHandler<CreateReviewCommand, string>
{
    public async Task<string> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        var review = new Review(
            new MovieRef(cmd.MovieId, cmd.MovieTitle),
            new Reviewer(cmd.CustomerId, cmd.ReviewerName),
            cmd.Rating,
            cmd.Text);

        await reviews.AddAsync(review, ct);
        return review.Id.ToString();
    }
}

public sealed class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator(IReviewRepository reviews)
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.MovieTitle).NotEmpty().MaximumLength(300);
        RuleFor(x => x.ReviewerName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max);
        RuleFor(x => x.Text).MaximumLength(2000);
        RuleFor(x => x.CustomerId).GreaterThan(0).When(x => x.CustomerId.HasValue);

        // АСИНХРОННА валідація із запитом до MongoDB: один клієнт — один відгук
        // на фільм. Для анонімних відгуків правило не застосовується.
        RuleFor(x => x)
            .MustAsync(async (cmd, ct) =>
                cmd.CustomerId is null || !await reviews.ExistsAsync(cmd.MovieId, cmd.CustomerId, ct))
            .WithName("Review")
            .WithMessage("Цей клієнт уже залишив відгук на цю фільм");
    }
}

// ------------------------------------------------------------ UpdateReview ---

public record UpdateReviewCommand(string Id, int Rating, string? Text, int? ExpectedVersion)
    : ICommand;

public sealed class UpdateReviewCommandHandler(IReviewRepository reviews)
    : IRequestHandler<UpdateReviewCommand>
{
    public async Task Handle(UpdateReviewCommand cmd, CancellationToken ct)
    {
        var id = ObjectId.Parse(cmd.Id);
        var review = await reviews.GetByIdAsync(id, ct)
                     ?? throw NotFoundException.For("Review", cmd.Id);

        // перевірка If-Match: клієнт редагував саме цю версію документа
        if (cmd.ExpectedVersion is { } expected && review.Version != expected)
            throw new ConflictException(
                $"Review {cmd.Id} has version {review.Version}, but {expected} was expected");

        review.Update(cmd.Rating, cmd.Text);
        await reviews.UpdateAsync(review, ct);
    }
}

public sealed class UpdateReviewCommandValidator : AbstractValidator<UpdateReviewCommand>
{
    public UpdateReviewCommandValidator()
    {
        RuleFor(x => x.Id)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("Id must be a valid ObjectId");
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max);
        RuleFor(x => x.Text).MaximumLength(2000);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0).When(x => x.ExpectedVersion.HasValue);
    }
}

// ------------------------------------------------------------ DeleteReview ---

public record DeleteReviewCommand(string Id) : ICommand;

public sealed class DeleteReviewCommandHandler(IReviewRepository reviews)
    : IRequestHandler<DeleteReviewCommand>
{
    public async Task Handle(DeleteReviewCommand cmd, CancellationToken ct)
    {
        if (!await reviews.DeleteAsync(ObjectId.Parse(cmd.Id), ct))
            throw NotFoundException.For("Review", cmd.Id);
    }
}

public sealed class DeleteReviewCommandValidator : AbstractValidator<DeleteReviewCommand>
{
    public DeleteReviewCommandValidator()
        => RuleFor(x => x.Id)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("Id must be a valid ObjectId");
}

// -------------------------------------------------------------- AddComment ---

public record AddCommentCommand(string ReviewId, long? CustomerId, string ReviewerName, string Text)
    : ICommand;

public sealed class AddCommentCommandHandler(IReviewRepository reviews)
    : IRequestHandler<AddCommentCommand>
{
    public async Task Handle(AddCommentCommand cmd, CancellationToken ct)
    {
        var id = ObjectId.Parse(cmd.ReviewId);
        var review = await reviews.GetByIdAsync(id, ct)
                     ?? throw NotFoundException.For("Review", cmd.ReviewId);

        review.AddComment(new Reviewer(cmd.CustomerId, cmd.ReviewerName), cmd.Text);
        await reviews.UpdateAsync(review, ct);
    }
}

public sealed class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("ReviewId must be a valid ObjectId");
        RuleFor(x => x.ReviewerName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(1000);
    }
}

// ------------------------------------------------- RefreshMovieTitle (подія) ---

/// <summary>
/// Оновлює денормалізовану назву фільму в усіх відгуках.
/// У Лаб. 08 цю команду викличе обробник події <c>MovieUpdated</c> з Каталогу;
/// зараз вона доступна як ендпоінт для демонстрації політики дублювання.
/// </summary>
public record RefreshMovieTitleCommand(long MovieId, string NewTitle) : ICommand<long>;

public sealed class RefreshMovieTitleCommandHandler(IReviewRepository reviews)
    : IRequestHandler<RefreshMovieTitleCommand, long>
{
    public Task<long> Handle(RefreshMovieTitleCommand cmd, CancellationToken ct)
        => reviews.RefreshMovieTitleAsync(cmd.MovieId, cmd.NewTitle, ct);
}

public sealed class RefreshMovieTitleCommandValidator : AbstractValidator<RefreshMovieTitleCommand>
{
    public RefreshMovieTitleCommandValidator()
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.NewTitle).NotEmpty().MaximumLength(300);
    }
}

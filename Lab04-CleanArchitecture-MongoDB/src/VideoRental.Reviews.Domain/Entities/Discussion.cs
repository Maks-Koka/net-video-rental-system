using VideoRental.Reviews.Domain.Common;
using VideoRental.Reviews.Domain.Exceptions;
using VideoRental.Reviews.Domain.ValueObjects;

namespace VideoRental.Reviews.Domain.Entities;

/// <summary>Повідомлення в обговоренні — вбудований піддокумент.</summary>
public sealed class DiscussionMessage
{
    public Reviewer Reviewer { get; private set; } = null!;
    public string Text { get; private set; } = "";
    public DateTime PostedAt { get; private set; }

    /// <summary>Індекс повідомлення, на яке це є відповіддю (або <c>null</c>).</summary>
    public int? ReplyTo { get; private set; }

    private DiscussionMessage() { }

    public DiscussionMessage(Reviewer reviewer, string text, int? replyTo = null)
    {
        ArgumentNullException.ThrowIfNull(reviewer);
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Message text is required");
        if (replyTo is < 0)
            throw new DomainException("ReplyTo must not be negative");

        Reviewer = reviewer;
        Text = text.Trim();
        PostedAt = DateTime.UtcNow;
        ReplyTo = replyTo;
    }
}

/// <summary>
/// Обговорення фільми — друга коренева сутність контексту.
/// </summary>
/// <remarks>
/// Винесена в <b>окрему колекцію</b>, а не вбудована у відгук, саме тому, що
/// кількість повідомлень не обмежена: вбудовувати тисячі повідомлень у відгук
/// не можна (ліміт документа 16 МБ і постійне перезаписування всього документа).
/// Відгук підключає обговорення <b>за посиланням</b> —
/// <c>Review.DiscussionId</c>.
/// </remarks>
public sealed class Discussion : BaseEntity
{
    // не readonly — присвоюється драйвером MongoDB при десеріалізації
    private List<DiscussionMessage> _messages = [];

    // та сама причина, що й у Review: у документі з Лаб. 01 movieId і movieTitle
    // лежать на верхньому рівні
    private long _movieId;
    private string _movieTitle = "";

    public MovieRef Movie => new(_movieId, _movieTitle);
    public string Topic { get; private set; } = "";
    public bool IsClosed { get; private set; }
    public string? ClosedReason { get; private set; }
    public DateTime? LastActivityAt { get; private set; }

    public IReadOnlyCollection<DiscussionMessage> Messages => _messages.AsReadOnly();

    private Discussion() { }

    public Discussion(MovieRef movie, string topic)
    {
        ArgumentNullException.ThrowIfNull(movie);
        if (string.IsNullOrWhiteSpace(topic))
            throw new DomainException("Topic is required");
        if (topic.Length > 300)
            throw new DomainException("Topic must not exceed 300 characters");

        _movieId = movie.MovieId;
        _movieTitle = movie.MovieTitle;
        Topic = topic.Trim();
    }

    /// <summary>Додає повідомлення. У закрите обговорення писати не можна.</summary>
    public void Post(Reviewer reviewer, string text, int? replyTo = null)
    {
        if (IsClosed)
            throw new DomainException($"Discussion '{Topic}' is closed and does not accept new messages");
        if (replyTo is { } index && index >= _messages.Count)
            throw new DomainException($"Cannot reply to message {index}: it does not exist");

        _messages.Add(new DiscussionMessage(reviewer, text, replyTo));
        LastActivityAt = DateTime.UtcNow;
        Touch();
    }

    public void Close(string? reason = null)
    {
        if (IsClosed)
            throw new DomainException("Discussion is already closed");

        IsClosed = true;
        ClosedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();
    }

    public void Reopen()
    {
        if (!IsClosed)
            throw new DomainException("Discussion is not closed");

        IsClosed = false;
        ClosedReason = null;
        Touch();
    }
}

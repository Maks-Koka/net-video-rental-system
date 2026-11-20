using VideoRental.Reviews.Domain.Common;
using VideoRental.Reviews.Domain.Exceptions;

namespace VideoRental.Reviews.Domain.ValueObjects;

/// <summary>
/// Автор відгуку або коментаря — <b>вбудований об'єкт</b> у документі MongoDB
/// (поле <c>reviewer</c> зі схеми Лаб. 01).
/// </summary>
/// <remarks>
/// Незмінний: обидві властивості мають лише getter, валідація — в конструкторі.
/// <see cref="CustomerId"/> може бути <c>null</c> — це анонімний відгук
/// (саме такий документ є в seed-даних Лаб. 01).
/// Аналог <c>Address</c> в eShop.
/// </remarks>
public sealed class Reviewer : ValueObject
{
    /// <summary>Логічне посилання на <c>Customers.Id</c> у БД Оренди. Без FK.</summary>
    public long? CustomerId { get; }

    /// <summary>Імʼя, під яким опубліковано відгук. Знімок на момент публікації.</summary>
    public string DisplayName { get; }

    public Reviewer(long? customerId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("DisplayName is required");
        if (displayName.Length > 100)
            throw new DomainException("DisplayName must not exceed 100 characters");
        if (customerId is <= 0)
            throw new DomainException("CustomerId must be a positive value when provided");

        CustomerId = customerId;
        DisplayName = displayName.Trim();
    }

    /// <summary>Анонімний автор — без посилання на клієнта.</summary>
    public static Reviewer Anonymous(string displayName = "Анонім") => new(null, displayName);

    public bool IsAnonymous => CustomerId is null;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CustomerId;
        yield return DisplayName;
    }

    public override string ToString() => IsAnonymous ? DisplayName : $"{DisplayName} (#{CustomerId})";
}

/// <summary>
/// Оцінка від 1 до 5. Value Object, а не <c>int</c>: правило «1..5» живе
/// в одному місці й не може бути порушене десь у коді.
/// </summary>
public sealed class Rating : ValueObject
{
    public const int Min = 1;
    public const int Max = 5;

    public int Value { get; }

    public Rating(int value)
    {
        if (value is < Min or > Max)
            throw new DomainException($"Rating must be between {Min} and {Max}");

        Value = value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public static implicit operator int(Rating rating) => rating.Value;

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Посилання на фільм з контексту Каталогу разом із <b>денормалізованою</b>
/// назвою.
/// </summary>
/// <remarks>
/// Це прямий наслідок таблиці політики дублювання з Лаб. 01: контекст Відгуків
/// зберігає лише <see cref="MovieId"/> (логічне посилання, без FOREIGN KEY) і
/// копію назви для читання, щоб показати список відгуків без жодного звернення
/// до сервісу Каталогу. Назва оновлюється подіями з Каталогу (Лаб. 08).
/// </remarks>
public sealed class MovieRef : ValueObject
{
    /// <summary>Той самий Id, що й <c>Movies.Id</c> у БД Каталогу. Власник — Каталог.</summary>
    public long MovieId { get; }

    /// <summary>Денормалізована назва фільму — копія для читання.</summary>
    public string MovieTitle { get; }

    public MovieRef(long movieId, string movieTitle)
    {
        if (movieId <= 0)
            throw new DomainException("MovieId must be a positive value");
        if (string.IsNullOrWhiteSpace(movieTitle))
            throw new DomainException("MovieTitle is required");

        MovieId = movieId;
        MovieTitle = movieTitle.Trim();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MovieId;
        yield return MovieTitle;
    }

    public override string ToString() => $"{MovieTitle} (#{MovieId})";
}

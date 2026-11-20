using VideoRental.Catalog.Domain.Exceptions;

namespace VideoRental.Catalog.Domain.Entities;

/// <summary>
/// Фільм — коренева сутність каталогу (аналог <c>CatalogItem</c> в eShop).
/// </summary>
/// <remarks>
/// <para>
/// <b>Бізнес-правила реалізовано МЕТОДАМИ сутності, а не відкритими setter-ами.</b>
/// Усі властивості мають <c>private set</c>, тому стан неможливо зіпсувати
/// «збоку»: ціну перевіряє конструктор і <see cref="ChangeDailyRate"/>, залишок
/// змінюють лише <see cref="RemoveStock"/> та <see cref="AddStock"/>. Так само
/// побудовано <c>CatalogItem</c> у референсному застосунку Microsoft eShop
/// (методи <c>RemoveStock</c> / <c>AddStock</c>).
/// </para>
/// <para>
/// <c>Id</c> цієї сутності — <b>власник ідентифікатора фільму в усій системі</b>:
/// саме він дублюється в <c>VideoRentalRentalsDb.Movies</c> (Проєкт №1) та в
/// <c>reviews.movieId</c> (MongoDB, Проєкт №3) — див. таблицю політики
/// дублювання в Лаб. 01.
/// </para>
/// </remarks>
public class Movie
{
    public long Id { get; private set; }
    public string Title { get; private set; } = "";
    public string Barcode { get; private set; } = "";
    public decimal DailyRate { get; private set; }
    public int AvailableDiscs { get; private set; }
    public int? ReleaseYear { get; private set; }
    public int DirectorId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    /// <summary>Токен оптимістичної конкурентності (<c>ROWVERSION</c> у SQL Server).</summary>
    public byte[]? RowVersion { get; private set; }

    // --- навігаційні властивості ---

    /// <summary>Режисер фільму. Зв'язок N:1 (з боку режисера — 1:N).</summary>
    public Director Director { get; private set; } = null!;

    /// <summary>Опис фільму. Зв'язок <b>1:1</b> (PK залежної таблиці = FK).</summary>
    public MovieDetail? Detail { get; private set; }

    /// <summary>Фізичні диски. Зв'язок <b>1:N</b>.</summary>
    public List<DiscCopy> Discs { get; private set; } = [];

    /// <summary>Жанри через проміжну сутність. Зв'язок <b>M:N</b> із власним полем.</summary>
    public List<MovieGenre> MovieGenres { get; private set; } = [];

    /// <summary>Конструктор для EF Core (матеріалізація з БД).</summary>
    private Movie() { }

    public Movie(string title, string barcode, decimal dailyRate, int directorId, int? releaseYear = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Title must not be empty");
        if (string.IsNullOrWhiteSpace(barcode))
            throw new DomainException("Barcode must not be empty");
        if (dailyRate <= 0)
            throw new DomainException("DailyRate must be positive");
        if (directorId <= 0)
            throw new DomainException("DirectorId must be a positive value");
        if (releaseYear is < 1400 or > 2100)
            throw new DomainException("ReleaseYear must be between 1400 and 2100");

        Title = title.Trim();
        Barcode = barcode.Trim();
        DailyRate = dailyRate;
        DirectorId = directorId;
        ReleaseYear = releaseYear;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Списання дисків. Інваріант перевіряє САМА сутність:
    /// не можна зняти більше, ніж доступно.
    /// </summary>
    public void RemoveStock(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be positive");
        if (AvailableDiscs < quantity)
            throw new DomainException(
                $"Only {AvailableDiscs} discs of '{Title}' are available, {quantity} requested");

        AvailableDiscs -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Надходження дисків.</summary>
    public void AddStock(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be positive");

        AvailableDiscs += quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Зміна тарифу. Нуль і відʼємні значення неприпустимі.</summary>
    public void ChangeDailyRate(decimal newFee)
    {
        if (newFee <= 0)
            throw new DomainException("DailyRate must be positive");

        DailyRate = newFee;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Rename(string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
            throw new DomainException("Title must not be empty");

        Title = newTitle.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Встановлює або замінює опис фільму (зв'язок 1:1).</summary>
    public void SetDetail(MovieDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        Detail = detail;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Додає фізичний диск (зв'язок 1:N) і збільшує залишок.</summary>
    public void AddDisc(DiscCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (Discs.Any(c => c.DiscNumber == copy.DiscNumber))
            throw new DomainException(
                $"Copy with inventory number '{copy.DiscNumber}' already exists for this movie");

        Discs.Add(copy);
        AddStock(1);
    }

    /// <summary>
    /// Призначає фільму жанр через проміжну сутність M:N.
    /// Повторне призначення того самого жанру — порушення інваріанту.
    /// </summary>
    public void AssignGenre(int genreId, bool isPrimary = false, string? addedBy = null)
    {
        if (MovieGenres.Any(bg => bg.GenreId == genreId))
            throw new DomainException($"Genre {genreId} is already assigned to movie '{Title}'");

        if (isPrimary)
            foreach (var existing in MovieGenres)
                existing.ClearPrimary();     // головний жанр може бути лише один

        MovieGenres.Add(new MovieGenre(genreId, isPrimary, addedBy));
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>М'яке видалення: запис лишається в базі для цілісності історії.</summary>
    public void MarkDeleted()
    {
        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }
}

using VideoRental.Catalog.Domain.Entities;
using VideoRental.Catalog.Domain.Exceptions;

namespace VideoRental.Catalog.Tests;

/// <summary>
/// Тести інваріантів сутностей — головна відмінність Лаб. 03 від Лаб. 02:
/// бізнес-правила реалізовано МЕТОДАМИ сутності, а не відкритими setter-ами,
/// тому їх можна перевірити взагалі без бази даних і без моків.
/// </summary>
public class MovieEntityTests
{
    private static Movie NewMovie(decimal fee = 10m) =>
        new("Тіні забутих предків", "482-001-00-0001-5", fee, directorId: 1, releaseYear: 1840);

    // ------------------------------------------------------- конструктор ---

    [Fact]
    public void Constructor_Sets_Fields_AndTrimsStrings()
    {
        var movie = new Movie("  Тіні забутих предків  ", " 482-001-00-0001-5 ", 3.5m, 1, 1840);

        Assert.Equal("Тіні забутих предків", movie.Title);
        Assert.Equal("482-001-00-0001-5", movie.Barcode);
        Assert.Equal(3.5m, movie.DailyRate);
        Assert.Equal(0, movie.AvailableDiscs);
        Assert.False(movie.IsDeleted);
        Assert.NotEqual(default, movie.CreatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Constructor_Rejects_NonPositiveFee(decimal fee)
    {
        var ex = Assert.Throws<DomainException>(
            () => new Movie("Тіні забутих предків", "482-001-00-0001-5", fee, 1));

        Assert.Contains("DailyRate", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Rejects_EmptyTitle(string title)
        => Assert.Throws<DomainException>(() => new Movie(title, "482-001-00-0001-5", 5m, 1));

    [Theory]
    [InlineData(1399)]
    [InlineData(2101)]
    public void Constructor_Rejects_ImpossibleReleaseYear(int year)
        => Assert.Throws<DomainException>(() => new Movie("Тіні забутих предків", "482-001-00-0001-5", 5m, 1, year));

    // ------------------------------------------------------------ залишок ---

    [Fact]
    public void AddStock_IncreasesAvailableDiscs()
    {
        var movie = NewMovie();

        movie.AddStock(5);
        movie.AddStock(3);

        Assert.Equal(8, movie.AvailableDiscs);
        Assert.NotNull(movie.UpdatedAt);
    }

    [Fact]
    public void RemoveStock_DecreasesAvailableDiscs()
    {
        var movie = NewMovie();
        movie.AddStock(10);

        movie.RemoveStock(4);

        Assert.Equal(6, movie.AvailableDiscs);
    }

    /// <summary>
    /// КЛЮЧОВИЙ інваріант: не можна списати більше, ніж доступно.
    /// Саме так побудовано <c>CatalogItem.RemoveStock</c> в eShop.
    /// </summary>
    [Fact]
    public void RemoveStock_Throws_WhenNotEnoughDiscs()
    {
        var movie = NewMovie();
        movie.AddStock(2);

        var ex = Assert.Throws<DomainException>(() => movie.RemoveStock(5));

        Assert.Contains("Only 2 discs", ex.Message);
        Assert.Equal(2, movie.AvailableDiscs);      // стан НЕ змінився
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RemoveStock_Throws_OnNonPositiveQuantity(int quantity)
    {
        var movie = NewMovie();
        movie.AddStock(10);

        Assert.Throws<DomainException>(() => movie.RemoveStock(quantity));
        Assert.Equal(10, movie.AvailableDiscs);
    }

    // --------------------------------------------------------------- тариф ---

    [Fact]
    public void ChangeDailyRate_Updates_Fee()
    {
        var movie = NewMovie(10m);

        movie.ChangeDailyRate(12.75m);

        Assert.Equal(12.75m, movie.DailyRate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ChangeDailyRate_Throws_OnNonPositive(decimal fee)
    {
        var movie = NewMovie(10m);

        Assert.Throws<DomainException>(() => movie.ChangeDailyRate(fee));
        Assert.Equal(10m, movie.DailyRate);           // стан НЕ змінився
    }

    // ----------------------------------------------------------- диски ---

    [Fact]
    public void AddDisc_AddsCopy_AndIncreasesStock()
    {
        var movie = NewMovie();

        movie.AddDisc(new DiscCopy("DISC-0001", "A-01"));

        Assert.Single(movie.Discs);
        Assert.Equal(1, movie.AvailableDiscs);      // залишок узгоджений із дисками
    }

    /// <summary>Інвентарний номер унікальний у межах фільму — це інваріант сутності.</summary>
    [Fact]
    public void AddDisc_Throws_OnDuplicateDiscNumber()
    {
        var movie = NewMovie();
        movie.AddDisc(new DiscCopy("DISC-0001"));

        var ex = Assert.Throws<DomainException>(() => movie.AddDisc(new DiscCopy("DISC-0001")));

        Assert.Contains("DISC-0001", ex.Message);
        Assert.Single(movie.Discs);
        Assert.Equal(1, movie.AvailableDiscs);
    }

    // ---------------------------------------------------------------- жанри ---

    [Fact]
    public void AssignGenre_AddsJunctionEntity_WithOwnFields()
    {
        var movie = NewMovie();

        movie.AssignGenre(genreId: 1, isPrimary: true, addedBy: "test");

        var link = Assert.Single(movie.MovieGenres);
        Assert.Equal(1, link.GenreId);
        Assert.True(link.IsPrimary);
        Assert.Equal("test", link.AddedBy);
        Assert.NotEqual(default, link.AddedAt);     // власне поле M:N
    }

    [Fact]
    public void AssignGenre_Throws_OnDuplicateGenre()
    {
        var movie = NewMovie();
        movie.AssignGenre(1);

        Assert.Throws<DomainException>(() => movie.AssignGenre(1));
        Assert.Single(movie.MovieGenres);
    }

    /// <summary>Головний жанр може бути лише один.</summary>
    [Fact]
    public void AssignGenre_AsPrimary_ClearsPreviousPrimary()
    {
        var movie = NewMovie();
        movie.AssignGenre(1, isPrimary: true);
        movie.AssignGenre(2, isPrimary: true);

        Assert.Single(movie.MovieGenres, bg => bg.IsPrimary);
        Assert.True(movie.MovieGenres.Single(bg => bg.GenreId == 2).IsPrimary);
        Assert.False(movie.MovieGenres.Single(bg => bg.GenreId == 1).IsPrimary);
    }

    // ------------------------------------------------------- м'яке видалення ---

    [Fact]
    public void MarkDeleted_SetsFlag()
    {
        var movie = NewMovie();

        movie.MarkDeleted();

        Assert.True(movie.IsDeleted);
    }
}

public class DirectorEntityTests
{
    [Fact]
    public void Constructor_Rejects_EmptyName()
        => Assert.Throws<DomainException>(() => new Director("  "));

    [Theory]
    [InlineData(999)]
    [InlineData(2101)]
    public void Constructor_Rejects_ImpossibleBirthYear(int year)
        => Assert.Throws<DomainException>(() => new Director("Сергій Параджанов", "Україна", year));

    [Fact]
    public void MarkDeleted_Succeeds_WhenNoMovies()
    {
        var director = new Director("Сергій Параджанов");

        director.MarkDeleted();

        Assert.True(director.IsDeleted);
    }

    /// <summary>
    /// Інваріант: режисера з фільмами видалити не можна.
    /// Саме цей виняток сервіс перетворює на HTTP 409.
    /// </summary>
    [Fact]
    public void MarkDeleted_Throws_WhenDirectorStillHasMovies()
    {
        var director = new Director("Сергій Параджанов");
        director.Movies.Add(new Movie("Тіні забутих предків", "482-001-00-0001-5", 3.5m, 1));

        var ex = Assert.Throws<DomainException>(() => director.MarkDeleted());

        Assert.Contains("cannot be deleted", ex.Message);
        Assert.False(director.IsDeleted);
    }
}

public class DiscCopyEntityTests
{
    [Fact]
    public void Constructor_Rejects_EmptyDiscNumber()
        => Assert.Throws<DomainException>(() => new DiscCopy(""));

    [Fact]
    public void Constructor_Rejects_UnknownCondition()
        => Assert.Throws<DomainException>(() => new DiscCopy("DISC-1", condition: "Броньований"));

    [Theory]
    [InlineData("New")]
    [InlineData("Good")]
    [InlineData("Worn")]
    [InlineData("Damaged")]
    [InlineData("Lost")]
    public void Constructor_Accepts_AllKnownConditions(string condition)
    {
        var copy = new DiscCopy("DISC-1", condition: condition);
        Assert.Equal(condition, copy.Condition);
    }

    [Fact]
    public void ChangeCondition_Throws_OnUnknownValue()
    {
        var copy = new DiscCopy("DISC-1");

        Assert.Throws<DomainException>(() => copy.ChangeCondition("Невідомо"));
        Assert.Equal("Good", copy.Condition);
    }
}

public class MovieDetailEntityTests
{
    [Fact]
    public void Constructor_Rejects_EmptyDescription()
        => Assert.Throws<DomainException>(() => new MovieDetail("   "));

    [Fact]
    public void Constructor_Rejects_NonPositiveDurationMinutes()
        => Assert.Throws<DomainException>(() => new MovieDetail("Опис", durationMinutes: 0));

    [Fact]
    public void Constructor_Defaults_LanguageToUk()
    {
        var detail = new MovieDetail("Опис");
        Assert.Equal("uk", detail.Language);
    }
}

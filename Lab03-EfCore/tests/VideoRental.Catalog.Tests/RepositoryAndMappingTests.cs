using AutoMapper;
using VideoRental.Catalog.Bll.Dtos;
using VideoRental.Catalog.Bll.Mapping;
using VideoRental.Catalog.Bll.Validators;
using VideoRental.Catalog.Dal;
using VideoRental.Catalog.Dal.Repositories;
using VideoRental.Catalog.Dal.Specifications;
using VideoRental.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace VideoRental.Catalog.Tests;

/// <summary>
/// Перевірка конфігурації AutoMapper та проєкції сутностей у DTO.
/// </summary>
public class MappingTests
{
    private static IMapper CreateMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<CatalogProfile>(),
                                NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public void Configuration_IsValid()
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<CatalogProfile>(), NullLoggerFactory.Instance);

        config.AssertConfigurationIsValid();
    }

    [Fact]
    public void Maps_Movie_To_MovieDto_WithDirectorAndGenres()
    {
        var mapper = CreateMapper();
        var movie = new Movie("Тіні забутих предків", "482-001-00-0001-5", 3.5m, 1, 1840);
        movie.SetDetail(new MovieDetail("Опис", "Кіностудія Довженка", 368));
        movie.AddDisc(new DiscCopy("DISC-0001", "A-01"));

        var dto = mapper.Map<MovieDto>(movie);

        Assert.Equal("Тіні забутих предків", dto.Title);
        Assert.Equal(3.5m, dto.DailyRate);
        Assert.Equal(1, dto.AvailableDiscs);
        Assert.NotNull(dto.Detail);
        Assert.Equal("Кіностудія Довженка", dto.Detail!.Studio);
        Assert.Single(dto.Discs);
        Assert.Equal("", dto.DirectorName);            // Director не завантажено — порожній рядок, не виняток
    }

    [Fact]
    public void Maps_Director_To_DirectorDto()
    {
        var mapper = CreateMapper();
        var director = new Director("Антоніо Лукіч", "Україна", 1930);

        var dto = mapper.Map<DirectorDto>(director);

        Assert.Equal("Антоніо Лукіч", dto.FullName);
        Assert.Equal(1930, dto.BirthYear);
    }
}

/// <summary>
/// Тести репозиторіїв і специфікацій на провайдері InMemory.
/// </summary>
/// <remarks>
/// InMemory не перевіряє SQL-обмеження, тому ним тестуємо саме ЛОГІКУ вибірок
/// (фільтри, сортування, пагінацію, запит через проміжну сутність M:N).
/// Обмеження БД і реальні SQL-запити перевіряє скрипт <c>tests/api-tests.py</c>
/// проти справжнього SQL Server.
/// </remarks>
public class RepositoryTests : IDisposable
{
    private readonly CatalogDbContext _context;

    public RepositoryTests()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"catalog-tests-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new CatalogDbContext(options);
        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        var comedy = new Genre("Комедія", "comedy");
        var drama = new Genre("Драма", "drama");
        _context.Genres.AddRange(comedy, drama);
        await _context.SaveChangesAsync();

        var shevchenko = new Director("Сергій Параджанов", "Україна", 1814);
        var zhadan = new Director("Олесь Санін", "Україна", 1974);
        _context.Directors.AddRange(shevchenko, zhadan);
        await _context.SaveChangesAsync();

        var kobzar = new Movie("Тіні забутих предків", "482-001-00-0001-5", 3.50m, shevchenko.Id, 1840);
        kobzar.AddStock(5);
        kobzar.AssignGenre(comedy.Id, isPrimary: true);

        var internat = new Movie("Поводир", "482-001-00-0004-9", 5.00m, zhadan.Id, 2017);
        internat.AssignGenre(drama.Id, isPrimary: true);     // залишок навмисно 0

        var misto = new Movie("Пропала грамота", "482-001-00-0003-1", 4.25m, zhadan.Id, 1928);
        misto.AddStock(2);
        misto.AssignGenre(drama.Id);

        _context.Movies.AddRange(kobzar, internat, misto);
        await _context.SaveChangesAsync();

        // Після сідінгу всі сутності лишаються у ChangeTracker, і EF Core
        // автоматично «склеює» навігації між відстежуваними об'єктами
        // (identity resolution). Тоді Director.Movies виявився б заповненим ще
        // ДО Explicit Loading — і тест перевіряв би не те. Очищаємо трекер,
        // щоб кожен тест читав з «чистого» контексту, як це робить застосунок.
        _context.ChangeTracker.Clear();
    }

    public void Dispose() => _context.Dispose();

    // ------------------------------------------------------ Eager Loading ---

    [Fact]
    public async Task MovieRepository_GetWithDetails_LoadsRelatedData()
    {
        var repo = new MovieRepository(_context);
        var id = _context.Movies.First(b => b.Barcode == "482-001-00-0001-5").Id;

        var movie = await repo.GetWithDetailsAsync(id);

        Assert.NotNull(movie);
        Assert.NotNull(movie!.Director);
        Assert.Equal("Сергій Параджанов", movie.Director.FullName);
        Assert.Single(movie.MovieGenres);
    }

    [Fact]
    public async Task MovieRepository_ExistsByBarcode_Works()
    {
        var repo = new MovieRepository(_context);

        Assert.True(await repo.ExistsByBarcodeAsync("482-001-00-0001-5"));
        Assert.False(await repo.ExistsByBarcodeAsync("000-000-00-0000-0"));
    }

    // --------------------------------------------------- Explicit Loading ---

    [Fact]
    public async Task DirectorRepository_ExplicitLoading_LoadsMoviesOnlyWhenRequested()
    {
        var repo = new DirectorRepository(_context);
        var id = _context.Directors.First(a => a.FullName == "Олесь Санін").Id;

        var without = await repo.GetWithMoviesAsync(id, loadMovies: false);
        Assert.NotNull(without);
        Assert.Empty(without!.Movies);

        _context.ChangeTracker.Clear();

        var with = await repo.GetWithMoviesAsync(id, loadMovies: true);
        Assert.NotNull(with);
        Assert.Equal(2, with!.Movies.Count);
    }

    // ------------------------------------- LINQ через проміжну сутність M:N ---

    [Fact]
    public async Task MovieGenreRepository_GetRecentlyAdded_FiltersByJunctionOwnField()
    {
        var repo = new MovieGenreRepository(_context);

        var recent = await repo.GetRecentlyAddedAsync("Комедія", DateTime.UtcNow.AddDays(-1));
        Assert.Single(recent);
        Assert.Equal("Тіні забутих предків", recent[0].Title);

        // той самий запит, але з майбутньої дати -> порожньо (працює фільтр AddedAt)
        var none = await repo.GetRecentlyAddedAsync("Комедія", DateTime.UtcNow.AddDays(1));
        Assert.Empty(none);
    }

    [Fact]
    public async Task MovieGenreRepository_GetGenreStats_CountsMoviesPerGenre()
    {
        var repo = new MovieGenreRepository(_context);

        var stats = await repo.GetGenreStatsAsync();

        Assert.Equal(2, stats.Count);
        Assert.Equal(2, stats.Single(s => s.GenreName == "Драма").MovieCount);
        Assert.Equal(1, stats.Single(s => s.GenreName == "Комедія").MovieCount);
    }

    // ------------------------------------------------ Specification pattern ---

    [Fact]
    public async Task Specification_Filters_BySearchTerm()
    {
        var repo = new MovieRepository(_context);

        var items = await repo.ListAsync(new MoviesFilterSpec(new MovieQuery(Search: "Тіні забутих предків")));

        Assert.Single(items);
        Assert.Equal("Тіні забутих предків", items[0].Title);
    }

    [Fact]
    public async Task Specification_Filters_ByFeeRange()
    {
        var repo = new MovieRepository(_context);

        var items = await repo.ListAsync(new MoviesFilterSpec(new MovieQuery(MinRate: 4m, MaxRate: 5m)));

        Assert.Equal(2, items.Count);
        Assert.All(items, b => Assert.InRange(b.DailyRate, 4m, 5m));
    }

    [Fact]
    public async Task Specification_Sorts_ByFeeDescending()
    {
        var repo = new MovieRepository(_context);

        var items = await repo.ListAsync(
            new MoviesFilterSpec(new MovieQuery(SortBy: "fee", SortDir: "desc")));

        var fees = items.Select(b => b.DailyRate).ToList();
        Assert.Equal(fees.OrderByDescending(f => f).ToList(), fees);
    }

    /// <summary>
    /// <c>CountAsync</c> має рахувати ВСІ записи за критеріями, ігноруючи
    /// <c>Skip</c>/<c>Take</c> — інакше totalCount показував би розмір хвилини.
    /// </summary>
    [Fact]
    public async Task Specification_Count_IgnoresPaging()
    {
        var repo = new MovieRepository(_context);
        var query = new MovieQuery(Page: 1, PageSize: 2);

        var page = await repo.ListAsync(new MoviesFilterSpec(query, applyPaging: true));
        var total = await repo.CountAsync(new MoviesFilterSpec(query, applyPaging: false));

        Assert.Equal(2, page.Count);     // на сторінці
        Assert.Equal(3, total);          // усього в базі
    }

    /// <summary>
    /// ДРУГА специфікація працює через той самий <c>ListAsync</c>
    /// без жодних змін у репозиторії.
    /// </summary>
    [Fact]
    public async Task SecondSpecification_WorksThroughSameRepositoryMethod()
    {
        var repo = new MovieRepository(_context);

        var outOfStock = await repo.ListAsync(new OutOfStockMoviesSpec());

        Assert.Single(outOfStock);
        Assert.Equal("Поводир", outOfStock[0].Title);
    }

    [Fact]
    public async Task ThirdSpecification_FiltersByGenreSlug()
    {
        var repo = new MovieRepository(_context);

        var items = await repo.ListAsync(new MoviesByGenreSlugSpec("drama"));

        Assert.Equal(2, items.Count);
    }

    // ---------------------------------------------- ідемпотентність сідінгу ---

    [Fact]
    public async Task Seeding_IsIdempotent()
    {
        var before = await _context.Movies.CountAsync();

        await DbInitializer.SeedAsync(_context);     // фільми вже є -> має вийти одразу
        await DbInitializer.SeedAsync(_context);

        Assert.Equal(before, await _context.Movies.CountAsync());
    }
}

/// <summary>Тести валідаторів FluentValidation — без бази й без HTTP.</summary>
public class ValidatorTests
{
    [Theory]
    [InlineData("482-001-00-0001-5", true)]
    [InlineData("1234567890", false)]
    [InlineData("482-001-00-0001", false)]
    [InlineData("", false)]
    public void CreateMovieDtoValidator_ChecksBarcodeFormat(string barcode, bool expectedValid)
    {
        var validator = new CreateMovieDtoValidator();
        var dto = new CreateMovieDto { Title = "Назва", Barcode = barcode, DailyRate = 5m, DirectorId = 1 };

        var result = validator.Validate(dto);

        Assert.Equal(expectedValid, result.Errors.All(e => e.PropertyName != nameof(dto.Barcode)));
    }

    [Fact]
    public void CreateMovieDtoValidator_RequiresDescription_WhenStudioGiven()
    {
        var validator = new CreateMovieDtoValidator();
        var dto = new CreateMovieDto
        {
            Title = "Назва", Barcode = "482-001-00-0001-5", DailyRate = 5m,
            DirectorId = 1, Studio = "Кіностудія Довженка"       // опису немає
        };

        var result = validator.Validate(dto);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(dto.Description));
    }

    [Fact]
    public void CreateMovieDtoValidator_RejectsDuplicateGenres()
    {
        var validator = new CreateMovieDtoValidator();
        var dto = new CreateMovieDto
        {
            Title = "Назва", Barcode = "482-001-00-0001-5", DailyRate = 5m,
            DirectorId = 1, GenreIds = [1, 1, 2]
        };

        var result = validator.Validate(dto);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(dto.GenreIds));
    }

    [Theory]
    [InlineData("title", true)]
    [InlineData("fee", true)]
    [InlineData("drop_table", false)]
    [InlineData("'; DELETE FROM Movies --", false)]
    public void MovieQueryValidator_AllowsOnlyWhitelistedSortFields(string sortBy, bool expectedValid)
    {
        var validator = new MovieQueryValidator();

        var result = validator.Validate(new MovieQuery(SortBy: sortBy));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void MovieQueryValidator_RejectsPageSizeAboveLimit()
    {
        var validator = new MovieQueryValidator();

        var result = validator.Validate(new MovieQuery(PageSize: 5000));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void MovieQueryValidator_RejectsInvertedFeeRange()
    {
        var validator = new MovieQueryValidator();

        var result = validator.Validate(new MovieQuery(MinRate: 50m, MaxRate: 10m));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AddDiscDtoValidator_RejectsNonLatinDiscNumber()
    {
        var validator = new AddDiscDtoValidator();

        var result = validator.Validate(new AddDiscDto { DiscNumber = "інв-1", Condition = "Good" });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AddDiscDtoValidator_RejectsFutureAcquisitionDate()
    {
        var validator = new AddDiscDtoValidator();

        var result = validator.Validate(new AddDiscDto
        {
            DiscNumber = "DISC-1",
            Condition = "Good",
            AcquiredAt = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10))
        });

        Assert.False(result.IsValid);
    }
}

/// <summary>Тести контракту посторінкової відповіді.</summary>
public class PagedResultTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(6, 5, 2)]
    [InlineData(10, 5, 2)]
    [InlineData(11, 5, 3)]
    public void TotalPages_IsCeilingOfDivision(int total, int pageSize, int expected)
    {
        var result = new PagedResult<string>([], total, 1, pageSize);
        Assert.Equal(expected, result.TotalPages);
    }

    [Fact]
    public void HasNext_And_HasPrevious_ReflectPosition()
    {
        var first = new PagedResult<string>([], 20, 1, 5);
        Assert.True(first.HasNext);
        Assert.False(first.HasPrevious);

        var middle = new PagedResult<string>([], 20, 2, 5);
        Assert.True(middle.HasNext);
        Assert.True(middle.HasPrevious);

        var last = new PagedResult<string>([], 20, 4, 5);
        Assert.False(last.HasNext);
        Assert.True(last.HasPrevious);
    }

    [Theory]
    [InlineData(0, MovieQuery.DefaultPageSize)]
    [InlineData(-5, MovieQuery.DefaultPageSize)]
    [InlineData(50, 50)]
    [InlineData(5000, MovieQuery.MaxPageSize)]
    public void MovieQuery_SafePageSize_IsClamped(int given, int expected)
        => Assert.Equal(expected, new MovieQuery(PageSize: given).SafePageSize);
}

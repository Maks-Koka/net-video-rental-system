using VideoRental.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace VideoRental.Catalog.Dal;

/// <summary>
/// Програмний (другий) спосіб сідінгу — доповнює статичний
/// <c>HasData</c> у конфігураціях.
/// </summary>
/// <remarks>
/// <para>
/// <b>Розподіл обовʼязків:</b>
/// <list type="bullet">
///   <item><b>Модельний (статичний) сідінг</b> — <c>HasData</c> у
///         <c>DirectorConfiguration</c> і <c>GenreConfiguration</c>: довідники з
///         фіксованими Id, якими керують міграції;</item>
///   <item><b>Програмний сідінг</b> — цей клас: сутності з навігаціями
///         (<c>Movie</c> + <c>MovieDetail</c> + <c>DiscCopy</c> + <c>MovieGenre</c>),
///         які через <c>HasData</c> задавати незручно.</item>
/// </list>
/// </para>
/// <para>
/// <b>ІДЕМПОТЕНТНІСТЬ:</b> перед вставкою перевіряється наявність запису за
/// бізнес-ключем (Barcode), тому повторний запуск застосунку дублікатів НЕ створює.
/// Дотримано цілісність FK: довідники (Directors, Genres) уже існують завдяки
/// міграції, фільми додаються після них.
/// </para>
/// <para>
/// Сідінг сумісний із CI/CD — його можна вимкнути в production
/// (<c>Database:SeedOnStartup</c>).
/// </para>
/// </remarks>
public static class DbInitializer
{
    public static async Task SeedAsync(
        CatalogDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        // ідемпотентність: якщо фільми вже є, нічого не робимо
        if (await db.Movies.AnyAsync(ct))
        {
            logger?.LogInformation("Сідінг пропущено: каталог уже наповнено.");
            return;
        }

        logger?.LogInformation("Наповнюю каталог початковими даними...");

        var movies = new[]
        {
            Create("Тіні забутих предків", "482-001-00-0001-5", 3.50m, directorId: 1, year: 1965,
                   description: "Екранізація повісті Коцюбинського — вершина українського поетичного кіно.",
                   studio: "Кіностудія Довженка", pages: 368,
                   discs: [("DISC-0001", "A-01", DiscCondition.Good), ("DISC-0002", "A-01", DiscCondition.New)],
                   genres: [(1, true), (4, false)]),

            Create("Земля", "482-001-00-0002-3", 4.00m, directorId: 2, year: 1930,
                   description: "Німа класика Довженка про українське село на зламі епох.",
                   studio: "Київнаукфільм", pages: 304,
                   discs: [("DISC-0003", "A-02", DiscCondition.Good)],
                   genres: [(2, true), (4, false)]),

            Create("Пропала грамота", "482-001-00-0003-1", 4.25m, directorId: 3, year: 1972,
                   description: "Музична комедія за мотивами творів Гоголя про козака-характерника.",
                   studio: "Довженко-Центр", pages: 288,
                   discs: [("DISC-0004", "B-01", DiscCondition.Worn)],
                   genres: [(3, true), (4, false)]),

            Create("Поводир", "482-001-00-0004-9", 5.00m, directorId: 4, year: 2014,
                   description: "Історична драма про сліпого кобзаря та хлопчика в Україні 1930-х.",
                   studio: "Pronto Film", pages: 336,
                   discs: [("DISC-0005", "B-02", DiscCondition.New)],
                   genres: [(3, true)]),

            Create("Кіборги", "482-001-00-0005-7", 4.75m, directorId: 5, year: 2017,
                   description: "Воєнна драма про оборону Донецького аеропорту.",
                   studio: "Idas Film", pages: 432,
                   discs: [("DISC-0006", "C-01", DiscCondition.Good)],
                   genres: [(2, true), (3, false)]),

            Create("Мої думки тихі", "482-001-00-0006-5", 5.50m, directorId: 6, year: 2019,
                   description: "Road-movie про пошук рідкісної качки в Закарпатті.",
                   studio: "ToyCinema", pages: 416,
                   discs: [("DISC-0007", "C-02", DiscCondition.Good)],
                   genres: [(5, true)]),
        };

        foreach (var movie in movies)
        {
            // друга лінія захисту від дублікатів — перевірка за бізнес-ключем
            if (await db.Movies.AnyAsync(b => b.Barcode == movie.Barcode, ct))
                continue;

            await db.Movies.AddAsync(movie, ct);
        }

        var saved = await db.SaveChangesAsync(ct);
        logger?.LogInformation("Сідінг завершено: збережено {Count} змін.", saved);
    }

    private static Movie Create(
        string title, string barcode, decimal fee, int directorId, int year,
        string description, string studio, int pages,
        (string Inv, string Shelf, string Condition)[] discs,
        (int GenreId, bool IsPrimary)[] genres)
    {
        var movie = new Movie(title, barcode, fee, directorId, year);

        movie.SetDetail(new MovieDetail(description, studio, pages));

        // AddDisc сам збільшує AvailableDiscs — залишок завжди узгоджений
        // із кількістю фізичних дисків
        foreach (var (inv, shelf, condition) in discs)
            movie.AddDisc(new DiscCopy(inv, shelf, condition));

        foreach (var (genreId, isPrimary) in genres)
            movie.AssignGenre(genreId, isPrimary, addedBy: "seed");

        return movie;
    }
}

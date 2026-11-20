using System.Data;
using Dapper;
using VideoRental.Rentals.Dal.Abstractions;
using VideoRental.Rentals.Dal.Infrastructure;
using VideoRental.Rentals.Domain.Entities;
using Microsoft.Data.SqlClient;

namespace VideoRental.Rentals.Dal.Repositories;

/// <summary>
/// Репозиторій локальної копії довідника фільмів — <b>ADO.NET + Dapper</b>.
/// </summary>
/// <remarks>
/// <see cref="DecreaseDiscsAsync"/> — це ДРУГИЙ репозиторій транзакційного
/// сценарію «підтвердити й видати»: якщо тут виникне помилка (брак
/// дисків), зміни першого репозиторію (статус оренди) відкотяться
/// разом із усією транзакцією Unit of Work.
/// </remarks>
public sealed class MovieRepository : BaseDapperRepository<Movie>, IMovieRepository
{
    public MovieRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction, "dbo.Movies") { }

    protected override string SelectColumns =>
        "Id, Title, Director, Barcode, DailyRate, DiscsAvailable, SyncedAt";

    public async Task<IReadOnlyList<Movie>> SearchAsync(string? title, CancellationToken ct = default)
    {
        // індекс IX_Movies_Title обслуговує саме цей запит.
        // Значення передається ПАРАМЕТРОМ — ніякої конкатенації з вводом.
        const string sql = """
            SELECT  Id, Title, Director, Barcode, DailyRate, DiscsAvailable, SyncedAt
            FROM    dbo.Movies
            WHERE   @Title IS NULL OR Title LIKE '%' + @Title + '%'
            ORDER BY Title
            """;

        await EnsureOpenAsync(ct);
        var rows = await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.QueryAsync<Movie>(Command(sql, new { Title = title }, ct)),
            "Movies.Search");
        return rows.ToList();
    }

    /// <summary>Зменшення залишку через <c>usp_DecreaseMovieDiscs</c> (перевірка + UPDLOCK у БД).</summary>
    public async Task DecreaseDiscsAsync(long movieId, int quantity, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteAsync(Command(
                "dbo.usp_DecreaseMovieDiscs",
                new { MovieId = movieId, Quantity = quantity },
                ct, CommandType.StoredProcedure)),
            "Movies.DecreaseDiscs");
    }

    public async Task IncreaseDiscsAsync(long movieId, int quantity, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.Movies
            SET    DiscsAvailable = DiscsAvailable + @Quantity
            WHERE  Id = @MovieId
            """;

        await EnsureOpenAsync(ct);
        await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteAsync(Command(sql, new { MovieId = movieId, Quantity = quantity }, ct)),
            "Movies.IncreaseDiscs");
    }

    /// <summary>
    /// Синхронізація копії довідника з Каталогу.
    /// <b>DiscsAvailable свідомо НЕ перетирається</b>: це власні дані контексту
    /// Оренди, ними керують операції оренди/повернення (таблиця політики
    /// дублювання, Лаб. 01, рядок 3).
    /// </summary>
    public async Task UpsertFromCatalogAsync(Movie movie, CancellationToken ct = default)
    {
        const string sql = """
            MERGE dbo.Movies AS target
            USING (SELECT @Id AS Id, @Title AS Title, @Director AS Director,
                          @Barcode AS Barcode, @DailyRate AS DailyRate) AS src
            ON target.Id = src.Id
            WHEN MATCHED THEN
                UPDATE SET Title = src.Title, Director = src.Director, Barcode = src.Barcode,
                           DailyRate = src.DailyRate, SyncedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (Id, Title, Director, Barcode, DailyRate, DiscsAvailable)
                VALUES (src.Id, src.Title, src.Director, src.Barcode, src.DailyRate, @InitialDiscs);
            """;

        await EnsureOpenAsync(ct);
        await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteAsync(Command(sql, new
            {
                movie.Id, movie.Title, movie.Director, movie.Barcode, movie.DailyRate,
                InitialDiscs = movie.DiscsAvailable
            }, ct)),
            "Movies.UpsertFromCatalog");
    }

    public override async Task<long> AddAsync(Movie entity, CancellationToken ct = default)
    {
        // Id НЕ генерується тут: він приходить із БД Каталогу (власника).
        const string sql = """
            INSERT INTO dbo.Movies (Id, Title, Director, Barcode, DailyRate, DiscsAvailable)
            VALUES (@Id, @Title, @Director, @Barcode, @DailyRate, @DiscsAvailable);
            SELECT @Id;
            """;

        await EnsureOpenAsync(ct);
        return await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteScalarAsync<long>(Command(sql, new
            {
                entity.Id, entity.Title, entity.Director, entity.Barcode,
                entity.DailyRate, entity.DiscsAvailable
            }, ct)),
            "Movies.Add");
    }

    public override async Task<bool> UpdateAsync(Movie entity, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.Movies
            SET    Title = @Title, Director = @Director, Barcode = @Barcode,
                   DailyRate = @DailyRate, SyncedAt = SYSUTCDATETIME()
            WHERE  Id = @Id
            """;

        await EnsureOpenAsync(ct);
        var affected = await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteAsync(Command(sql, new
            {
                entity.Id, entity.Title, entity.Director, entity.Barcode, entity.DailyRate
            }, ct)),
            "Movies.Update");
        return affected > 0;
    }
}

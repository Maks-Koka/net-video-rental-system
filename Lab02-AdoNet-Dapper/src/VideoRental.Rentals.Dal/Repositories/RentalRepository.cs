using System.Data;
using System.Text.Json;
using Dapper;
using VideoRental.Rentals.Dal.Abstractions;
using VideoRental.Rentals.Dal.Infrastructure;
using VideoRental.Rentals.Domain.Entities;
using Microsoft.Data.SqlClient;

namespace VideoRental.Rentals.Dal.Repositories;

/// <summary>
/// Репозиторій оренд на <b>ADO.NET + Dapper</b>.
/// </summary>
/// <remarks>
/// Демонструє:
/// <list type="bullet">
///   <item><b>multi-mapping</b> — оренда з позиціями одним запитом (<c>GetWithItemsAsync</c>);</item>
///   <item><b>виклики збережуваних процедур</b> із Лаб. 01, у тому числі з OUTPUT-параметром;</item>
///   <item>успадкування спільного CRUD від <see cref="BaseDapperRepository{T}"/>.</item>
/// </list>
/// Усі методи асинхронні (<c>QueryAsync</c>, <c>ExecuteAsync</c>) і працюють
/// через з'єднання та транзакцію, які надає Unit of Work.
/// </remarks>
public sealed class RentalRepository : BaseDapperRepository<Rental>, IRentalRepository
{
    public RentalRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction, "dbo.Rentals") { }

    protected override string SelectColumns => """
        Id, CustomerId, Status, RentalDate, DueDate, PickedUpAt, ReturnedAt, LateFeeAmount,
        CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted
        """;

    protected override string NotDeletedFilter => "IsDeleted = 0";

    /// <summary>
    /// Оренда разом із позиціями — ОДНИМ запитом із LEFT JOIN.
    /// Dapper multi-mapping: кожен рядок результату розбивається на
    /// <see cref="Rental"/> + один <see cref="RentalItem"/> починаючи зі стовпця,
    /// указаного в <c>splitOn</c>.
    /// </summary>
    public async Task<Rental?> GetWithItemsAsync(long rentalId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT  l.Id, l.CustomerId, l.Status, l.RentalDate, l.DueDate,
                    l.PickedUpAt, l.ReturnedAt, l.LateFeeAmount, l.IsDeleted,
                    d.RentalId, d.PickupStore, d.ClerkName, d.Comment,
                    i.RentalId, i.MovieId, i.MovieTitle, i.MovieDirector,
                    i.DailyRate, i.Units, i.Discount
            FROM        dbo.Rentals       AS l
            LEFT  JOIN  dbo.RentalDetails AS d ON d.RentalId = l.Id
            LEFT  JOIN  dbo.RentalItems   AS i ON i.RentalId = l.Id
            WHERE   l.Id = @RentalId AND l.IsDeleted = 0
            ORDER BY i.MovieId
            """;

        await EnsureOpenAsync(ct);

        Rental? result = null;

        await SqlExceptionTranslator.ExecuteAsync(async () =>
        {
            await Connection.QueryAsync<Rental, RentalDetail?, RentalItem?, Rental>(
                Command(sql, new { RentalId = rentalId }, ct),
                (rental, detail, item) =>
                {
                    // перший рядок визначає корінь агрегату
                    result ??= rental;
                    result.Detail ??= detail;

                    // LEFT JOIN дає NULL-позицію, якщо позицій немає взагалі
                    if (item is not null && result.Items.All(x => x.MovieId != item.MovieId))
                        result.Items.Add(item);

                    return result;
                },
                // з цих стовпців починаються RentalDetail і RentalItem відповідно
                splitOn: "RentalId,RentalId");
        }, "Rentals.GetWithItems");

        return result;
    }

    public async Task<IReadOnlyList<Rental>> GetByCustomerAsync(
        long customerId, string? status, CancellationToken ct = default)
    {
        // індекс IX_Rentals_CustomerId_Status обслуговує саме цей запит
        const string sql = """
            SELECT  Id, CustomerId, Status, RentalDate, DueDate, PickedUpAt, ReturnedAt,
                    LateFeeAmount, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted
            FROM    dbo.Rentals
            WHERE   CustomerId = @CustomerId
              AND   IsDeleted = 0
              AND  (@Status IS NULL OR Status = @Status)
            ORDER BY RentalDate DESC
            """;

        await EnsureOpenAsync(ct);
        var rows = await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.QueryAsync<Rental>(Command(sql, new { CustomerId = customerId, Status = status }, ct)),
            "Rentals.GetByCustomer");
        return rows.ToList();
    }

    public async Task<IReadOnlyList<Rental>> GetOverdueAsync(DateOnly today, CancellationToken ct = default)
    {
        // фільтрований індекс IX_Rentals_Overdue обслуговує саме цей запит
        const string sql = """
            SELECT  Id, CustomerId, Status, RentalDate, DueDate, PickedUpAt, ReturnedAt,
                    LateFeeAmount, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted
            FROM    dbo.Rentals
            WHERE   Status = 'PickedUp' AND IsDeleted = 0 AND DueDate < @Today
            ORDER BY DueDate
            """;

        await EnsureOpenAsync(ct);
        var rows = await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.QueryAsync<Rental>(Command(sql, new { Today = today.ToDateTime(TimeOnly.MinValue) }, ct)),
            "Rentals.GetOverdue");
        return rows.ToList();
    }

    /// <summary>
    /// Створення оренди через <c>usp_CreateRental</c>: процедура в одній
    /// транзакції створює оренду, деталі та позиції зі ЗНІМКАМИ полів каталогу.
    /// Позиції передаються як JSON-масив (у процедурі — <c>OPENJSON</c>).
    /// </summary>
    public async Task<long> CreateViaProcedureAsync(
        long customerId, DateOnly dueDate, string pickupStore, string? comment,
        IEnumerable<(long MovieId, int Units)> items, string? createdBy,
        CancellationToken ct = default)
    {
        var itemsJson = JsonSerializer.Serialize(
            items.Select(i => new { MovieId = i.MovieId, Units = i.Units }));

        var parameters = new DynamicParameters();
        parameters.Add("@CustomerId",     customerId,     DbType.Int64);
        parameters.Add("@DueDate",      dueDate.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("@PickupStore", pickupStore, DbType.String, size: 100);
        parameters.Add("@Comment",      comment,      DbType.String, size: 1000);
        parameters.Add("@ItemsJson",    itemsJson,    DbType.String);
        parameters.Add("@CreatedBy",    createdBy,    DbType.String, size: 100);
        parameters.Add("@RentalDate",     null,         DbType.DateTime2);
        parameters.Add("@RentalId",       dbType: DbType.Int64, direction: ParameterDirection.Output);

        await EnsureOpenAsync(ct);
        await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteAsync(Command(
                "dbo.usp_CreateRental", parameters, ct, CommandType.StoredProcedure)),
            "Rentals.CreateViaProcedure");

        return parameters.Get<long>("@RentalId");
    }

    public Task ConfirmDiscsAsync(long rentalId, string? changedBy, CancellationToken ct = default) =>
        ExecuteProcedureAsync("dbo.usp_ConfirmRentalDiscs",
            new { RentalId = rentalId, ChangedBy = changedBy }, "Rentals.ConfirmDiscs", ct);

    public Task PickUpAsync(long rentalId, string? changedBy, CancellationToken ct = default) =>
        ExecuteProcedureAsync("dbo.usp_PickUpRental",
            new { RentalId = rentalId, ChangedBy = changedBy }, "Rentals.PickUp", ct);

    /// <summary>
    /// <c>usp_ReturnRental</c> з OUTPUT-параметром: процедура сама рахує пеню
    /// за прострочені дні, використовуючи ЗНІМОК тарифу з позицій оренди.
    /// </summary>
    public async Task<decimal> ReturnAsync(
        long rentalId, DateTime? returnDate, string? changedBy, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@RentalId",     rentalId,     DbType.Int64);
        parameters.Add("@ReturnDate", returnDate, DbType.DateTime2);
        parameters.Add("@ChangedBy",  changedBy,  DbType.String, size: 100);
        parameters.Add("@LateFee", dbType: DbType.Decimal, direction: ParameterDirection.Output,
                       precision: 18, scale: 2);

        await EnsureOpenAsync(ct);
        await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteAsync(Command(
                "dbo.usp_ReturnRental", parameters, ct, CommandType.StoredProcedure)),
            "Rentals.Return");

        return parameters.Get<decimal>("@LateFee");
    }

    public Task CancelAsync(long rentalId, string? reason, string? changedBy, CancellationToken ct = default) =>
        ExecuteProcedureAsync("dbo.usp_CancelRental",
            new { RentalId = rentalId, Reason = reason, ChangedBy = changedBy }, "Rentals.Cancel", ct);

    public async Task<IReadOnlyList<RentalSummary>> GetCustomerSummaryAsync(
        long customerId, string? status, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        var rows = await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.QueryAsync<RentalSummary>(Command(
                "dbo.usp_GetCustomerRentalSummary",
                new { CustomerId = customerId, Status = status },
                ct, CommandType.StoredProcedure)),
            "Rentals.GetCustomerSummary");
        return rows.ToList();
    }

    public async Task<IReadOnlyList<RentalStatusHistoryEntry>> GetStatusHistoryAsync(
        long rentalId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT  Id, RentalId, FromStatus, ToStatus, ChangedAt, ChangedBy, Note
            FROM    dbo.RentalStatusHistory
            WHERE   RentalId = @RentalId
            ORDER BY ChangedAt, Id
            """;

        await EnsureOpenAsync(ct);
        var rows = await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.QueryAsync<RentalStatusHistoryEntry>(Command(sql, new { RentalId = rentalId }, ct)),
            "Rentals.GetStatusHistory");
        return rows.ToList();
    }

    /// <summary>Пряма вставка (без процедури) — реалізація контракту Generic Repository.</summary>
    public override async Task<long> AddAsync(Rental entity, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dbo.Rentals (CustomerId, Status, RentalDate, DueDate, CreatedBy)
            VALUES (@CustomerId, @Status, @RentalDate, @DueDate, @CreatedBy);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """;

        await EnsureOpenAsync(ct);
        return await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteScalarAsync<long>(Command(sql, new
            {
                entity.CustomerId,
                entity.Status,
                RentalDate = entity.RentalDate == default ? DateTime.UtcNow : entity.RentalDate,
                DueDate  = entity.DueDate.ToDateTime(TimeOnly.MinValue),
                entity.CreatedBy
            }, ct)),
            "Rentals.Add");
    }

    public override async Task<bool> UpdateAsync(Rental entity, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.Rentals
            SET    DueDate   = @DueDate,
                   UpdatedAt = SYSUTCDATETIME(),
                   UpdatedBy = @UpdatedBy
            WHERE  Id = @Id AND IsDeleted = 0
            """;

        await EnsureOpenAsync(ct);
        var affected = await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteAsync(Command(sql, new
            {
                entity.Id,
                DueDate   = entity.DueDate.ToDateTime(TimeOnly.MinValue),
                UpdatedBy = entity.UpdatedBy
            }, ct)),
            "Rentals.Update");
        return affected > 0;
    }

    private async Task ExecuteProcedureAsync(
        string procedure, object parameters, string context, CancellationToken ct)
    {
        await EnsureOpenAsync(ct);
        await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteAsync(Command(procedure, parameters, ct, CommandType.StoredProcedure)),
            context);
    }
}

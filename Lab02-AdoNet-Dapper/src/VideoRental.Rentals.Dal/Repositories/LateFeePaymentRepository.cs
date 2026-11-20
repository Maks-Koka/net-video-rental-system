using Dapper;
using VideoRental.Rentals.Dal.Abstractions;
using VideoRental.Rentals.Dal.Infrastructure;
using VideoRental.Rentals.Domain.Entities;
using Microsoft.Data.SqlClient;

namespace VideoRental.Rentals.Dal.Repositories;

/// <summary>
/// Репозиторій оплат пені — <b>ADO.NET + Dapper</b> (третя Dapper-реалізація).
/// Демонструє агрегатний запит і роботу з токеном ідемпотентності.
/// </summary>
public sealed class LateFeePaymentRepository : BaseDapperRepository<LateFeePayment>, ILateFeePaymentRepository
{
    public LateFeePaymentRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction, "dbo.LateFeePayments") { }

    protected override string SelectColumns =>
        "Id, RentalId, Amount, Method, PaidAt, IdempotencyKey";

    public async Task<IReadOnlyList<LateFeePayment>> GetByRentalAsync(long rentalId, CancellationToken ct = default)
    {
        // індекс IX_LateFeePayments_RentalId обслуговує саме цей запит
        const string sql = """
            SELECT  Id, RentalId, Amount, Method, PaidAt, IdempotencyKey
            FROM    dbo.LateFeePayments
            WHERE   RentalId = @RentalId
            ORDER BY PaidAt DESC, Id DESC
            """;

        await EnsureOpenAsync(ct);
        var rows = await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.QueryAsync<LateFeePayment>(Command(sql, new { RentalId = rentalId }, ct)),
            "LateFeePayments.GetByRental");
        return rows.ToList();
    }

    /// <summary>Агрегатний запит: суму рахує БД, а не застосунок.</summary>
    public async Task<decimal> GetTotalPaidAsync(long rentalId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT ISNULL(SUM(Amount), 0)
            FROM   dbo.LateFeePayments
            WHERE  RentalId = @RentalId
            """;

        await EnsureOpenAsync(ct);
        return await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteScalarAsync<decimal>(Command(sql, new { RentalId = rentalId }, ct)),
            "LateFeePayments.GetTotalPaid");
    }

    /// <summary>
    /// Пошук за токеном ідемпотентності: якщо платіж уже існує, повторна
    /// відправка запиту НЕ створює другий платіж, а повертає наявний.
    /// </summary>
    public async Task<LateFeePayment?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default)
    {
        const string sql = """
            SELECT  Id, RentalId, Amount, Method, PaidAt, IdempotencyKey
            FROM    dbo.LateFeePayments
            WHERE   IdempotencyKey = @Key
            """;

        await EnsureOpenAsync(ct);
        return await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.QuerySingleOrDefaultAsync<LateFeePayment>(Command(sql, new { Key = key }, ct)),
            "LateFeePayments.FindByIdempotencyKey");
    }

    public override async Task<long> AddAsync(LateFeePayment entity, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dbo.LateFeePayments (RentalId, Amount, Method, IdempotencyKey)
            VALUES (@RentalId, @Amount, @Method, @IdempotencyKey);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """;

        await EnsureOpenAsync(ct);
        return await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteScalarAsync<long>(Command(sql, new
            {
                entity.RentalId, entity.Amount, entity.Method, entity.IdempotencyKey
            }, ct)),
            "LateFeePayments.Add");
    }

    public override async Task<bool> UpdateAsync(LateFeePayment entity, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.LateFeePayments
            SET    Amount = @Amount, Method = @Method
            WHERE  Id = @Id
            """;

        await EnsureOpenAsync(ct);
        var affected = await SqlExceptionTranslator.ExecuteAsync(
            () => Connection.ExecuteAsync(Command(sql, new { entity.Id, entity.Amount, entity.Method }, ct)),
            "LateFeePayments.Update");
        return affected > 0;
    }
}

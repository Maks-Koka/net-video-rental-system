using System.Data;
using VideoRental.Rentals.Dal.Abstractions;
using VideoRental.Rentals.Dal.Infrastructure;
using VideoRental.Rentals.Domain.Entities;
using Microsoft.Data.SqlClient;

namespace VideoRental.Rentals.Dal.Repositories;

/// <summary>
/// Репозиторій клієнтів на <b>ЧИСТОМУ ADO.NET</b> — без Dapper і будь-яких
/// інших сторонніх відеопрокатів.
/// </summary>
/// <remarks>
/// <para>
/// Демонструє ручну роботу з <see cref="SqlConnection"/>,
/// <see cref="SqlCommand"/>, <see cref="SqlParameter"/> та
/// <see cref="SqlDataReader"/>: явна параметризація з указанням
/// <see cref="SqlDbType"/>, ручний мапінг рядків на об'єкти, звільнення
/// ресурсів через <c>await using</c>.
/// </para>
/// <para>
/// <b>Свідомо НЕ успадковує</b> <see cref="BaseDapperRepository{T}"/> та
/// <see cref="IGenericRepository{T}"/>: сенс цього класу — показати, як те саме
/// робиться «руками», щоб було з чим порівняти Dapper-репозиторії.
/// </para>
/// <para>
/// Усі значення передаються ЛИШЕ як параметри. Конкатенації рядків із вхідними
/// даними немає: саме тому ім'я <c>Liam O'Brien</c> зі seed-даних не ламає
/// синтаксис запиту.
/// </para>
/// </remarks>
public sealed class CustomerRepository : RepositoryBase, ICustomerRepository
{
    private const string SelectColumns = """
        Id, MembershipNumber, Email, FullName, Phone, IsBlocked,
        CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted
        """;

    public CustomerRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
        : base(connection, currentTransaction) { }

    public async Task<Customer?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM   dbo.Customers
            WHERE  Id = @Id AND IsDeleted = 0
            """;

        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(sql, Connection, Transaction);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;   // ЛИШЕ параметр

        try
        {
            await using var customer = await command.ExecuteReaderAsync(ct);
            if (!await customer.ReadAsync(ct)) return null;
            return Map(customer);
        }
        catch (SqlException ex)
        {
            throw SqlExceptionTranslator.TryTranslate(ex, "Customers.GetById") ?? (Exception)ex;
        }
    }

    public async Task<Customer?> GetByMembershipNumberAsync(string membershipNumber, CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM   dbo.Customers
            WHERE  MembershipNumber = @MembershipNumber AND IsDeleted = 0
            """;

        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(sql, Connection, Transaction);
        command.Parameters.Add("@MembershipNumber", SqlDbType.NVarChar, 20).Value = membershipNumber;

        try
        {
            await using var customer = await command.ExecuteReaderAsync(ct);
            if (!await customer.ReadAsync(ct)) return null;
            return Map(customer);
        }
        catch (SqlException ex)
        {
            throw SqlExceptionTranslator.TryTranslate(ex, "Customers.GetByMembershipNumber") ?? (Exception)ex;
        }
    }

    public async Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM   dbo.Customers
            WHERE  IsDeleted = 0
            ORDER BY Id
            """;

        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(sql, Connection, Transaction);

        var result = new List<Customer>();
        try
        {
            await using var customer = await command.ExecuteReaderAsync(ct);
            while (await customer.ReadAsync(ct))
                result.Add(Map(customer));
        }
        catch (SqlException ex)
        {
            throw SqlExceptionTranslator.TryTranslate(ex, "Customers.GetAll") ?? (Exception)ex;
        }
        return result;
    }

    public async Task<long> AddAsync(Customer customer, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dbo.Customers (MembershipNumber, Email, FullName, Phone, IsBlocked, CreatedBy)
            VALUES (@MembershipNumber, @Email, @FullName, @Phone, @IsBlocked, @CreatedBy);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """;

        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(sql, Connection, Transaction);
        command.Parameters.Add("@MembershipNumber", SqlDbType.NVarChar, 20).Value  = customer.MembershipNumber;
        command.Parameters.Add("@Email",      SqlDbType.NVarChar, 256).Value = customer.Email;
        command.Parameters.Add("@FullName",   SqlDbType.NVarChar, 200).Value = customer.FullName;
        command.Parameters.Add("@Phone",      SqlDbType.NVarChar, 30).Value  = (object?)customer.Phone ?? DBNull.Value;
        command.Parameters.Add("@IsBlocked",  SqlDbType.Bit).Value           = customer.IsBlocked;
        command.Parameters.Add("@CreatedBy",  SqlDbType.NVarChar, 100).Value = (object?)customer.CreatedBy ?? DBNull.Value;

        try
        {
            var scalar = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt64(scalar);
        }
        catch (SqlException ex)
        {
            throw SqlExceptionTranslator.TryTranslate(ex, "Customers.Add") ?? (Exception)ex;
        }
    }

    public async Task<bool> SetBlockedAsync(long id, bool isBlocked, string? changedBy, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.Customers
            SET    IsBlocked = @IsBlocked,
                   UpdatedAt = SYSUTCDATETIME(),
                   UpdatedBy = @UpdatedBy
            WHERE  Id = @Id AND IsDeleted = 0
            """;

        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(sql, Connection, Transaction);
        command.Parameters.Add("@Id",        SqlDbType.BigInt).Value          = id;
        command.Parameters.Add("@IsBlocked", SqlDbType.Bit).Value             = isBlocked;
        command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 100).Value   = (object?)changedBy ?? DBNull.Value;

        try
        {
            return await command.ExecuteNonQueryAsync(ct) > 0;
        }
        catch (SqlException ex)
        {
            throw SqlExceptionTranslator.TryTranslate(ex, "Customers.SetBlocked") ?? (Exception)ex;
        }
    }

    public async Task<bool> SoftDeleteAsync(long id, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.Customers
            SET    IsDeleted = 1, UpdatedAt = SYSUTCDATETIME()
            WHERE  Id = @Id AND IsDeleted = 0
            """;

        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(sql, Connection, Transaction);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;

        try
        {
            return await command.ExecuteNonQueryAsync(ct) > 0;
        }
        catch (SqlException ex)
        {
            throw SqlExceptionTranslator.TryTranslate(ex, "Customers.SoftDelete") ?? (Exception)ex;
        }
    }

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default)
    {
        const string sql = "SELECT 1 FROM dbo.Customers WHERE Email = @Email AND IsDeleted = 0";

        await EnsureOpenAsync(ct);
        await using var command = new SqlCommand(sql, Connection, Transaction);
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = email;

        try
        {
            return await command.ExecuteScalarAsync(ct) is not null;
        }
        catch (SqlException ex)
        {
            throw SqlExceptionTranslator.TryTranslate(ex, "Customers.ExistsByEmail") ?? (Exception)ex;
        }
    }

    /// <summary>
    /// Ручний мапінг рядка на модель домену — те, що Dapper робить автоматично.
    /// Індекси стовпців відповідають порядку в <see cref="SelectColumns"/>.
    /// </summary>
    private static Customer Map(SqlDataReader r) => new()
    {
        Id         = r.GetInt64(0),
        MembershipNumber = r.GetString(1),
        Email      = r.GetString(2),
        FullName   = r.GetString(3),
        Phone      = r.IsDBNull(4) ? null : r.GetString(4),
        IsBlocked  = r.GetBoolean(5),
        CreatedAt  = r.GetDateTime(6),
        CreatedBy  = r.IsDBNull(7) ? null : r.GetString(7),
        UpdatedAt  = r.IsDBNull(8) ? null : r.GetDateTime(8),
        UpdatedBy  = r.IsDBNull(9) ? null : r.GetString(9),
        IsDeleted  = r.GetBoolean(10)
    };
}

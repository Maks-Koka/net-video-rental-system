using System.Data;
using AutoMapper;
using VideoRental.Rentals.Bll.Abstractions;
using VideoRental.Rentals.Bll.Dtos;
using VideoRental.Rentals.Dal.Abstractions;
using VideoRental.Rentals.Domain.Entities;
using VideoRental.Rentals.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace VideoRental.Rentals.Bll.Services;

/// <summary>Сервіс клієнтів. Працює через Unit of Work і репозиторій на чистому ADO.NET.</summary>
public sealed class CustomerService : ICustomerService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(IUnitOfWork uow, IMapper mapper, ILogger<CustomerService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<CustomerDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var customer = await _uow.Customers.GetByIdAsync(id, ct)
                     ?? throw NotFoundException.For("Customer", id);
        return _mapper.Map<CustomerDto>(customer);
    }

    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct = default)
    {
        var customers = await _uow.Customers.GetAllAsync(ct);
        return customers.Select(_mapper.Map<CustomerDto>).ToList();
    }

    /// <summary>
    /// Створення клієнта. Бізнес-правило, для якого потрібен стан із БД:
    /// унікальність email. Перевіряємо заздалегідь, щоб повернути зрозумілий
    /// 409, а не чекати на порушення UNIQUE-індексу.
    /// </summary>
    public async Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken ct = default)
    {
        if (await _uow.Customers.ExistsByEmailAsync(input.Email, ct))
            throw new BusinessConflictException($"Customer with email '{input.Email}' already exists");

        if (await _uow.Customers.GetByMembershipNumberAsync(input.MembershipNumber, ct) is not null)
            throw new BusinessConflictException($"Customer with card number '{input.MembershipNumber}' already exists");

        var entity = _mapper.Map<Customer>(input);
        entity.CreatedBy = "rentals-api";

        await _uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        long id;
        try
        {
            id = await _uow.Customers.AddAsync(entity, ct);
            await _uow.CommitAsync(ct);
        }
        catch
        {
            await _uow.RollbackAsync(ct);
            throw;
        }

        _logger.LogInformation("Customer {CustomerId} created with card {MembershipNumber}", id, input.MembershipNumber);

        var saved = await _uow.Customers.GetByIdAsync(id, ct)
                    ?? throw NotFoundException.For("Customer", id);
        return _mapper.Map<CustomerDto>(saved);
    }

    public async Task SetBlockedAsync(long id, bool isBlocked, CancellationToken ct = default)
    {
        if (!await _uow.Customers.SetBlockedAsync(id, isBlocked, "rentals-api", ct))
            throw NotFoundException.For("Customer", id);

        _logger.LogInformation("Customer {CustomerId} blocked flag set to {IsBlocked}", id, isBlocked);
    }

    /// <summary>
    /// М'яке видалення: клієнта з історією оренд фізично не видаляємо
    /// (у БД немає каскаду <c>Customers -> Rentals</c>).
    /// </summary>
    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        if (!await _uow.Customers.SoftDeleteAsync(id, ct))
            throw NotFoundException.For("Customer", id);

        _logger.LogInformation("Customer {CustomerId} soft-deleted", id);
    }
}

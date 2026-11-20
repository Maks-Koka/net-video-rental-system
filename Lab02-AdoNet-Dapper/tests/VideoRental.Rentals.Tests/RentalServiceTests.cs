using System.Data;
using AutoMapper;
using VideoRental.Rentals.Bll.Dtos;
using VideoRental.Rentals.Bll.Mapping;
using VideoRental.Rentals.Bll.Services;
using VideoRental.Rentals.Dal.Abstractions;
using VideoRental.Rentals.Domain.Constants;
using VideoRental.Rentals.Domain.Entities;
using VideoRental.Rentals.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace VideoRental.Rentals.Tests;

/// <summary>
/// Unit-тести бізнес-логіки: DAL і Unit of Work замокані (Moq), тому
/// тести не торкаються бази даних і перевіряють саме РІШЕННЯ сервісу.
/// Покрито «happy path» і негативні сценарії (валідація, бізнес-помилки).
/// </summary>
public class RentalServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new(MockBehavior.Strict);
    private readonly Mock<IRentalRepository> _rentals = new(MockBehavior.Strict);
    private readonly Mock<ICustomerRepository> _customers = new(MockBehavior.Strict);
    private readonly Mock<IMovieRepository> _movies = new(MockBehavior.Strict);
    private readonly Mock<ILateFeePaymentRepository> _fines = new(MockBehavior.Strict);
    private readonly IMapper _mapper;

    public RentalServiceTests()
    {
        _mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<RentalsProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

        _uow.SetupGet(u => u.Rentals).Returns(_rentals.Object);
        _uow.SetupGet(u => u.Customers).Returns(_customers.Object);
        _uow.SetupGet(u => u.Movies).Returns(_movies.Object);
        _uow.SetupGet(u => u.LateFeePayments).Returns(_fines.Object);
    }

    private RentalService CreateSut() =>
        new(_uow.Object, _mapper, NullLogger<RentalService>.Instance);

    private void ExpectTransaction()
    {
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static Customer ActiveCustomer(long id = 1) => new()
    {
        Id = id, MembershipNumber = $"VR-{id:D6}", Email = $"r{id}@example.com",
        FullName = "Тестовий Клієнт", IsBlocked = false
    };

    private static Movie AvailableMovie(long id = 1, int discs = 5) => new()
    {
        Id = id, Title = "Тіні забутих предків", Director = "Сергій Параджанов",
        DailyRate = 3.5m, DiscsAvailable = discs
    };

    private static Rental RentalWith(string status, long id = 10, int items = 1)
    {
        var rental = new Rental
        {
            Id = id, CustomerId = 1, Status = status,
            RentalDate = DateTime.UtcNow.AddDays(-1),
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14))
        };
        for (var i = 0; i < items; i++)
            rental.Items.Add(new RentalItem
            {
                RentalId = id, MovieId = i + 1, MovieTitle = "Тіні забутих предків",
                MovieDirector = "Сергій Параджанов", DailyRate = 3.5m, Units = 1
            });
        return rental;
    }

    // ------------------------------------------------------------ GetById ---

    [Fact]
    public async Task GetById_ReturnsDto_WhenRentalExists()
    {
        var rental = RentalWith(RentalStatus.PickedUp, 10, items: 2);
        _rentals.Setup(r => r.GetWithItemsAsync(10, It.IsAny<CancellationToken>()))
              .ReturnsAsync(rental);

        var dto = await CreateSut().GetByIdAsync(10);

        Assert.Equal(10, dto.Id);
        Assert.Equal(RentalStatus.PickedUp, dto.Status);
        Assert.Equal(2, dto.Items.Count);
    }

    /// <summary>
    /// Контрольна точка Кроку C Лаб. 02:
    /// «підтвердження неіснуючої оренди → NotFoundException».
    /// </summary>
    [Fact]
    public async Task GetById_Throws_NotFound_WhenRentalMissing()
    {
        _rentals.Setup(r => r.GetWithItemsAsync(999, It.IsAny<CancellationToken>()))
              .ReturnsAsync((Rental?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => CreateSut().GetByIdAsync(999));

        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public async Task ConfirmAndPickUp_Throws_NotFound_WhenRentalMissing()
    {
        ExpectTransaction();
        _rentals.Setup(r => r.GetWithItemsAsync(999, It.IsAny<CancellationToken>()))
              .ReturnsAsync((Rental?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateSut().ConfirmAndPickUpAsync(999));

        // транзакцію мусить бути відкочено
        _uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------- Create ---

    [Fact]
    public async Task Create_Throws_NotFound_WhenCustomerMissing()
    {
        _customers.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Customer?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateSut().CreateAsync(new CreateRentalDto
        {
            CustomerId = 5,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            PickupStore = "Центральний магазин",
            Items = [new CreateRentalItemDto { MovieId = 1, Units = 1 }]
        }));

        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()),
                    Times.Never);
    }

    [Fact]
    public async Task Create_Throws_Conflict_WhenCustomerBlocked()
    {
        var blocked = ActiveCustomer(5);
        blocked.IsBlocked = true;
        _customers.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(blocked);

        var ex = await Assert.ThrowsAsync<BusinessConflictException>(
            () => CreateSut().CreateAsync(new CreateRentalDto
            {
                CustomerId = 5,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                PickupStore = "Центральний магазин",
                Items = [new CreateRentalItemDto { MovieId = 1, Units = 1 }]
            }));

        Assert.Contains("blocked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_Throws_Validation_WhenDueDateInPast()
    {
        _customers.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActiveCustomer());

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateSut().CreateAsync(new CreateRentalDto
            {
                CustomerId = 1,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                PickupStore = "Центральний магазин",
                Items = [new CreateRentalItemDto { MovieId = 1, Units = 1 }]
            }));

        Assert.True(ex.Errors.ContainsKey("DueDate"));
    }

    /// <summary>Брак дисків виявляється ДО початку транзакції.</summary>
    [Fact]
    public async Task Create_Throws_Conflict_WhenNotEnoughDiscs()
    {
        _customers.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActiveCustomer());
        _movies.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
              .ReturnsAsync(AvailableMovie(1, discs: 2));

        var ex = await Assert.ThrowsAsync<BusinessConflictException>(
            () => CreateSut().CreateAsync(new CreateRentalDto
            {
                CustomerId = 1,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                PickupStore = "Центральний магазин",
                Items = [new CreateRentalItemDto { MovieId = 1, Units = 10 }]
            }));

        Assert.Contains("only 2 discs", ex.Message);
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()),
                    Times.Never);
    }

    /// <summary>Дублікати тієї самого фільму у запиті склеюються в одну позицію.</summary>
    [Fact]
    public async Task Create_MergesDuplicateMovies_IntoSingleItem()
    {
        ExpectTransaction();
        _customers.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ActiveCustomer());
        _movies.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
              .ReturnsAsync(AvailableMovie(1, discs: 5));

        IEnumerable<(long MovieId, int Units)>? captured = null;
        _rentals.Setup(r => r.CreateViaProcedureAsync(
                  1, It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<string?>(),
                  It.IsAny<IEnumerable<(long, int)>>(), It.IsAny<string?>(),
                  It.IsAny<CancellationToken>()))
              .Callback((long _, DateOnly _, string _, string? _,
                         IEnumerable<(long MovieId, int Units)> items, string? _, CancellationToken _) =>
                         captured = items.ToList())
              .ReturnsAsync(77L);

        _rentals.Setup(r => r.GetWithItemsAsync(77, It.IsAny<CancellationToken>()))
              .ReturnsAsync(RentalWith(RentalStatus.Submitted, 77));

        await CreateSut().CreateAsync(new CreateRentalDto
        {
            CustomerId = 1,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            PickupStore = "Центральний магазин",
            Items =
            [
                new CreateRentalItemDto { MovieId = 1, Units = 2 },
                new CreateRentalItemDto { MovieId = 1, Units = 3 }
            ]
        });

        Assert.NotNull(captured);
        var single = Assert.Single(captured!);
        Assert.Equal(1, single.MovieId);
        Assert.Equal(5, single.Units);          // 2 + 3 склеєно
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------- ConfirmAndPickUp (happy) ---

    [Fact]
    public async Task ConfirmAndPickUp_CallsBothProcedures_AndCommits()
    {
        ExpectTransaction();
        var rental = RentalWith(RentalStatus.Submitted, 10);

        _rentals.Setup(r => r.GetWithItemsAsync(10, It.IsAny<CancellationToken>()))
              .ReturnsAsync(rental);
        _rentals.Setup(r => r.ConfirmDiscsAsync(10, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        _rentals.Setup(r => r.PickUpAsync(10, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var dto = await CreateSut().ConfirmAndPickUpAsync(10);

        _rentals.Verify(r => r.ConfirmDiscsAsync(10, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _rentals.Verify(r => r.PickUpAsync(10, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(10, dto.Id);
    }

    [Fact]
    public async Task ConfirmAndPickUp_Throws_Conflict_WhenRentalHasNoItems()
    {
        ExpectTransaction();
        _rentals.Setup(r => r.GetWithItemsAsync(10, It.IsAny<CancellationToken>()))
              .ReturnsAsync(RentalWith(RentalStatus.Submitted, 10, items: 0));

        await Assert.ThrowsAsync<BusinessConflictException>(
            () => CreateSut().ConfirmAndPickUpAsync(10));

        _uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// КЛЮЧОВИЙ тест транзакційності: помилка в ДРУГОМУ кроці операції
    /// (брак дисків у процедурі оренди) мусить відкотити транзакцію,
    /// тому зміни першого кроку (підтвердження статусу) не збережуться.
    /// </summary>
    [Fact]
    public async Task ConfirmAndPickUp_RollsBack_WhenSecondStepFails()
    {
        ExpectTransaction();
        _rentals.Setup(r => r.GetWithItemsAsync(10, It.IsAny<CancellationToken>()))
              .ReturnsAsync(RentalWith(RentalStatus.Submitted, 10));
        _rentals.Setup(r => r.ConfirmDiscsAsync(10, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        _rentals.Setup(r => r.PickUpAsync(10, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new BusinessConflictException("Not enough discs available to issue the rental"));

        await Assert.ThrowsAsync<BusinessConflictException>(
            () => CreateSut().ConfirmAndPickUpAsync(10));

        _rentals.Verify(r => r.ConfirmDiscsAsync(10, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------- GetByCustomer ---

    [Fact]
    public async Task GetByCustomer_Throws_Validation_OnUnknownStatus()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateSut().GetByCustomerAsync(1, "NoSuchStatus"));

        Assert.True(ex.Errors.ContainsKey("status"));
    }

    [Fact]
    public async Task GetByCustomer_Throws_NotFound_WhenCustomerMissing()
    {
        _customers.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Customer?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateSut().GetByCustomerAsync(42, null));
    }

    // ----------------------------------------------------------- PayLateFee ---

    /// <summary>Ідемпотентність: наявний платіж повертається, новий не створюється.</summary>
    [Fact]
    public async Task PayLateFee_ReturnsExistingPayment_OnIdempotentReplay()
    {
        var rental = RentalWith(RentalStatus.Returned, 10);
        rental.LateFeeAmount = 50m;

        _rentals.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(rental);
        _fines.Setup(r => r.FindByIdempotencyKeyAsync("key-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new LateFeePayment { Id = 555, RentalId = 10, Amount = 50m, Method = "Card" });

        var dto = await CreateSut().PayLateFeeAsync(10, new PayLateFeeDto
        {
            Amount = 50m, Method = "Card", IdempotencyKey = "key-1"
        });

        Assert.Equal(555, dto.Id);
        _fines.Verify(r => r.AddAsync(It.IsAny<LateFeePayment>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()),
                    Times.Never);
    }

    [Fact]
    public async Task PayLateFee_Throws_Conflict_WhenNoOutstandingFine()
    {
        var rental = RentalWith(RentalStatus.Returned, 10);
        rental.LateFeeAmount = 0m;

        _rentals.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(rental);

        var ex = await Assert.ThrowsAsync<BusinessConflictException>(
            () => CreateSut().PayLateFeeAsync(10, new PayLateFeeDto { Amount = 10m, Method = "Cash" }));

        Assert.Contains("no outstanding fine", ex.Message);
    }

    [Fact]
    public async Task PayLateFee_Throws_Conflict_WhenAmountExceedsOutstanding()
    {
        var rental = RentalWith(RentalStatus.Returned, 10);
        rental.LateFeeAmount = 50m;

        _rentals.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(rental);
        _fines.Setup(r => r.GetTotalPaidAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(40m);

        var ex = await Assert.ThrowsAsync<BusinessConflictException>(
            () => CreateSut().PayLateFeeAsync(10, new PayLateFeeDto { Amount = 30m, Method = "Cash" }));

        Assert.Contains("exceeds the outstanding fine", ex.Message);
    }

    [Fact]
    public async Task PayLateFee_Throws_Conflict_WhenAlreadyFullyPaid()
    {
        var rental = RentalWith(RentalStatus.Returned, 10);
        rental.LateFeeAmount = 50m;

        _rentals.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(rental);
        _fines.Setup(r => r.GetTotalPaidAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(50m);

        var ex = await Assert.ThrowsAsync<BusinessConflictException>(
            () => CreateSut().PayLateFeeAsync(10, new PayLateFeeDto { Amount = 5m, Method = "Cash" }));

        Assert.Contains("already fully paid", ex.Message);
    }

    // ------------------------------------------------------------- Return ---

    [Fact]
    public async Task Return_Throws_Validation_WhenReturnDateInFuture()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateSut().ReturnAsync(10, new ReturnRentalDto
            {
                ReturnDate = DateTime.UtcNow.AddDays(30)
            }));

        Assert.True(ex.Errors.ContainsKey("ReturnDate"));
    }

    [Fact]
    public async Task Return_Commits_AndReturnsUpdatedRental()
    {
        ExpectTransaction();
        _rentals.Setup(r => r.ReturnAsync(10, It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(12.5m);

        var returned = RentalWith(RentalStatus.Returned, 10);
        returned.LateFeeAmount = 12.5m;
        _rentals.Setup(r => r.GetWithItemsAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(returned);

        var dto = await CreateSut().ReturnAsync(10, new ReturnRentalDto());

        Assert.Equal(RentalStatus.Returned, dto.Status);
        Assert.Equal(12.5m, dto.LateFeeAmount);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

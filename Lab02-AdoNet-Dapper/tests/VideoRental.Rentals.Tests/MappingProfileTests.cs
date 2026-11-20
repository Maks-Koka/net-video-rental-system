using AutoMapper;
using VideoRental.Rentals.Bll.Dtos;
using VideoRental.Rentals.Bll.Mapping;
using VideoRental.Rentals.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace VideoRental.Rentals.Tests;

/// <summary>
/// Перевірка конфігурації AutoMapper. Крок C.1 Лаб. 02 вимагає валідувати
/// мапінг тестом <c>AssertConfigurationIsValid()</c>: він ловить забуті
/// властивості ще на етапі збірки тестів, а не в рантаймі.
/// </summary>
public class MappingProfileTests
{
    private static IMapper CreateMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<RentalsProfile>(),
                                NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public void Configuration_IsValid()
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<RentalsProfile>(),
            NullLoggerFactory.Instance);

        // кидає AutoMapperConfigurationException, якщо якась властивість DTO
        // не має джерела в сутності
        config.AssertConfigurationIsValid();
    }

    [Fact]
    public void Maps_Rental_WithItemsAndDetail()
    {
        var mapper = CreateMapper();

        var rental = new Rental
        {
            Id = 42,
            CustomerId = 7,
            Status = "PickedUp",
            RentalDate = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
            DueDate = new DateOnly(2026, 11, 15),
            LateFeeAmount = 12.50m,
            Detail = new RentalDetail { RentalId = 42, PickupStore = "Центральний магазин", Comment = "коментар" }
        };
        rental.Items.Add(new RentalItem
        {
            RentalId = 42, MovieId = 1, MovieTitle = "Тіні забутих предків", MovieDirector = "Сергій Параджанов",
            DailyRate = 3.5m, Units = 2, Discount = 1m
        });

        var dto = mapper.Map<RentalDto>(rental);

        Assert.Equal(42, dto.Id);
        Assert.Equal("PickedUp", dto.Status);
        Assert.Equal(new DateOnly(2026, 11, 15), dto.DueDate);
        Assert.Equal(12.50m, dto.LateFeeAmount);
        Assert.NotNull(dto.Detail);
        Assert.Equal("Центральний магазин", dto.Detail!.PickupStore);

        var item = Assert.Single(dto.Items);
        Assert.Equal("Тіні забутих предків", item.MovieTitle);
        // DailyCharge = DailyRate * Units - Discount = 3.5 * 2 - 1 = 6.0
        Assert.Equal(6.0m, item.DailyCharge);
    }

    [Fact]
    public void Maps_CreateCustomerDto_ToEntity()
    {
        var mapper = CreateMapper();

        var dto = new CreateCustomerDto
        {
            MembershipNumber = "VR-000099",
            Email = "test@example.com",
            FullName = "Liam O'Brien",
            Phone = "+380501234567"
        };

        var entity = mapper.Map<Customer>(dto);

        Assert.Equal("VR-000099", entity.MembershipNumber);
        Assert.Equal("Liam O'Brien", entity.FullName);   // апостроф не губиться
        Assert.False(entity.IsBlocked);
        Assert.Equal(0, entity.Id);                      // Id призначає база
    }

    [Fact]
    public void Maps_Customer_ToDto()
    {
        var mapper = CreateMapper();

        var customer = new Customer
        {
            Id = 5, MembershipNumber = "VR-000005", Email = "a@b.com",
            FullName = "Олена Коваленко", IsBlocked = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var dto = mapper.Map<CustomerDto>(customer);

        Assert.Equal(5, dto.Id);
        Assert.Equal("Олена Коваленко", dto.FullName);
        Assert.True(dto.IsBlocked);
    }
}

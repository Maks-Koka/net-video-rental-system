using AutoMapper;
using VideoRental.Rentals.Bll.Dtos;
using VideoRental.Rentals.Domain.Entities;

namespace VideoRental.Rentals.Bll.Mapping;

/// <summary>
/// Профілі мапінгу AutoMapper. Зберігаються в BLL централізовано; коректність
/// конфігурації перевіряється тестом <c>AssertConfigurationIsValid()</c>
/// (див. <c>VideoRental.Rentals.Tests/MappingProfileTests.cs</c>).
/// </summary>
public sealed class RentalsProfile : Profile
{
    public RentalsProfile()
    {
        CreateMap<Customer, CustomerDto>();

        CreateMap<Movie, MovieDto>();

        CreateMap<RentalItem, RentalItemDto>();          // DailyCharge — обчислювана властивість сутності

        CreateMap<RentalDetail, RentalDetailDto>();

        CreateMap<Rental, RentalDto>();

        CreateMap<RentalStatusHistoryEntry, RentalStatusHistoryDto>();

        CreateMap<RentalSummary, RentalSummaryDto>();

        CreateMap<LateFeePayment, LateFeePaymentDto>();

        /* вхідні DTO -> сутності.
           Поля, які задає БАЗА (ідентифікатор, аудитні колонки, soft delete)
           або СЕРВІС, явно позначаємо Ignore: інакше AssertConfigurationIsValid()
           справедливо скаржиться на незамаплені властивості призначення.
           Явний Ignore — це задокументоване рішення, а не приховування помилки. */
        CreateMap<CreateCustomerDto, Customer>()
            .ForMember(d => d.Id,        o => o.Ignore())   // IDENTITY у dbo.Customers
            .ForMember(d => d.IsBlocked, o => o.Ignore())   // DEFAULT 0
            .ForMember(d => d.CreatedAt, o => o.Ignore())   // DEFAULT SYSUTCDATETIME()
            .ForMember(d => d.CreatedBy, o => o.Ignore())   // задає сервіс
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedBy, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore());  // DEFAULT 0

        CreateMap<SyncMovieDto, Movie>()
            .ForMember(d => d.SyncedAt, o => o.Ignore());   // SYSUTCDATETIME() у MERGE
    }
}

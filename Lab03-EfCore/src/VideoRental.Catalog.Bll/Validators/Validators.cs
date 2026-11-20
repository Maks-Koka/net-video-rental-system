using FluentValidation;
using VideoRental.Catalog.Bll.Dtos;
using VideoRental.Catalog.Dal.Specifications;

namespace VideoRental.Catalog.Bll.Validators;

/* =============================================================================
   FluentValidation — ДРУГИЙ рівень валідації (Частина 4.3 Лаб. 03).

   Розподіл обов'язків, щоб не дублювати правила:
     DataAnnotations (на DTO) — ФОРМАТ: обов'язковість, довжина, діапазон.
     FluentValidation (тут)   — СКЛАДНІ правила: регулярні вирази, умовні
                                перевірки, взаємозалежні поля, контрольна сума.

   ВАЖЛИВО: автоматична валідація через пакет FluentValidation.AspNetCore
   (AddFluentValidation) ЗАСТАРІЛА і більше не підтримується. Валідатори
   реєструються через FluentValidation.DependencyInjectionExtensions
   (AddValidatorsFromAssemblyContaining) і викликаються ЯВНО в контролері.
   ============================================================================= */

public sealed class CreateMovieDtoValidator : AbstractValidator<CreateMovieDto>
{
    public CreateMovieDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Назва фільму обовʼязкова")
            .MaximumLength(300);

        RuleFor(x => x.Barcode)
            .NotEmpty().WithMessage("Barcode обовʼязковий")
            .Matches(@"^[0-9]{3}-[0-9]{3}-[0-9]{2}-[0-9]{4}-[0-9]$")
                .WithMessage("Barcode має бути у форматі 482-001-00-0001-5");

        RuleFor(x => x.DailyRate)
            .GreaterThan(0).WithMessage("Денний тариф має бути більшим за нуль")
            .LessThanOrEqualTo(100_000);

        RuleFor(x => x.DirectorId)
            .GreaterThan(0).WithMessage("Потрібно вказати режисера");

        RuleFor(x => x.ReleaseYear)
            .InclusiveBetween(1400, 2100)
            .When(x => x.ReleaseYear.HasValue)
            .WithMessage("Рік видання має бути в межах 1400–2100");

        // взаємозалежні поля: якщо вказано видавця або кількість хвилин,
        // опис стає обовʼязковим — інакше вийде «напівпорожній» MovieDetail
        RuleFor(x => x.Description)
            .NotEmpty()
            .When(x => !string.IsNullOrWhiteSpace(x.Studio) || x.DurationMinutes.HasValue)
            .WithMessage("Якщо вказано видавця або кількість хвилин, опис обовʼязковий");

        RuleFor(x => x.GenreIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Жанри не повинні повторюватися");

        RuleForEach(x => x.GenreIds)
            .GreaterThan(0).WithMessage("Ідентифікатор жанру має бути додатним");
    }
}

public sealed class UpdateMovieDtoValidator : AbstractValidator<UpdateMovieDto>
{
    public UpdateMovieDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.DailyRate).GreaterThan(0).LessThanOrEqualTo(100_000);
    }
}

public sealed class CreateDirectorDtoValidator : AbstractValidator<CreateDirectorDto>
{
    public CreateDirectorDtoValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Імʼя режисера обовʼязкове")
            .MinimumLength(2)
            .MaximumLength(200);

        RuleFor(x => x.BirthYear)
            .InclusiveBetween(1000, 2100)
            .When(x => x.BirthYear.HasValue);
    }
}

public sealed class AddDiscDtoValidator : AbstractValidator<AddDiscDto>
{
    public AddDiscDtoValidator()
    {
        RuleFor(x => x.DiscNumber)
            .NotEmpty()
            .Matches("^[A-Z0-9-]+$")
                .WithMessage("Інвентарний номер може містити лише великі латинські літери, цифри й дефіс");

        RuleFor(x => x.Condition)
            .Must(Domain.Entities.DiscCondition.IsValid)
            .WithMessage($"Стан має бути одним із: {string.Join(", ", Domain.Entities.DiscCondition.All)}");

        RuleFor(x => x.AcquiredAt)
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .When(x => x.AcquiredAt.HasValue)
            .WithMessage("Дата надходження не може бути в майбутньому");
    }
}

/// <summary>
/// Валідація параметрів списку: захищає від довільних полів сортування
/// та завеликих хвилин.
/// </summary>
public sealed class MovieQueryValidator : AbstractValidator<MovieQuery>
{
    public MovieQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThan(0).WithMessage("Номер хвилини починається з 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, MovieQuery.MaxPageSize)
            .WithMessage($"Розмір хвилини має бути в межах 1–{MovieQuery.MaxPageSize}");

        RuleFor(x => x.SortBy)
            .Must(f => string.IsNullOrWhiteSpace(f) ||
                       MovieQuery.AllowedSortFields.Contains(f.ToLowerInvariant()))
            .WithMessage($"Сортувати можна лише за: {string.Join(", ", MovieQuery.AllowedSortFields)}");

        RuleFor(x => x.SortDir)
            .Must(d => string.IsNullOrWhiteSpace(d) ||
                       d.Equals("asc", StringComparison.OrdinalIgnoreCase) ||
                       d.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Напрямок сортування: asc або desc");

        RuleFor(x => x.MaxRate)
            .GreaterThanOrEqualTo(x => x.MinRate!.Value)
            .When(x => x.MinRate.HasValue && x.MaxRate.HasValue)
            .WithMessage("maxRate не може бути меншим за minRate");
    }
}

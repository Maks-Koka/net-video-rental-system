using VideoRental.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace VideoRental.Catalog.Dal.Configurations;

/// <summary>
/// Фіксована дата для <c>HasData</c>. Значення в модельному сідінгу мають бути
/// СТАТИЧНИМИ: EF Core порівнює їх між міграціями, і <c>DateTime.UtcNow</c>
/// давав би нову міграцію при кожній генерації.
/// </summary>
internal static class SeedConstants
{
    public static readonly DateTime SeedDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

/// <summary>Конфігурація <see cref="Director"/> + СТАТИЧНИЙ сідінг довідника.</summary>
public sealed class DirectorConfiguration : IEntityTypeConfiguration<Director>
{
    public void Configure(EntityTypeBuilder<Director> builder)
    {
        builder.ToTable("Directors", t =>
            t.HasCheckConstraint("CK_Directors_BirthYear",
                "[BirthYear] IS NULL OR ([BirthYear] >= 1000 AND [BirthYear] <= 2100)"));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.FullName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Country).HasMaxLength(100);
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(a => a.IsDeleted).HasDefaultValue(false);
        builder.Property(a => a.RowVersion).IsRowVersion();

        // індекс під запит: «пошук режисера за прізвищем»
        builder.HasIndex(a => a.FullName).HasDatabaseName("IX_Directors_FullName");

        /* МОДЕЛЬНИЙ (статичний) СІДІНГ — HasData.
           Довідник із ФІКСОВАНИМИ Id: міграція сама стежить, щоб рядки
           з'явилися рівно один раз, тому повторне застосування дублікатів
           не створює. Другий, програмний спосіб сідінгу — у DbInitializer. */
        builder.HasData(
            new { Id = 1, FullName = "Сергій Параджанов",   Country = "Вірменія", BirthYear = (int?)1924, CreatedAt = SeedConstants.SeedDate, IsDeleted = false },
            new { Id = 2, FullName = "Олександр Довженко",  Country = "Україна",  BirthYear = (int?)1894, CreatedAt = SeedConstants.SeedDate, IsDeleted = false },
            new { Id = 3, FullName = "Борис Івченко",       Country = "Україна",  BirthYear = (int?)1941, CreatedAt = SeedConstants.SeedDate, IsDeleted = false },
            new { Id = 4, FullName = "Олесь Санін",         Country = "Україна",  BirthYear = (int?)1972, CreatedAt = SeedConstants.SeedDate, IsDeleted = false },
            new { Id = 5, FullName = "Ахтем Сеітаблаєв",    Country = "Україна",  BirthYear = (int?)1972, CreatedAt = SeedConstants.SeedDate, IsDeleted = false },
            new { Id = 6, FullName = "Антоніо Лукіч",       Country = "Україна",  BirthYear = (int?)1993, CreatedAt = SeedConstants.SeedDate, IsDeleted = false });
    }
}

/// <summary>Конфігурація <see cref="Genre"/> + СТАТИЧНИЙ сідінг довідника.</summary>
public sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("Genres");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name).IsRequired().HasMaxLength(100);
        builder.Property(g => g.Slug).IsRequired().HasMaxLength(100);
        builder.Property(g => g.Description).HasMaxLength(500);
        builder.Property(g => g.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(g => g.IsDeleted).HasDefaultValue(false);

        // унікальні бізнес-ключі: назва та slug з URL
        builder.HasIndex(g => g.Name).IsUnique().HasDatabaseName("UQ_Genres_Name");
        builder.HasIndex(g => g.Slug).IsUnique().HasDatabaseName("UQ_Genres_Slug");

        builder.HasData(
            new { Id = 1, Name = "Комедія",           Slug = "comedy",     Description = (string?)"Комедійні фільми",                   CreatedAt = SeedConstants.SeedDate, IsDeleted = false },
            new { Id = 2, Name = "Історичний", Slug = "historical", Description = (string?)"Фільми на історичному матеріалі",    CreatedAt = SeedConstants.SeedDate, IsDeleted = false },
            new { Id = 3, Name = "Драма",    Slug = "drama",     Description = (string?)"Драматичні фільми",  CreatedAt = SeedConstants.SeedDate, IsDeleted = false },
            new { Id = 4, Name = "Класика",          Slug = "classic",    Description = (string?)"Класика українського кіно",                   CreatedAt = SeedConstants.SeedDate, IsDeleted = false },
            new { Id = 5, Name = "Документальний",     Slug = "documentary", Description = (string?)"Документальне кіно",              CreatedAt = SeedConstants.SeedDate, IsDeleted = false });
    }
}

/// <summary>
/// Конфігурація <see cref="MovieDetail"/> — зв'язок <b>1:1</b>:
/// первинний ключ є водночас зовнішнім.
/// </summary>
public sealed class MovieDetailConfiguration : IEntityTypeConfiguration<MovieDetail>
{
    public void Configure(EntityTypeBuilder<MovieDetail> builder)
    {
        builder.ToTable("MovieDetails", t =>
            t.HasCheckConstraint("CK_MovieDetails_DurationMinutes",
                "[DurationMinutes] IS NULL OR [DurationMinutes] > 0"));

        // PK = FK: саме це робить зв'язок 1:1, а не 1:N
        builder.HasKey(d => d.MovieId);

        builder.Property(d => d.Description).IsRequired().HasMaxLength(2000);
        builder.Property(d => d.Studio).HasMaxLength(200);
        builder.Property(d => d.Language).IsRequired().HasMaxLength(50).HasDefaultValue("uk");
        builder.Property(d => d.CoverUrl).HasMaxLength(500);
    }
}

/// <summary>Конфігурація <see cref="DiscCopy"/> — зв'язок <b>1:N</b> від фільму.</summary>
public sealed class DiscCopyConfiguration : IEntityTypeConfiguration<DiscCopy>
{
    public void Configure(EntityTypeBuilder<DiscCopy> builder)
    {
        builder.ToTable("DiscCopies", t =>
            t.HasCheckConstraint("CK_DiscCopies_Condition",
                "[Condition] IN ('New', 'Good', 'Worn', 'Damaged', 'Lost')"));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.DiscNumber).IsRequired().HasMaxLength(40);
        builder.Property(c => c.Shelf).HasMaxLength(40);
        builder.Property(c => c.Condition).IsRequired().HasMaxLength(20).HasDefaultValue("Good");

        // унікальний інвентарний номер — бізнес-ключ диска
        builder.HasIndex(c => c.DiscNumber)
               .IsUnique()
               .HasDatabaseName("UQ_DiscCopies_DiscNumber");

        // індекс під запит: «усі диски фільму зі станом»
        builder.HasIndex(c => c.MovieId)
               .HasDatabaseName("IX_DiscCopies_MovieId")
               .IncludeProperties(c => new { c.Condition, c.Shelf });
    }
}

/// <summary>
/// Конфігурація <see cref="MovieGenre"/> — зв'язок <b>M:N</b> через ЯВНУ
/// проміжну сутність із власними полями.
/// </summary>
/// <remarks>
/// Автоматичний M:N в EF Core (<c>HasMany().WithMany()</c>) створив би «чисту»
/// таблицю лише з двома зовнішніми ключами й зберегти <c>AddedAt</c>,
/// <c>AddedBy</c>, <c>IsPrimary</c> не дозволив би. Саме через ці поля
/// проміжну сутність описано явно.
/// </remarks>
public sealed class MovieGenreConfiguration : IEntityTypeConfiguration<MovieGenre>
{
    public void Configure(EntityTypeBuilder<MovieGenre> builder)
    {
        builder.ToTable("MovieGenres");

        // складений первинний ключ: пара (фільм, жанр) не може повторитися
        builder.HasKey(bg => new { bg.MovieId, bg.GenreId });

        builder.Property(bg => bg.AddedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(bg => bg.AddedBy).HasMaxLength(100);
        builder.Property(bg => bg.IsPrimary).HasDefaultValue(false);

        builder.HasOne(bg => bg.Movie)
               .WithMany(b => b.MovieGenres)
               .HasForeignKey(bg => bg.MovieId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(bg => bg.Genre)
               .WithMany(g => g.MovieGenres)
               .HasForeignKey(bg => bg.GenreId)
               .OnDelete(DeleteBehavior.Restrict);   // жанр-довідник не видаляємо каскадно

        // індекс під запит: «нові фільми жанру за період»
        // (LINQ-запит через проміжну сутність, Частина 2.2)
        builder.HasIndex(bg => new { bg.GenreId, bg.AddedAt })
               .HasDatabaseName("IX_MovieGenres_GenreId_AddedAt")
               .IsDescending(false, true)
               .IncludeProperties(bg => bg.MovieId);
    }
}

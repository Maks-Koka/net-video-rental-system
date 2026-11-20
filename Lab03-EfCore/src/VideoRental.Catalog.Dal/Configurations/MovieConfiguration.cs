using VideoRental.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace VideoRental.Catalog.Dal.Configurations;

/// <summary>
/// Конфігурація <see cref="Movie"/> через Fluent API.
/// </summary>
/// <remarks>
/// Окремий клас <see cref="IEntityTypeConfiguration{TEntity}"/> на кожну
/// сутність тримає логіку конфігурації відокремленою й не «захаращує» класи
/// сутностей атрибутами. Усі конфігурації підключаються централізовано в
/// <c>OnModelCreating</c> одним викликом
/// <c>ApplyConfigurationsFromAssembly</c>.
/// </remarks>
public sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable("Movies", t =>
        {
            t.HasCheckConstraint("CK_Movies_DailyRate", "[DailyRate] > 0");
            t.HasCheckConstraint("CK_Movies_AvailableDiscs", "[AvailableDiscs] >= 0");
            t.HasCheckConstraint("CK_Movies_ReleaseYear",
                "[ReleaseYear] IS NULL OR ([ReleaseYear] >= 1400 AND [ReleaseYear] <= 2100)");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Title).IsRequired().HasMaxLength(300);
        builder.Property(b => b.Barcode).IsRequired().HasMaxLength(20);
        builder.Property(b => b.DailyRate).HasColumnType("decimal(18,2)");
        builder.Property(b => b.AvailableDiscs).HasDefaultValue(0);
        builder.Property(b => b.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(b => b.IsDeleted).HasDefaultValue(false);

        // оптимістична конкурентність: SQL Server сам оновлює ROWVERSION
        builder.Property(b => b.RowVersion).IsRowVersion();

        // --- індекси (кожен під конкретний запит) ---

        // унікальний бізнес-ключ; повторне створення -> 409 Conflict
        builder.HasIndex(b => b.Barcode).IsUnique().HasDatabaseName("UQ_Movies_Barcode");

        // «список фільмів із пошуком за назвою» — покриваючий індекс для пагінації
        builder.HasIndex(b => b.Title)
               .HasDatabaseName("IX_Movies_Title")
               .IncludeProperties(b => new { b.DailyRate, b.AvailableDiscs, b.DirectorId });

        // «усі фільми режисера» — під JOIN з Directors
        builder.HasIndex(b => b.DirectorId).HasDatabaseName("IX_Movies_DirectorId");

        // «наявні в каталозі фільму» — фільтрований індекс лише для «живих» записів
        builder.HasIndex(b => b.Title)
               .HasDatabaseName("IX_Movies_Active")
               .HasFilter("[IsDeleted] = 0");

        // --- зв'язки ---

        // N:1 до Directors (з боку режисера — 1:N). БЕЗ каскаду:
        // режисера з фільмами видаляти не можна.
        builder.HasOne(b => b.Director)
               .WithMany(a => a.Movies)
               .HasForeignKey(b => b.DirectorId)
               .OnDelete(DeleteBehavior.Restrict);

        // 1:1 до MovieDetails. PK залежної сутності є водночас FK,
        // тому двох описів на один фільм фізично бути не може.
        builder.HasOne(b => b.Detail)
               .WithOne()
               .HasForeignKey<MovieDetail>(d => d.MovieId)
               .OnDelete(DeleteBehavior.Cascade);

        // 1:N до DiscCopies. Каскад доречний: диск не існує без фільму.
        builder.HasMany(b => b.Discs)
               .WithOne()
               .HasForeignKey(c => c.MovieId)
               .OnDelete(DeleteBehavior.Cascade);

        // M:N до Genres через MovieGenres налаштовано в MovieGenreConfiguration

        // навігаційні колекції заповнює EF Core через backing field
        builder.Navigation(b => b.Discs).UsePropertyAccessMode(PropertyAccessMode.Property);
        builder.Navigation(b => b.MovieGenres).UsePropertyAccessMode(PropertyAccessMode.Property);
        builder.Navigation(b => b.Detail).UsePropertyAccessMode(PropertyAccessMode.Property);
    }
}

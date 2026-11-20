using VideoRental.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace VideoRental.Catalog.Dal;

/// <summary>
/// Контекст бази даних Каталогу (Проєкт №2 з Лаб. 01), побудований
/// підходом <b>Code-First</b>: схема генерується з моделей міграціями.
/// </summary>
/// <remarks>
/// Усі конфігурації сутностей підключаються ЦЕНТРАЛІЗОВАНО одним викликом
/// <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/> — це гарантує
/// однаковий підхід до всіх моделей і не дає забути нову конфігурацію.
/// </remarks>
public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
        : base(options) { }

    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Director> Directors => Set<Director>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<MovieDetail> MovieDetails => Set<MovieDetail>();
    public DbSet<DiscCopy> DiscCopies => Set<DiscCopy>();
    public DbSet<MovieGenre> MovieGenres => Set<MovieGenre>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // усі IEntityTypeConfiguration<T> зі збірки DAL — одним рядком
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}

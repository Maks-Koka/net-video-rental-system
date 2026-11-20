using System.ComponentModel.DataAnnotations;

namespace VideoRental.Catalog.Bll.Dtos;

/* =============================================================================
   DTO та контракт посторінкової відповіді.
   Два рівні валідації (Частина 4.3 Лаб. 03):
     - прості правила  -> DataAnnotations тут, на DTO;
     - складні правила -> FluentValidation у Validators/.
   Дублювання правил свідомо уникаємо: DataAnnotations описують ФОРМАТ
   (обов'язковість, довжина, діапазон), FluentValidation — СКЛАДНІ правила
   (регулярні вирази Barcode, взаємозалежні поля).
   ============================================================================= */

/// <summary>
/// Уніфікований контракт посторінкової відповіді.
/// Метадані повертаються і в ТІЛІ відповіді, і в заголовках
/// (<c>X-Pagination-*</c>) — тіло зручне для клієнта, заголовки для кешування.
/// </summary>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

// ------------------------------------------------------------------ Directors ---

public record DirectorDto(int Id, string FullName, string? Country, int? BirthYear);

public record DirectorWithMoviesDto(
    int Id, string FullName, string? Country, int? BirthYear,
    IReadOnlyList<MovieListItemDto> Movies);

public class CreateDirectorDto
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    [StringLength(100)]
    public string? Country { get; set; }

    [Range(1000, 2100)]
    public int? BirthYear { get; set; }
}

// ------------------------------------------------------------------- Genres ---

public record GenreDto(int Id, string Name, string Slug, string? Description);

public record GenreStatsDto(int GenreId, string GenreName, int MovieCount);

// -------------------------------------------------------------------- Movies ---

public record MovieListItemDto(
    long Id, string Title, string Barcode, decimal DailyRate,
    int AvailableDiscs, int? ReleaseYear,
    int DirectorId, string DirectorName,
    IReadOnlyList<string> Genres);

public record DiscCopyDto(long Id, string DiscNumber, string? Shelf,
                          string Condition, DateOnly? AcquiredAt);

public record MovieDetailDto(string Description, string? Studio,
                            int? DurationMinutes, string Language, string? CoverUrl);

public record MovieDto(
    long Id, string Title, string Barcode, decimal DailyRate,
    int AvailableDiscs, int? ReleaseYear,
    int DirectorId, string DirectorName,
    MovieDetailDto? Detail,
    IReadOnlyList<DiscCopyDto> Discs,
    IReadOnlyList<string> Genres,
    DateTime CreatedAt, DateTime? UpdatedAt);

public class CreateMovieDto
{
    [Required, StringLength(300, MinimumLength = 1)]
    public string Title { get; set; } = "";

    [Required, StringLength(20, MinimumLength = 10)]
    public string Barcode { get; set; } = "";

    [Range(0.01, 100_000)]
    public decimal DailyRate { get; set; }

    [Range(1, int.MaxValue)]
    public int DirectorId { get; set; }

    [Range(1400, 2100)]
    public int? ReleaseYear { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(200)]
    public string? Studio { get; set; }

    [Range(1, 100_000)]
    public int? DurationMinutes { get; set; }

    public List<int> GenreIds { get; set; } = [];
}

public class UpdateMovieDto
{
    [Required, StringLength(300, MinimumLength = 1)]
    public string Title { get; set; } = "";

    [Range(0.01, 100_000)]
    public decimal DailyRate { get; set; }
}

public class ChangeStockDto
{
    [Range(1, 10_000)]
    public int Quantity { get; set; }
}

public class AddDiscDto
{
    [Required, StringLength(40, MinimumLength = 3)]
    public string DiscNumber { get; set; } = "";

    [StringLength(40)]
    public string? Shelf { get; set; }

    [Required]
    public string Condition { get; set; } = "Good";

    public DateOnly? AcquiredAt { get; set; }
}

public class AssignGenreDto
{
    [Range(1, int.MaxValue)]
    public int GenreId { get; set; }

    public bool IsPrimary { get; set; }
}

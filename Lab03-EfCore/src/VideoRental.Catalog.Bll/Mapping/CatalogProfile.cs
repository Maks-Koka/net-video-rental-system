using AutoMapper;
using VideoRental.Catalog.Bll.Dtos;
using VideoRental.Catalog.Domain.Entities;

namespace VideoRental.Catalog.Bll.Mapping;

/// <summary>
/// Профілі мапінгу AutoMapper. Підключаються централізовано в Program.cs;
/// конфігурація перевіряється тестом <c>AssertConfigurationIsValid()</c>.
/// </summary>
public sealed class CatalogProfile : Profile
{
    public CatalogProfile()
    {
        CreateMap<Director, DirectorDto>();

        CreateMap<Director, DirectorWithMoviesDto>();

        CreateMap<Genre, GenreDto>();

        CreateMap<MovieDetail, MovieDetailDto>();

        CreateMap<DiscCopy, DiscCopyDto>();

        CreateMap<Movie, MovieListItemDto>()
            .ForCtorParam(nameof(MovieListItemDto.DirectorName),
                o => o.MapFrom(b => b.Director != null ? b.Director.FullName : ""))
            .ForCtorParam(nameof(MovieListItemDto.Genres),
                o => o.MapFrom(b => b.MovieGenres
                    .Where(bg => bg.Genre != null)
                    .Select(bg => bg.Genre.Name)
                    .ToList()));

        CreateMap<Movie, MovieDto>()
            .ForCtorParam(nameof(MovieDto.DirectorName),
                o => o.MapFrom(b => b.Director != null ? b.Director.FullName : ""))
            .ForCtorParam(nameof(MovieDto.Genres),
                o => o.MapFrom(b => b.MovieGenres
                    .Where(bg => bg.Genre != null)
                    .Select(bg => bg.Genre.Name)
                    .ToList()));
    }
}

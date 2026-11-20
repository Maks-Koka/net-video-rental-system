using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VideoRental.Catalog.Dal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Directors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BirthYear = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Directors", x => x.Id);
                    table.CheckConstraint("CK_Directors_BirthYear", "[BirthYear] IS NULL OR ([BirthYear] >= 1000 AND [BirthYear] <= 2100)");
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DailyRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AvailableDiscs = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ReleaseYear = table.Column<int>(type: "int", nullable: true),
                    DirectorId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                    table.CheckConstraint("CK_Movies_AvailableDiscs", "[AvailableDiscs] >= 0");
                    table.CheckConstraint("CK_Movies_DailyRate", "[DailyRate] > 0");
                    table.CheckConstraint("CK_Movies_ReleaseYear", "[ReleaseYear] IS NULL OR ([ReleaseYear] >= 1400 AND [ReleaseYear] <= 2100)");
                    table.ForeignKey(
                        name: "FK_Movies_Directors_DirectorId",
                        column: x => x.DirectorId,
                        principalTable: "Directors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiscCopies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MovieId = table.Column<long>(type: "bigint", nullable: false),
                    DiscNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Shelf = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Good"),
                    AcquiredAt = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscCopies", x => x.Id);
                    table.CheckConstraint("CK_DiscCopies_Condition", "[Condition] IN ('New', 'Good', 'Worn', 'Damaged', 'Lost')");
                    table.ForeignKey(
                        name: "FK_DiscCopies_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovieDetails",
                columns: table => new
                {
                    MovieId = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Studio = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "uk"),
                    CoverUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieDetails", x => x.MovieId);
                    table.CheckConstraint("CK_MovieDetails_DurationMinutes", "[DurationMinutes] IS NULL OR [DurationMinutes] > 0");
                    table.ForeignKey(
                        name: "FK_MovieDetails_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovieGenres",
                columns: table => new
                {
                    MovieId = table.Column<long>(type: "bigint", nullable: false),
                    GenreId = table.Column<int>(type: "int", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    AddedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieGenres", x => new { x.MovieId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_MovieGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovieGenres_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Directors",
                columns: new[] { "Id", "BirthYear", "Country", "CreatedAt", "FullName", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, 1924, "Вірменія", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Сергій Параджанов", null },
                    { 2, 1894, "Україна", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Олександр Довженко", null },
                    { 3, 1941, "Україна", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Борис Івченко", null },
                    { 4, 1972, "Україна", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Олесь Санін", null },
                    { 5, 1972, "Україна", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Ахтем Сеітаблаєв", null },
                    { 6, 1993, "Україна", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Антоніо Лукіч", null }
                });

            migrationBuilder.InsertData(
                table: "Genres",
                columns: new[] { "Id", "CreatedAt", "Description", "Name", "Slug" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Комедійні фільми", "Комедія", "comedy" },
                    { 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Фільми на історичному матеріалі", "Історичний", "historical" },
                    { 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Драматичні фільми", "Драма", "drama" },
                    { 4, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Класика українського кіно", "Класика", "classic" },
                    { 5, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Документальне кіно", "Документальний", "documentary" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Directors_FullName",
                table: "Directors",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_DiscCopies_MovieId",
                table: "DiscCopies",
                column: "MovieId")
                .Annotation("SqlServer:Include", new[] { "Condition", "Shelf" });

            migrationBuilder.CreateIndex(
                name: "UQ_DiscCopies_DiscNumber",
                table: "DiscCopies",
                column: "DiscNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Genres_Name",
                table: "Genres",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Genres_Slug",
                table: "Genres",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovieGenres_GenreId_AddedAt",
                table: "MovieGenres",
                columns: new[] { "GenreId", "AddedAt" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "MovieId" });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_Active",
                table: "Movies",
                column: "Title",
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "DailyRate", "AvailableDiscs", "DirectorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_DirectorId",
                table: "Movies",
                column: "DirectorId");

            migrationBuilder.CreateIndex(
                name: "UQ_Movies_Barcode",
                table: "Movies",
                column: "Barcode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscCopies");

            migrationBuilder.DropTable(
                name: "MovieDetails");

            migrationBuilder.DropTable(
                name: "MovieGenres");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Movies");

            migrationBuilder.DropTable(
                name: "Directors");
        }
    }
}

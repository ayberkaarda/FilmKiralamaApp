using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmKiralama.Data.Persistence.Migrations
{
    /// <summary>
    /// Adds the version of the films (Surum), which every change of a film's details through the admin's
    /// form raises, so a form loaded before another admin's change cannot write over it. Existing films
    /// start at 1, like a new one. The default constraint is named, so a fresh and an upgraded database
    /// have the same schema.
    /// </summary>
    public partial class AddFilmVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [Filmler] ADD [Surum] int NOT NULL CONSTRAINT [DF_Filmler_Surum] DEFAULT 1;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Filmler_Surum",
                table: "Filmler",
                sql: "[Surum] >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Filmler_Surum",
                table: "Filmler");

            migrationBuilder.Sql("ALTER TABLE [Filmler] DROP CONSTRAINT [DF_Filmler_Surum];");
            migrationBuilder.DropColumn(
                name: "Surum",
                table: "Filmler");
        }
    }
}

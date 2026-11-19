using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmKiralama.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The default gets a fixed name, so a fresh and an upgraded database have the same schema.
            migrationBuilder.Sql("ALTER TABLE [Degerlendirmeler] ADD [Silindi] bit NOT NULL CONSTRAINT [DF_Degerlendirmeler_Silindi] DEFAULT CAST(0 AS bit);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [Degerlendirmeler] DROP CONSTRAINT [DF_Degerlendirmeler_Silindi];");
            migrationBuilder.DropColumn(
                name: "Silindi",
                table: "Degerlendirmeler");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmKiralama.Data.Persistence.Migrations
{
    /// <summary>
    /// Adds the session version of the accounts (OturumSurumu), which a new password or a new role raises
    /// so that every session issued before is refused. Existing accounts start at 0, like a new one. The
    /// default constraint is named, so a fresh and an upgraded database have the same schema.
    /// </summary>
    public partial class AddSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE [Kullanicilar] ADD [OturumSurumu] int NOT NULL CONSTRAINT [DF_Kullanicilar_OturumSurumu] DEFAULT 0;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Kullanicilar_OturumSurumu",
                table: "Kullanicilar",
                sql: "[OturumSurumu] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Kullanicilar_OturumSurumu",
                table: "Kullanicilar");

            migrationBuilder.Sql("ALTER TABLE [Kullanicilar] DROP CONSTRAINT [DF_Kullanicilar_OturumSurumu];");
            migrationBuilder.DropColumn(
                name: "OturumSurumu",
                table: "Kullanicilar");
        }
    }
}

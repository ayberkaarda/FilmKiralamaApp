using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmKiralama.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Degerlendirmeler",
                columns: table => new
                {
                    DegerlendirmeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KullaniciID = table.Column<int>(type: "int", nullable: false),
                    FilmID = table.Column<int>(type: "int", nullable: false),
                    Puan = table.Column<int>(type: "int", nullable: false),
                    Yorum = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Gizli = table.Column<bool>(type: "bit", nullable: false),
                    GuncellemeTarihi = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Degerlendirmeler", x => x.DegerlendirmeID);
                    table.CheckConstraint("CK_Degerlendirmeler_Puan", "[Puan] >= 1 AND [Puan] <= 5");
                    table.ForeignKey(
                        name: "FK_Degerlendirmeler_Filmler",
                        column: x => x.FilmID,
                        principalTable: "Filmler",
                        principalColumn: "FilmID");
                    table.ForeignKey(
                        name: "FK_Degerlendirmeler_Kullanicilar",
                        column: x => x.KullaniciID,
                        principalTable: "Kullanicilar",
                        principalColumn: "KullaniciID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Degerlendirmeler_FilmID_Gizli",
                table: "Degerlendirmeler",
                columns: new[] { "FilmID", "Gizli" });

            migrationBuilder.CreateIndex(
                name: "IX_Degerlendirmeler_KullaniciID_FilmID",
                table: "Degerlendirmeler",
                columns: new[] { "KullaniciID", "FilmID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Degerlendirmeler");
        }
    }
}

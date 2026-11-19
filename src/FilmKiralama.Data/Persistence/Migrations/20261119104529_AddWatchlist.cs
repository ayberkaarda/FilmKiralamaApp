using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmKiralama.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IzlemeListesi",
                columns: table => new
                {
                    IzlemeID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KullaniciID = table.Column<int>(type: "int", nullable: false),
                    FilmID = table.Column<int>(type: "int", nullable: false),
                    EklenmeTarihi = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IzlemeListesi", x => x.IzlemeID);
                    table.ForeignKey(
                        name: "FK_IzlemeListesi_Filmler",
                        column: x => x.FilmID,
                        principalTable: "Filmler",
                        principalColumn: "FilmID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IzlemeListesi_Kullanicilar",
                        column: x => x.KullaniciID,
                        principalTable: "Kullanicilar",
                        principalColumn: "KullaniciID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IzlemeListesi_FilmID",
                table: "IzlemeListesi",
                column: "FilmID");

            migrationBuilder.CreateIndex(
                name: "IX_IzlemeListesi_KullaniciID_FilmID",
                table: "IzlemeListesi",
                columns: new[] { "KullaniciID", "FilmID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IzlemeListesi");
        }
    }
}

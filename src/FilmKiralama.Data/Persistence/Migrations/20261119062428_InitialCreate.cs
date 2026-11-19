using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmKiralama.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Filmler",
                columns: table => new
                {
                    FilmID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ad = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Tur = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Yil = table.Column<int>(type: "int", nullable: false),
                    Yonetmen = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    GunlukUcret = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Image = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    Hakkinda = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Adet = table.Column<int>(type: "int", nullable: false),
                    MevcutAdet = table.Column<int>(type: "int", nullable: false),
                    KiralamayaAcik = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Filmler", x => x.FilmID);
                    table.CheckConstraint("CK_Filmler_Adet", "[Adet] >= 0 AND [MevcutAdet] >= 0 AND [MevcutAdet] <= [Adet]");
                    table.CheckConstraint("CK_Filmler_GunlukUcret", "[GunlukUcret] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Kullanicilar",
                columns: table => new
                {
                    KullaniciID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KullaniciAdi = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Sifre = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SifreBicimi = table.Column<int>(type: "int", nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Eposta = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Telefon = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    BasarisizGiris = table.Column<int>(type: "int", nullable: false),
                    KilitBitis = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kullanicilar", x => x.KullaniciID);
                    table.CheckConstraint("CK_Kullanicilar_BasarisizGiris", "[BasarisizGiris] >= 0");
                    table.CheckConstraint("CK_Kullanicilar_Rol", "[Rol] IN ('Kullanici', 'Admin')");
                    table.CheckConstraint("CK_Kullanicilar_SifreBicimi", "[SifreBicimi] IN (0, 1)");
                });

            migrationBuilder.CreateTable(
                name: "KurulumAnahtarlari",
                columns: table => new
                {
                    AnahtarID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnahtarOzeti = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    Olusturma = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SonKullanma = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Tuketildi = table.Column<bool>(type: "bit", nullable: false),
                    TuketimTarihi = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KurulumAnahtarlari", x => x.AnahtarID);
                });

            migrationBuilder.CreateTable(
                name: "Kiralama",
                columns: table => new
                {
                    KiralamaID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FilmID = table.Column<int>(type: "int", nullable: false),
                    KullaniciID = table.Column<int>(type: "int", nullable: false),
                    KiralamaTarihi = table.Column<DateOnly>(type: "date", nullable: false),
                    PlanlananIadeTarihi = table.Column<DateOnly>(type: "date", nullable: true),
                    GercekIadeTarihi = table.Column<DateOnly>(type: "date", nullable: true),
                    Durum = table.Column<int>(type: "int", nullable: false),
                    Ucret = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    GecikmeUcreti = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kiralama", x => x.KiralamaID);
                    table.CheckConstraint("CK_Kiralama_Durum", "[Durum] IN (0, 1)");
                    table.CheckConstraint("CK_Kiralama_Iade", "([Durum] = 0 AND [GercekIadeTarihi] IS NULL) OR ([Durum] = 1 AND [GercekIadeTarihi] IS NOT NULL)");
                    table.CheckConstraint("CK_Kiralama_Ucret", "[Ucret] >= 0 AND ([GecikmeUcreti] IS NULL OR [GecikmeUcreti] >= 0)");
                    table.ForeignKey(
                        name: "FK_Kiralama_Filmler",
                        column: x => x.FilmID,
                        principalTable: "Filmler",
                        principalColumn: "FilmID");
                    table.ForeignKey(
                        name: "FK_Kiralama_Kullanicilar",
                        column: x => x.KullaniciID,
                        principalTable: "Kullanicilar",
                        principalColumn: "KullaniciID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Filmler_Tur",
                table: "Filmler",
                column: "Tur");

            migrationBuilder.CreateIndex(
                name: "IX_Filmler_Yil",
                table: "Filmler",
                column: "Yil");

            migrationBuilder.CreateIndex(
                name: "IX_Filmler_Yonetmen",
                table: "Filmler",
                column: "Yonetmen");

            migrationBuilder.CreateIndex(
                name: "IX_Kiralama_FilmID",
                table: "Kiralama",
                column: "FilmID");

            migrationBuilder.CreateIndex(
                name: "IX_Kiralama_KullaniciID",
                table: "Kiralama",
                column: "KullaniciID");

            migrationBuilder.CreateIndex(
                name: "IX_Kiralama_KullaniciID_FilmID",
                table: "Kiralama",
                columns: new[] { "KullaniciID", "FilmID" },
                unique: true,
                filter: "[Durum] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Kullanicilar_Eposta",
                table: "Kullanicilar",
                column: "Eposta",
                unique: true,
                filter: "[Eposta] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Kullanicilar_KullaniciAdi",
                table: "Kullanicilar",
                column: "KullaniciAdi",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kullanicilar_Telefon",
                table: "Kullanicilar",
                column: "Telefon",
                unique: true,
                filter: "[Telefon] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Kiralama");

            migrationBuilder.DropTable(
                name: "KurulumAnahtarlari");

            migrationBuilder.DropTable(
                name: "Filmler");

            migrationBuilder.DropTable(
                name: "Kullanicilar");
        }
    }
}

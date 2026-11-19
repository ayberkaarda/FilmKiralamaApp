using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilmKiralama.Data.Persistence.Configurations;

internal sealed class WatchlistConfiguration : IEntityTypeConfiguration<WatchlistEntry>
{
    public void Configure(EntityTypeBuilder<WatchlistEntry> builder)
    {
        builder.ToTable("IzlemeListesi");

        builder.HasKey(w => w.Id).HasName("PK_IzlemeListesi");
        builder.Property(w => w.Id).HasColumnName("IzlemeID").ValueGeneratedOnAdd();
        builder.Property(w => w.UserId).HasColumnName("KullaniciID");
        builder.Property(w => w.FilmId).HasColumnName("FilmID");
        builder.Property(w => w.AddedAt).HasColumnName("EklenmeTarihi");

        // A list is no history: deleting a film that was never rented, or an account without rentals,
        // takes its entries with it.
        builder.HasOne<Film>().WithMany().HasForeignKey(w => w.FilmId).HasConstraintName("FK_IzlemeListesi_Filmler").OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.UserId).HasConstraintName("FK_IzlemeListesi_Kullanicilar").OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(w => new { w.UserId, w.FilmId }, "IX_IzlemeListesi_KullaniciID_FilmID").IsUnique();
        builder.HasIndex(w => w.FilmId, "IX_IzlemeListesi_FilmID");
    }
}

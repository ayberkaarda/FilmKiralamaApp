using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilmKiralama.Data.Persistence.Configurations;

internal sealed class RentalConfiguration : IEntityTypeConfiguration<Rental>
{
    public void Configure(EntityTypeBuilder<Rental> builder)
    {
        builder.ToTable("Kiralama", table =>
        {
            table.HasCheckConstraint("CK_Kiralama_Durum", SchemaLimits.RentalStatusCheck);
            table.HasCheckConstraint("CK_Kiralama_Iade", SchemaLimits.RentalReturnCheck);
            table.HasCheckConstraint("CK_Kiralama_Ucret", SchemaLimits.RentalPriceCheck);
        });

        builder.HasKey(r => r.Id).HasName("PK_Kiralama");
        builder.Property(r => r.Id).HasColumnName("KiralamaID").ValueGeneratedOnAdd();
        builder.Property(r => r.FilmId).HasColumnName("FilmID");
        builder.Property(r => r.UserId).HasColumnName("KullaniciID");
        builder.Property(r => r.RentedOn).HasColumnName("KiralamaTarihi");
        builder.Property(r => r.DueOn).HasColumnName("PlanlananIadeTarihi");
        builder.Property(r => r.ReturnedOn).HasColumnName("GercekIadeTarihi");
        builder.Property(r => r.Status).HasColumnName("Durum");
        builder.Property(r => r.Price).HasColumnName("Ucret").HasPrecision(SchemaLimits.MoneyPrecision, SchemaLimits.MoneyScale);
        builder.Property(r => r.LateFee).HasColumnName("GecikmeUcreti").HasPrecision(SchemaLimits.MoneyPrecision, SchemaLimits.MoneyScale);

        // Rentals keep films and accounts: neither can be deleted while it has rental history.
        builder.HasOne<Film>().WithMany().HasForeignKey(r => r.FilmId).HasConstraintName("FK_Kiralama_Filmler").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).HasConstraintName("FK_Kiralama_Kullanicilar").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(r => r.FilmId, "IX_Kiralama_FilmID");
        builder.HasIndex(r => r.UserId, "IX_Kiralama_KullaniciID");
        builder.HasIndex(r => new { r.UserId, r.FilmId }, "IX_Kiralama_KullaniciID_FilmID").IsUnique().HasFilter(SchemaLimits.ActiveRentalFilter);
    }
}

using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilmKiralama.Data.Persistence.Configurations;

internal sealed class FilmConfiguration : IEntityTypeConfiguration<Film>
{
    public void Configure(EntityTypeBuilder<Film> builder)
    {
        builder.ToTable("Filmler", table =>
        {
            table.HasCheckConstraint("CK_Filmler_GunlukUcret", SchemaLimits.DailyPriceCheck);
            table.HasCheckConstraint("CK_Filmler_Adet", SchemaLimits.CopiesCheck);
            table.HasCheckConstraint("CK_Filmler_Surum", SchemaLimits.FilmVersionCheck);
        });

        builder.HasKey(f => f.Id).HasName("PK_Filmler");
        builder.Property(f => f.Id).HasColumnName("FilmID").ValueGeneratedOnAdd();
        builder.Property(f => f.Title).HasColumnName("Ad").HasMaxLength(SchemaLimits.TitleLength).IsRequired();
        builder.Property(f => f.Genre).HasColumnName("Tur").HasMaxLength(SchemaLimits.GenreLength).IsRequired();
        builder.Property(f => f.Year).HasColumnName("Yil");
        builder.Property(f => f.Director).HasColumnName("Yonetmen").HasMaxLength(SchemaLimits.DirectorLength).IsRequired();
        builder.Property(f => f.DailyPrice).HasColumnName("GunlukUcret").HasPrecision(SchemaLimits.MoneyPrecision, SchemaLimits.MoneyScale);
        builder.Property(f => f.ImageUrl).HasColumnName("Image").HasMaxLength(SchemaLimits.ImageUrlLength).IsUnicode(false);
        builder.Property(f => f.About).HasColumnName("Hakkinda");
        builder.Property(f => f.TotalCopies).HasColumnName("Adet");
        builder.Property(f => f.AvailableCopies).HasColumnName("MevcutAdet");
        builder.Property(f => f.IsListed).HasColumnName("KiralamayaAcik");
        builder.Property(f => f.Version).HasColumnName("Surum").HasDefaultValue(1);

        builder.HasIndex(f => f.Genre, "IX_Filmler_Tur");
        builder.HasIndex(f => f.Director, "IX_Filmler_Yonetmen");
        builder.HasIndex(f => f.Year, "IX_Filmler_Yil");
    }
}

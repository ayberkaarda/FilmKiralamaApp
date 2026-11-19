using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilmKiralama.Data.Persistence.Configurations;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Degerlendirmeler", table => table.HasCheckConstraint("CK_Degerlendirmeler_Puan", SchemaLimits.RatingCheck));

        builder.HasKey(r => r.Id).HasName("PK_Degerlendirmeler");
        builder.Property(r => r.Id).HasColumnName("DegerlendirmeID").ValueGeneratedOnAdd();
        builder.Property(r => r.UserId).HasColumnName("KullaniciID");
        builder.Property(r => r.FilmId).HasColumnName("FilmID");
        builder.Property(r => r.Rating).HasColumnName("Puan");
        builder.Property(r => r.Comment).HasColumnName("Yorum").HasMaxLength(SchemaLimits.CommentLength);
        builder.Property(r => r.IsHidden).HasColumnName("Gizli");
        builder.Property(r => r.IsDeleted).HasColumnName("Silindi").HasDefaultValue(false);
        builder.Property(r => r.UpdatedAt).HasColumnName("GuncellemeTarihi");

        // A review needs a returned rental, and rentals keep films and accounts, so neither is deleted
        // while it has reviews.
        builder.HasOne<Film>().WithMany().HasForeignKey(r => r.FilmId).HasConstraintName("FK_Degerlendirmeler_Filmler").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).HasConstraintName("FK_Degerlendirmeler_Kullanicilar").OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(r => new { r.UserId, r.FilmId }, "IX_Degerlendirmeler_KullaniciID_FilmID").IsUnique();
        builder.HasIndex(r => new { r.FilmId, r.IsHidden }, "IX_Degerlendirmeler_FilmID_Gizli");
    }
}

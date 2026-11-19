using FilmKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FilmKiralama.Data.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>Roles are stored as "Kullanici" or "Admin"; the legacy upgrade rewrites the original values.</summary>
    internal static readonly ValueConverter<UserRole, string> RoleConverter = new(
        role => role == UserRole.Admin ? SchemaLimits.AdminRoleValue : SchemaLimits.UserRoleValue,
        value => string.Equals(value, SchemaLimits.AdminRoleValue, StringComparison.Ordinal) ? UserRole.Admin : UserRole.User);

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Kullanicilar", table =>
        {
            table.HasCheckConstraint("CK_Kullanicilar_Rol", SchemaLimits.RoleCheck);
            table.HasCheckConstraint("CK_Kullanicilar_BasarisizGiris", SchemaLimits.FailedLoginCheck);
            table.HasCheckConstraint("CK_Kullanicilar_SifreBicimi", SchemaLimits.PasswordFormatCheck);
            table.HasCheckConstraint("CK_Kullanicilar_OturumSurumu", SchemaLimits.SessionVersionCheck);
        });

        builder.HasKey(u => u.Id).HasName("PK_Kullanicilar");
        builder.Property(u => u.Id).HasColumnName("KullaniciID").ValueGeneratedOnAdd();
        builder.Property(u => u.UserName).HasColumnName("KullaniciAdi").HasMaxLength(SchemaLimits.UserNameLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("Sifre").HasMaxLength(SchemaLimits.PasswordLength).IsRequired();
        builder.Property(u => u.PasswordFormat).HasColumnName("SifreBicimi");
        builder.Property(u => u.Role).HasColumnName("Rol").HasMaxLength(SchemaLimits.RoleLength).HasConversion(RoleConverter).IsRequired();
        builder.Property(u => u.Email).HasColumnName("Eposta").HasMaxLength(SchemaLimits.EmailLength);
        builder.Property(u => u.Phone).HasColumnName("Telefon").HasMaxLength(SchemaLimits.PhoneLength).IsUnicode(false);
        builder.Property(u => u.FailedLoginCount).HasColumnName("BasarisizGiris");
        builder.Property(u => u.LockoutEnd).HasColumnName("KilitBitis");
        builder.Property(u => u.SessionVersion).HasColumnName("OturumSurumu").HasDefaultValue(0);

        builder.HasIndex(u => u.UserName, "IX_Kullanicilar_KullaniciAdi").IsUnique();
        builder.HasIndex(u => u.Email, "IX_Kullanicilar_Eposta").IsUnique().HasFilter(SchemaLimits.EmailFilter);
        builder.HasIndex(u => u.Phone, "IX_Kullanicilar_Telefon").IsUnique().HasFilter(SchemaLimits.PhoneFilter);
    }
}

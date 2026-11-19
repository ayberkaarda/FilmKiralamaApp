namespace FilmKiralama.Data.Persistence;

/// <summary>
/// Column sizes and value rules enforced by the database. The business layer applies the same or
/// stricter rules; the legacy upgrade checks existing rows against these values.
/// </summary>
internal static class SchemaLimits
{
    /// <summary>Sizes of the original columns (NVARCHAR(50), NVARCHAR(100), VARCHAR(15), VARCHAR(255)).</summary>
    public const int UserNameLength = 50;
    public const int EmailLength = 100;
    public const int PhoneLength = 15;
    public const int TitleLength = 50;
    public const int GenreLength = 50;
    public const int DirectorLength = 50;
    public const int ImageUrlLength = 255;

    /// <summary>Room for a PBKDF2 hash; the original column held plain text in 50 characters.</summary>
    public const int PasswordLength = 256;

    public const int RoleLength = 20;

    /// <summary>Money columns: DECIMAL(10, 2), as in the original schema.</summary>
    public const int MoneyPrecision = 10;
    public const int MoneyScale = 2;

    /// <summary>Length of a SHA-256 hash of a setup key.</summary>
    public const int SetupKeyHashLength = 32;

    /// <summary>Stored values of <see cref="Domain.Entities.UserRole"/>.</summary>
    public const string UserRoleValue = "Kullanici";
    public const string AdminRoleValue = "Admin";

    // Plain literals: the same text has to work on SQLite, which has no N'' prefix.
    public const string RoleCheck = "[Rol] IN ('Kullanici', 'Admin')";
    public const string FailedLoginCheck = "[BasarisizGiris] >= 0";

    /// <summary>Values of <see cref="Domain.Entities.PasswordFormat"/>: 0 PBKDF2 hash, 1 legacy plain text.</summary>
    public const string PasswordFormatCheck = "[SifreBicimi] IN (0, 1)";

    public const string DailyPriceCheck = "[GunlukUcret] > 0";

    /// <summary>The shelf can neither go negative nor hold more copies than the shop owns.</summary>
    public const string CopiesCheck = "[Adet] >= 0 AND [MevcutAdet] >= 0 AND [MevcutAdet] <= [Adet]";

    /// <summary>Values of <see cref="Domain.Entities.RentalStatus"/>: 0 active, 1 returned.</summary>
    public const string RentalStatusCheck = "[Durum] IN (0, 1)";

    /// <summary>A rental has a return day exactly when it is returned.</summary>
    public const string RentalReturnCheck =
        "([Durum] = 0 AND [GercekIadeTarihi] IS NULL) OR ([Durum] = 1 AND [GercekIadeTarihi] IS NOT NULL)";

    public const string RentalPriceCheck = "[Ucret] >= 0 AND ([GecikmeUcreti] IS NULL OR [GecikmeUcreti] >= 0)";

    /// <summary>Only active rentals hold a copy; an account can rent the same film once at a time.</summary>
    public const string ActiveRentalFilter = "[Durum] = 0";

    public const string EmailFilter = "[Eposta] IS NOT NULL";
    public const string PhoneFilter = "[Telefon] IS NOT NULL";
}

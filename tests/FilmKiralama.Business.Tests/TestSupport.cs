using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Services;
using FilmKiralama.Business.Validation;
using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Business.Tests;

internal static class Sessions
{
    public static UserSession User { get; } = new(2, "ornek", UserRole.User, sessionVersion: 0);

    public static UserSession OtherUser { get; } = new(5, "baska", UserRole.User, sessionVersion: 0);

    public static UserSession Admin { get; } = new(1, "yonetici", UserRole.Admin, sessionVersion: 0);
}

/// <summary>The validators the services get from the container.</summary>
internal static class TestValidators
{
    public static SignInInputValidator SignIn { get; } = new();

    public static SignUpInputValidator SignUp { get; } = new();

    public static PasswordChangeInputValidator PasswordChange { get; } = new();

    public static FirstAdminInputValidator FirstAdmin { get; } = new();

    public static NewAccountInputValidator NewAccount { get; } = new();

    public static AccountChangeInputValidator AccountChange { get; } = new();

    public static FilmFieldsValidator Film { get; } = new();

    public static RentalQuoteInputValidator Quote(RentalPolicy policy) => new(policy);
}

/// <summary>Collects audit events as code and role, as a log would show them.</summary>
internal sealed class RecordingAuditLog : IAuditLog
{
    public List<string> Entries { get; } = [];

    public void Record(AuditEvent auditEvent, AuditRole role) =>
        Entries.Add(AuditCodes.Of(auditEvent) + " " + AuditCodes.Of(role));
}

using FilmKiralama.Business.Auditing;
using FilmKiralama.Business.Services;
using FilmKiralama.Domain.Entities;

namespace FilmKiralama.Business.Tests;

internal static class Sessions
{
    public static UserSession User { get; } = new(2, "ornek", UserRole.User);

    public static UserSession OtherUser { get; } = new(5, "baska", UserRole.User);

    public static UserSession Admin { get; } = new(1, "yonetici", UserRole.Admin);
}

/// <summary>Collects audit events as code and role, as a log would show them.</summary>
internal sealed class RecordingAuditLog : IAuditLog
{
    public List<string> Entries { get; } = [];

    public void Record(AuditEvent auditEvent, AuditRole role) =>
        Entries.Add(AuditCodes.Of(auditEvent) + " " + AuditCodes.Of(role));
}

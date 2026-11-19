using System.Diagnostics;
using FilmKiralama.Business.Auditing;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>
/// Writes audit events to the trace listeners as a fixed code and a role, nothing else. A file log
/// replaces it later; until then the events are visible to an attached debugger or a trace listener.
/// </summary>
internal sealed class TraceAuditLog : IAuditLog
{
    public void Record(AuditEvent auditEvent, AuditRole role) =>
        Trace.TraceInformation("audit {0} role={1}", AuditCodes.Of(auditEvent), AuditCodes.Of(role));
}

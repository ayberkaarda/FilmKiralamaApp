using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using FilmKiralama.Business;
using FilmKiralama.Data.Persistence;
using Serilog;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>
/// Turns exceptions into short, user friendly Turkish messages and writes what may be logged about them
/// (see <see cref="Record"/>) to the log file.
/// </summary>
internal static class ErrorReporter
{
    private const int MaxLoggedExceptions = 8;

    internal const string UnexpectedErrorMessage = "Beklenmeyen bir hata oluştu. İşleminiz tamamlanamamış olabilir.";

    internal const string DatabaseInitializationMessage =
        "Veritabanı hazırlanamadı. SQL Server'ın çalıştığından, bağlantı ayarlarının doğru olduğundan "
        + "ve veritabanı oluşturma yetkiniz bulunduğundan emin olun.";

    internal const string DatabaseMessage =
        "Veritabanı işlemi tamamlanamadı. SQL Server'ın çalıştığından ve bağlantı ayarlarının doğru olduğundan emin olun.";

    internal const string BusyMessage = "İşlem meşgul: aynı kayıt üzerinde başka bir işlem sürüyor. Lütfen biraz sonra tekrar deneyin.";

    internal const string UpgradeBusyMessage =
        "Veritabanı şu anda başka bir açılışta güncelleniyor. Lütfen güncelleme bittikten sonra uygulamayı yeniden açın.";

    internal const string TimeoutMessage = "İşlem zaman aşımına uğradı. Lütfen tekrar deneyin.";

    public static void Show(Exception? exception, IWin32Window? owner = null)
    {
        if (exception is null)
        {
            return;
        }

        Record(Log.Logger, exception);

        // Rule and permission messages are fixed texts written for the user; they are shown as warnings.
        if (IsRefusal(exception))
        {
            Dialogs.Show(owner, exception.Message, "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Dialogs.Show(owner, Describe(exception), AppText.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>
    /// Writes what may be logged about <paramref name="exception"/> to <paramref name="logger"/>. A refused
    /// operation (a business rule or a permission) is an answer for the screen and leaves no entry: the
    /// security relevant refusals are already counted by the audit log with fixed codes, and an entry of
    /// its own would tell a taken sign-up from a new account, or a refused sign-in from another. Anything
    /// else is an error described by <see cref="FormatForLog"/>, without any exception message.
    /// </summary>
    internal static void Record(ILogger logger, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        if (IsRefusal(exception))
        {
            return;
        }

        logger.Error("Operation failed: {Details}", FormatForLog(exception));
    }

    private static bool IsRefusal(Exception exception) => exception is BusinessRuleException or AccessDeniedException;

    /// <summary>
    /// Builds a log entry that carries no exception messages and no file paths: exception messages can
    /// contain server names, connection details or personal data such as user names or e-mail
    /// addresses. Only the exception types, their error codes and the methods of each stack frame are
    /// written, for the whole inner exception chain.
    /// </summary>
    public static string FormatForLog(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        var depth = 0;
        for (Exception? current = exception; current is not null && depth < MaxLoggedExceptions; depth++)
        {
            if (depth > 0)
            {
                builder.AppendLine("--- inner exception ---");
            }

            builder.Append(current.GetType().FullName)
                .Append(" (HResult 0x")
                .Append(current.HResult.ToString("X8", CultureInfo.InvariantCulture))
                .AppendLine(")");

            foreach (var frame in new StackTrace(current, fNeedFileInfo: false).GetFrames())
            {
                var method = frame.GetMethod();
                if (method is not null)
                {
                    builder.Append("   at ")
                        .Append(method.DeclaringType?.FullName ?? "<unknown>")
                        .Append('.')
                        .AppendLine(method.Name);
                }
            }

            current = current is AggregateException { InnerExceptions.Count: > 0 } aggregate
                ? aggregate.InnerExceptions[0]
                : current.InnerException;
        }

        return builder.ToString();
    }

    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Find<DatabaseInitializationException>(exception) is not null)
        {
            // Another start held the upgrade lock for longer than this one waits.
            return Find<AppLockNotGrantedException>(exception) is not null ? UpgradeBusyMessage : DatabaseInitializationMessage;
        }

        if (Find<AppLockNotGrantedException>(exception) is not null)
        {
            return BusyMessage;
        }

        if (Find<DbException>(exception) is not null)
        {
            return DatabaseMessage;
        }

        if (Find<TimeoutException>(exception) is not null)
        {
            return TimeoutMessage;
        }

        return UnexpectedErrorMessage;
    }

    private static T? Find<T>(Exception exception)
        where T : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }

            if (current is AggregateException { InnerExceptions.Count: > 0 } aggregate)
            {
                current = aggregate.InnerExceptions[0];
                if (current is T inner)
                {
                    return inner;
                }
            }
        }

        return null;
    }
}

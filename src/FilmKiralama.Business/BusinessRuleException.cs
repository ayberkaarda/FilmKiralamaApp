namespace FilmKiralama.Business;

/// <summary>
/// Raised when an operation violates a business rule. The message is user facing (Turkish), fixed and
/// free of entered values, so it can be shown to the user and written to a log as is.
/// </summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException()
    {
    }

    public BusinessRuleException(string message)
        : base(message)
    {
    }

    public BusinessRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>A refused input with one fixed message per invalid field (keys are the input's property names).</summary>
    public BusinessRuleException(string message, IReadOnlyDictionary<string, string> fieldErrors)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(fieldErrors);
        FieldErrors = fieldErrors;
    }

    /// <summary>The invalid fields of the input and why; empty for other rules.</summary>
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = new Dictionary<string, string>();
}

/// <summary>
/// Raised when the signed-in account tries an operation its role does not allow. The message is fixed
/// and does not reveal whether the requested record exists.
/// </summary>
public sealed class AccessDeniedException : Exception
{
    internal const string DefaultMessage = "Bu işlem için yetkiniz yok.";

    /// <summary>
    /// The one answer when the signed-in account was deleted or lost the role an operation needs after
    /// the sign-in; the session has to end.
    /// </summary>
    internal const string SessionRevokedMessage =
        "Hesabınızın yetkileri değişti veya hesabınız artık bulunmuyor. Lütfen oturumu kapatıp yeniden giriş yapın.";

    public AccessDeniedException()
        : base(DefaultMessage)
    {
    }

    public AccessDeniedException(string message)
        : base(message)
    {
    }

    public AccessDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

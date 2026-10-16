namespace GuardianConnect.Shared;

// Pipe representation of an Exception: Type / Message / StackTrace plus a
// recursive InnerException. Deserializes to a plain System.Exception, since
// arbitrary derived types cannot be reconstructed safely; the original type's
// FullName is prefixed onto the message and the stack trace is kept in
// Data["RemoteStackTrace"].
public sealed class WireException
{
    public string? Type { get; set; }
    public string? Message { get; set; }
    public string? StackTrace { get; set; }
    public WireException? InnerException { get; set; }

    internal static WireException? From(Exception? exception)
    {
        if (exception is null) return null;
        return new WireException
        {
            Type = exception.GetType().FullName ?? exception.GetType().Name,
            Message = exception.Message,
            StackTrace = exception.StackTrace,
            InnerException = From(exception.InnerException)
        };
    }

    internal Exception ToException()
    {
        var message = string.IsNullOrEmpty(Type) ? (Message ?? "") : $"[{Type}] {Message}";
        var inner = InnerException?.ToException();
        var ex = inner is null ? new Exception(message) : new Exception(message, inner);
        if (!string.IsNullOrEmpty(StackTrace)) ex.Data["RemoteStackTrace"] = StackTrace;
        return ex;
    }
}

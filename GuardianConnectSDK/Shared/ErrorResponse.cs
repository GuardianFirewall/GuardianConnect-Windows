using GuardianConnect.Shared.Extensions;

namespace GuardianConnect.Shared;

public record ErrorResponse(
    string MessageArg = "",
    Object? ThrownExceptionArg = null,
    bool IsErrorArg = false,
    object? ResponseArg = null,
    object? DataArg = null)
{
    public bool IsError = IsErrorArg;
    public string Message = MessageArg;
    public object? ThrownException = ThrownExceptionArg;
    public object? Response = ResponseArg;
    public object? Data = DataArg;

    public static ErrorResponse FromException(Exception exception)
    {
        return new ErrorResponse
        {
            IsError = true,
            Message = exception.Message,
            ThrownException = exception
        };
    }

    public ErrorResponse WithException(Exception exception)
    {
        this.SetException(exception);
        return this;
    }
    
    public string GetReasonPhrase()
    {
        var resp = (HttpResponseMessage)Response!;
        return resp.ReasonPhrase ?? "";
    }

    public override string ToString()
    {
        string xt = string.Empty;
        if (ThrownException != null)
        {
            Exception tx = (Exception) ThrownException;
            var innerX = tx.InnerException != null ? tx.InnerException.ToString() : string.Empty;
            xt = $"Exception: Message = {tx.Message}, StackTrace = {tx.StackTrace}, InnerException = {innerX}";
        }
        var message = string.IsNullOrEmpty(Message) ? "" : Message;
        var response = (HttpResponseMessage)Response!;
        var logText = $"ErrorResponse: IsError: {IsError}, Message: {message}, Exception: {xt}, Response: {response}, Data: {Data ?? string.Empty}";
        return logText;
    }
}
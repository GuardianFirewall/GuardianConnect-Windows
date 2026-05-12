using System.Text.Json.Serialization;
using GuardianConnect.Shared.Extensions;

namespace GuardianConnect.Shared;

public record ErrorResponse(
    string MessageArg = "",
    Exception? ThrownExceptionArg = null,
    bool IsErrorArg = false,
    object? ResponseArg = null,
    object? DataArg = null,
    HttpResponseMessage? HttpResponse = null,
    GRDAPIError? GrdapiErrorArg = null)
{
    public bool IsError { get; set; } = IsErrorArg;

    public string Message { get; set; } = MessageArg;

    // Typed as Exception (not object) so the JsonConverter is picked up by
    // System.Text.Json static-type dispatch — without that, STJ walks the
    // runtime Exception and chokes on `TargetSite` (a MethodBase).
    [JsonConverter(typeof(ExceptionJsonConverter))]
    public Exception? ThrownException { get; set; } = ThrownExceptionArg;
    public object? Response { get; set; } = ResponseArg;
    public object? GRDApiError { get; set; } = GrdapiErrorArg;
    public object? Data { get; set; } = DataArg;

    [JsonIgnore]
    public HttpResponseMessage HttpResponse { get; set; } = (HttpResponse ?? null) ?? new HttpResponseMessage();

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

    public ErrorResponse WithApiError(GRDAPIError error)
    {
        GRDApiError = error;
        return this;
    }

    public string GetReasonPhrase()
    {
        var resp = Response as HttpResponseMessage ?? new HttpResponseMessage();
        return resp.ReasonPhrase ?? "";
    }

    public override string ToString()
    {
        var xt = string.Empty;
        if (ThrownException is { } tx)
        {
            var innerX = tx.InnerException != null ? tx.InnerException.ToString() : string.Empty;
            xt = $"Exception: Message = {tx.Message}, StackTrace = {tx.StackTrace}, InnerException = {innerX}";
        }

        var message = string.IsNullOrEmpty(Message) ? "" : Message;
        var response = (HttpResponseMessage)Response!;
        var logText =
            $"ErrorResponse: IsError: {IsError}, Message: {message}, Exception: {xt}, Response: {response}, Data: {Data ?? string.Empty}, HttpResponse: [{HttpResponse.StatusCode}]{HttpResponse.ReasonPhrase}";
        return logText;
    }
}
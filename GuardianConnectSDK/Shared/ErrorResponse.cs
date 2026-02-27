using System.Text.Json.Serialization;
using GuardianConnect.Shared.Extensions;

namespace GuardianConnect.Shared;

public record ErrorResponse(
    string MessageArg = "",
    Object? ThrownExceptionArg = null,
    bool IsErrorArg = false,
    object? ResponseArg = null,
    object? DataArg = null,
    HttpResponseMessage? HttpResponse = null,
    GRDAPIError? GrdapiErrorArg = null)
{
    public bool IsError { get; set; } = IsErrorArg;

    public string Message { get; set; } = MessageArg;
    public object? ThrownException { get; set; } = ThrownExceptionArg;
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
        string xt = string.Empty;
        if (ThrownException != null)
        {
            Exception tx = (Exception) ThrownException;
            var innerX = tx.InnerException != null ? tx.InnerException.ToString() : string.Empty;
            xt = $"Exception: Message = {tx.Message}, StackTrace = {tx.StackTrace}, InnerException = {innerX}";
        }
        var message = string.IsNullOrEmpty(Message) ? "" : Message;
        var response = (HttpResponseMessage)Response!;
        var logText = $"ErrorResponse: IsError: {IsError}, Message: {message}, Exception: {xt}, Response: {response}, Data: {Data ?? string.Empty}, HttpResponse: [{HttpResponse.StatusCode}]{HttpResponse.ReasonPhrase}";
        return logText;
    }
}
using System.Text.Json.Serialization;
using GuardianConnect.Shared.Extensions;

namespace GuardianConnect.Shared;

public record ErrorResponse(
    [property: JsonIgnore] string MessageArg = "",
    [property: JsonIgnore] Exception? ThrownExceptionArg = null,
    [property: JsonIgnore] bool IsErrorArg = false,
    [property: JsonIgnore] object? ResponseArg = null,
    [property: JsonIgnore] object? DataArg = null,
    HttpResponseMessage? HttpResponse = null,
    [property: JsonIgnore] GRDAPIError? GrdapiErrorArg = null)
{
    // Deserialization entry point, so the source generator does not emit
    // metadata for the primary constructor's Exception parameter.
    [JsonConstructor]
    public ErrorResponse() : this("")
    {
    }

    public bool IsError { get; set; } = IsErrorArg;

    public string Message { get; set; } = MessageArg;

    // Serialized through ThrownExceptionWire. Source-generated metadata for
    // Exception reads `TargetSite`, which is trim-unsafe and not serializable.
    [JsonIgnore]
    public Exception? ThrownException { get; set; } = ThrownExceptionArg;

    [JsonInclude]
    [JsonPropertyName("ThrownException")]
    internal WireException? ThrownExceptionWire
    {
        get => WireException.From(ThrownException);
        set => ThrownException = value?.ToException();
    }
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
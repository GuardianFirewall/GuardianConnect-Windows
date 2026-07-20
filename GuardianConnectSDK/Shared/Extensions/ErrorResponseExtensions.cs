using System.Net;

namespace GuardianConnect.Shared.Extensions;

public static class ErrorResponseExtensions
{
    public static ErrorResponse SetException(this ErrorResponse er, Exception exception)
    {
        er.IsError = true;
        er.ThrownException = exception;
        if (string.IsNullOrEmpty(er.Message)) er.Message = exception.Message;
        return er;
    }

    public static ErrorResponse SetErrorMessage(this ErrorResponse er, string message)
    {
        er.IsError = true;
        er.Message = message;
        return er;
    }

    public static ErrorResponse SetResponse(this ErrorResponse er, HttpResponseMessage response)
    {
        er.IsError = !response.IsSuccessStatusCode;
        er.Message = response.ReasonPhrase ?? "";
        er.HttpResponse = response;
        return er;
    }

    public static ErrorResponse SetData(this ErrorResponse er, object data)
    {
        er.Data = data;
        return er;
    }

    public static string ToString(this HttpResponseMessage response)
    {
        if (response is null) return string.Empty;
        var text =
            $"{response.ReasonPhrase}, IsSuccess: {response.IsSuccessStatusCode}, StatusCode: {response.StatusCode}";
        return text;
    }

    public static ErrorResponse SetGrdApiError(this ErrorResponse er, Dictionary<string, object?>? data,
        HttpStatusCode statusCode)
    {
        er.IsError = true;
        er.GRDApiError = new GRDAPIError(data?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), statusCode);
        return er;
    }

    public static ErrorResponse SetGrdApiError(this ErrorResponse er, GRDAPIError error)
    {
        // (was: `return er.SetGrdApiError(error);` — infinite self-recursion;
        // latent until the v1.4 error plumbing became the first caller)
        er.IsError = true;
        er.GRDApiError = error;
        if (string.IsNullOrEmpty(er.Message) && !string.IsNullOrEmpty(error.Message))
            er.Message = string.IsNullOrEmpty(error.Title) ? error.Message : $"{error.Title}: {error.Message}";
        return er;
    }
}
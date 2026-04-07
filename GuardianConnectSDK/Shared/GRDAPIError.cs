using System.Net;
using System.Text.Json.Serialization;

namespace GuardianConnect.Shared;

public class GRDAPIError
{
    public GRDAPIError(Dictionary<string, object>? dict, HttpStatusCode statusCode)
    {
        Title = "";
        Message = "";
        if (dict == null)
        {
            Title = @"Failed to parse error";
            Message = @"Failed to parse the API error message returned by the server";
        }
        else
        {
            Title = (dict.ContainsKey("error-title") ? dict["error-title"].ToString() : "") ?? "";
            Message = (dict.ContainsKey("error-message") ? dict["error-message"].ToString() : "") ?? "";
            StatusCode = (int)statusCode;
        }
    }

    [JsonPropertyName("error-title")] public string Title { get; set; }
    [JsonPropertyName("error-message")] public string Message { get; set; }
    public int StatusCode { get; set; }
}
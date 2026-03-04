using System.Net;
using System.Text.Json.Serialization;

namespace GuardianConnect.Shared;

public class GRDAPIError
{
    [JsonPropertyName("error-title")]
    public string Title { get; set; }
    [JsonPropertyName("error-message")]
    public string Message { get; set; }
    public int StatusCode { get; set; }

    public GRDAPIError (Dictionary<string, object> data, HttpStatusCode statusCode)
    {
        if (data == null)
        {
 			Title 		= @"Failed to parse error";
 			Message 	= @"Failed to parse the API error message returned by the server";
        }
        else
        {

            Title = data.ContainsKey("error-title") ? data["error-title"].ToString() : string.Empty;
            Message = data.ContainsKey("error-message") ? data["error-message"].ToString() : string.Empty;
            StatusCode = (int)statusCode;
        }
    }
}
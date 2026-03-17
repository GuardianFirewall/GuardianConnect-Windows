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

    public GRDAPIError (Dictionary<string, object>? dict, HttpStatusCode statusCode)
    {
        if (dict == null)
        {
 			Title 		= @"Failed to parse error";
 			Message 	= @"Failed to parse the API error message returned by the server";
        }
        else
        {

            Title = dict.ContainsKey("error-title") ? dict["error-title"].ToString() : string.Empty;
            Message = dict.ContainsKey("error-message") ? dict["error-message"].ToString() : string.Empty;
            StatusCode = (int)statusCode;
        }
    }
}
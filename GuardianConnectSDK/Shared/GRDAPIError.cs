using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.Shared;

/// <summary>
/// The standardized API error body. As of the v1.4 SGW API this exact shape —
/// <c>{"error-title", "error-message"}</c> — is returned for EVERY non-200/201
/// response in both the Connect API and SGW environments, and the doc
/// explicitly blesses showing title/message to the user ("plain english
/// explanation of what has gone wrong").
/// </summary>
public class GRDAPIError
{
    public GRDAPIError()
    {
        Title = "";
        Message = "";
    }

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

    /// <summary>
    /// Parse a raw non-2xx response body into a <see cref="GRDAPIError"/>.
    /// Never throws: a body that isn't the standardized error JSON (older
    /// hosts, proxies, HTML error pages) degrades to a "Failed to parse"
    /// error carrying the status code, so callers can rely on Title/Message
    /// always being present. AOT-safe (source-generated serializer context).
    /// </summary>
    public static GRDAPIError FromResponseBody(string? body, HttpStatusCode statusCode)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize(body, GRDAPIErrorJsonContext.Default.GRDAPIError);
                if (parsed is not null &&
                    (!string.IsNullOrEmpty(parsed.Title) || !string.IsNullOrEmpty(parsed.Message)))
                {
                    parsed.StatusCode = (int)statusCode;
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // fall through to the unparsable shape below
            }
        }

        return new GRDAPIError
        {
            Title = "Failed to parse error",
            Message = "Failed to parse the API error message returned by the server",
            StatusCode = (int)statusCode,
        };
    }

    [JsonPropertyName("error-title")] public string Title { get; set; }
    [JsonPropertyName("error-message")] public string Message { get; set; }
    [JsonIgnore] public int StatusCode { get; set; }
}

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(GRDAPIError))]
public partial class GRDAPIErrorJsonContext : JsonSerializerContext
{
}

using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials;

public class GRDLoginCredentials
{
    public GRDLoginCredentials(string userEmail, string userPassword)
    {
        Email = userEmail;
        Password = userPassword;
    }

    [JsonConstructor]
    public GRDLoginCredentials()
    {
    }

    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
}
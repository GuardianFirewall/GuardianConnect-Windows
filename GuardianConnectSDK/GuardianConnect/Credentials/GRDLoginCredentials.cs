using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GuardianConnect.Credentials
{
    public class GRDLoginCredentials
    {
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;

        public GRDLoginCredentials(string userEmail, string userPassword)
        {
            Email = userEmail;
            Password = userPassword;
        }

        [JsonConstructor]
        public GRDLoginCredentials()
        {
        }
    }
}

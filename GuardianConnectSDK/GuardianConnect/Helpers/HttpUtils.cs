using System.Text.Json;

namespace GuardianConnect.Helpers
{
    public static class HttpUtils
    {
        private static HttpClient _client;
        private static JsonSerializerOptions _serializerOptions;
        public static HttpClient Client
        {
            get
            {
                _client ??= new HttpClient();

                return _client;
            }
        }

        public static JsonSerializerOptions SerializerOptions {
            get
            {

                _serializerOptions ??= new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    };

                return _serializerOptions;
            }
        }
    }
}

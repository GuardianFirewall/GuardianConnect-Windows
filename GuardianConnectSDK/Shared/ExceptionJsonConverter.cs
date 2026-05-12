using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.Shared;

// Serializes Exception across the pipe without dragging in reflection-bound
// members that System.Text.Json refuses to handle. By default STJ walks an
// Exception's runtime properties and trips on `TargetSite` (a MethodBase),
// which throws `Serialization and deserialization of System.Reflection.MethodBase
// instances is not supported`. We flatten to Type / Message / StackTrace plus a
// recursive InnerException so the client can log what failed and rebuild a
// best-effort Exception on the other side.
//
// Read produces a plain System.Exception (not the original derived type) — we
// can't safely reconstruct arbitrary derived types over the wire. The Type
// FullName is prefixed onto the message so callers still see what kind of
// failure it was.
public class ExceptionJsonConverter : JsonConverter<Exception>
{
    public override Exception? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected StartObject for Exception, got {reader.TokenType}");

        string? type = null;
        string? message = null;
        string? stackTrace = null;
        Exception? inner = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException($"Expected PropertyName, got {reader.TokenType}");

            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "Type": type = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                case "Message": message = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                case "StackTrace": stackTrace = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                case "InnerException":
                    inner = reader.TokenType == JsonTokenType.Null ? null : Read(ref reader, typeof(Exception), options);
                    break;
                default: reader.Skip(); break;
            }
        }

        var prefixedMessage = string.IsNullOrEmpty(type) ? (message ?? "") : $"[{type}] {message}";
        var ex = inner is null ? new Exception(prefixedMessage) : new Exception(prefixedMessage, inner);
        if (!string.IsNullOrEmpty(stackTrace)) ex.Data["RemoteStackTrace"] = stackTrace;
        return ex;
    }

    public override void Write(Utf8JsonWriter writer, Exception value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Type", value.GetType().FullName ?? value.GetType().Name);
        writer.WriteString("Message", value.Message);
        if (value.StackTrace is { } st) writer.WriteString("StackTrace", st);
        if (value.InnerException is { } inner)
        {
            writer.WritePropertyName("InnerException");
            Write(writer, inner, options);
        }
        writer.WriteEndObject();
    }
}

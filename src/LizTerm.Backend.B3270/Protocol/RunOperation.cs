using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LizTerm.Backend.B3270.Protocol;

public static class RunOperation
{
    public static string Serialize(string tag, IReadOnlyList<B3270Action> actions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        // Relaxed escaping keeps quotes as \" and leaves UTF-8 text alone (b3270 runs with -utf8).
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("run");
            writer.WriteString("r-tag", tag);
            writer.WriteStartArray("actions");
            foreach (var action in actions)
            {
                writer.WriteStartObject();
                writer.WriteString("action", action.Name);
                if (action.Args.Length > 0)
                {
                    writer.WriteStartArray("args");
                    foreach (var arg in action.Args) writer.WriteStringValue(arg);
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}

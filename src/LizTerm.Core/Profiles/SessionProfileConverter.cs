using System.Text.Json;
using System.Text.Json.Serialization;
using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

/// <summary>Custom converter that applies the new DestructiveBackspace default (true) when deserializing
/// profiles saved before that field existed.</summary>
internal sealed class SessionProfileConverter : JsonConverter<SessionProfile>
{
    public override SessionProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var name = root.GetProperty("name").GetString() ?? "";
        var host = root.GetProperty("host").GetString() ?? "";
        var port = root.TryGetProperty("port", out var portEl) ? portEl.GetInt32() : 23;
        var useTls = root.TryGetProperty("useTls", out var useTlsEl) && useTlsEl.GetBoolean();
        var verifyCertificate = !root.TryGetProperty("verifyCertificate", out var verifyCertEl) || verifyCertEl.GetBoolean();
        var model = root.TryGetProperty("model", out var modelEl) ? modelEl.GetInt32() : 2;
        var extended = !root.TryGetProperty("extended", out var extendedEl) || extendedEl.GetBoolean();
        var codePage = root.TryGetProperty("codePage", out var codePageEl) ? codePageEl.GetString() ?? "cp037" : "cp037";
        var luName = root.TryGetProperty("luName", out var luNameEl) && luNameEl.ValueKind != JsonValueKind.Null ? luNameEl.GetString() : null;
        // When the field is missing, use the new default (true) instead of the CLR default (false).
        var destructiveBackspace = !root.TryGetProperty("destructiveBackspace", out var dbEl) || dbEl.GetBoolean();

        CertificatePin? pinnedCertificate = null;
        if (root.TryGetProperty("pinnedCertificate", out var pinEl) && pinEl.ValueKind != JsonValueKind.Null)
        {
            var sha256 = pinEl.GetProperty("sha256").GetString() ?? "";
            var subject = pinEl.GetProperty("subject").GetString() ?? "";
            var pem = pinEl.GetProperty("pem").GetString() ?? "";
            pinnedCertificate = new CertificatePin(sha256, subject, pem);
        }

        return new SessionProfile
        {
            Name = name,
            Host = host,
            Port = port,
            UseTls = useTls,
            VerifyCertificate = verifyCertificate,
            Model = model,
            Extended = extended,
            CodePage = codePage,
            LuName = luName,
            DestructiveBackspace = destructiveBackspace,
            PinnedCertificate = pinnedCertificate,
        };
    }

    public override void Write(Utf8JsonWriter writer, SessionProfile value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        writer.WriteString("host", value.Host);
        writer.WriteNumber("port", value.Port);
        writer.WriteBoolean("useTls", value.UseTls);
        writer.WriteBoolean("verifyCertificate", value.VerifyCertificate);
        if (value.PinnedCertificate is not null)
        {
            writer.WritePropertyName("pinnedCertificate");
            writer.WriteStartObject();
            writer.WriteString("sha256", value.PinnedCertificate.Sha256);
            writer.WriteString("subject", value.PinnedCertificate.Subject);
            writer.WriteString("pem", value.PinnedCertificate.Pem);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("pinnedCertificate");
        }
        writer.WriteNumber("model", value.Model);
        writer.WriteBoolean("extended", value.Extended);
        writer.WriteString("codePage", value.CodePage);
        if (value.LuName is not null)
            writer.WriteString("luName", value.LuName);
        else
            writer.WriteNull("luName");
        writer.WriteBoolean("destructiveBackspace", value.DestructiveBackspace);
        writer.WriteEndObject();
    }
}

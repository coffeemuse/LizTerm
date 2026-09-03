using System.Text.Json.Serialization;
using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionProfile))]
internal partial class ProfileJsonContext : JsonSerializerContext;

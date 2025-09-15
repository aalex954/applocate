using AppLocate.Core.Models;

namespace AppLocate.Cli {
    [JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(AppHit))]
    [JsonSerializable(typeof(IReadOnlyList<AppHit>))]
    internal partial class AppLocateJsonContext : JsonSerializerContext { }
}

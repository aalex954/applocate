using System.Collections.Generic;
using System.Text.Json.Serialization;
using AppLocate.Core.Models;
using AppLocate.Core.Indexing;

namespace AppLocate.Cli;

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppHit))]
[JsonSerializable(typeof(IReadOnlyList<AppHit>))]
[JsonSerializable(typeof(IndexFile))]
[JsonSerializable(typeof(IndexRecord))]
[JsonSerializable(typeof(IndexEntry))]
internal partial class AppLocateJsonContext : JsonSerializerContext { }

using System.Text.Json.Serialization;

namespace Sweeft.Core;

/// <summary>
/// Source-generated JSON context for <see cref="AppConfig"/>. Using the source
/// generator (instead of reflection-based serialization) keeps configuration
/// load/save compatible with NativeAOT and trimming.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppConfig))]
internal partial class AppConfigJsonContext : JsonSerializerContext
{
}

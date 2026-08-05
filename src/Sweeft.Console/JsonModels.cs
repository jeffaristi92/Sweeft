using System.Text.Json.Serialization;

namespace Sweeft.ConsoleApp;

/// <summary>Shape of the <c>--json</c> output (camelCase keys).</summary>
internal sealed record JsonReport(
    long TotalReclaimableBytes,
    string TotalReclaimableHuman,
    int Count,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<JsonFinding> Findings);

internal sealed record JsonFinding(
    string Kind,
    string Path,
    long SizeBytes,
    string SizeHuman,
    int AgeDays,
    DateTime LastModifiedUtc,
    string Reason,
    string? RepoRoot,
    string RepoStatus,
    DateTime? ProjectLastActivityUtc,
    int? ProjectIdleDays);

/// <summary>Source-generated JSON context so <c>--json</c> works under NativeAOT.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JsonReport))]
internal partial class ReportJsonContext : JsonSerializerContext
{
}

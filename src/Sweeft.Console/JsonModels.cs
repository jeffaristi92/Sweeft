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

/// <summary>Shape of the <c>--top --json</c> disk-usage output.</summary>
internal sealed record JsonDiskUsage(
    string Root,
    long TotalBytes,
    string TotalHuman,
    int Count,
    IReadOnlyList<JsonDiskEntry> Entries);

internal sealed record JsonDiskEntry(
    string Path,
    string Name,
    long SizeBytes,
    string SizeHuman,
    bool IsDirectory);

/// <summary>Source-generated JSON context so <c>--json</c> works under NativeAOT.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JsonReport))]
[JsonSerializable(typeof(JsonDiskUsage))]
internal partial class ReportJsonContext : JsonSerializerContext
{
}

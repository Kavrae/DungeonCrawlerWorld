using System.Globalization;
using System.Text.Json;

namespace Workshop.Debug;

/// <summary>Lists the report files in the report folders, newest first, each with what it was run with -- read from the file's own fields.</summary>
/// <remarks>
/// The kind comes from the file's name (DiagnosticsReportKind); a file of no known kind is left out. The game's reports name
/// their fields in PascalCase and the phase-performance-testing skill's in camelCase, so fields are found either way. A file
/// that won't read is still listed, its detail saying why. Reads every file, so it runs off the UI thread.
/// </remarks>
public static class DiagnosticsReportCatalog
{
    private const string SavedRunTimestampFormat = "yyyyMMdd-HHmmss";

    public static IReadOnlyList<DiagnosticsReportEntry> Scan(IEnumerable<string> directories)
    {
        var entries = new List<DiagnosticsReportEntry>();
        foreach (var directory in directories.Where(Directory.Exists))
        {
            foreach (var filePath in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (KindOf(Path.GetFileName(filePath)) is { } kind)
                {
                    entries.Add(Read(kind, filePath));
                }
            }
        }

        entries.Sort(static (first, second) => second.TimestampUtc.CompareTo(first.TimestampUtc));
        return entries;
    }

    /// <summary>The kind fileName names; null for a file of no known kind.</summary>
    public static DiagnosticsReportKind? KindOf(string fileName)
    {
        if (fileName.Equals("latest.json", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticsReportKind.Periodic;
        }

        if (fileName.StartsWith("memory-ab-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fileName switch
        {
            _ when fileName.StartsWith("benchmark-", StringComparison.OrdinalIgnoreCase) => DiagnosticsReportKind.Benchmark,
            _ when fileName.StartsWith("memory-", StringComparison.OrdinalIgnoreCase) => DiagnosticsReportKind.Memory,
            _ when fileName.StartsWith("gauges-", StringComparison.OrdinalIgnoreCase) => DiagnosticsReportKind.Gauges,
            _ when fileName.StartsWith("startup-", StringComparison.OrdinalIgnoreCase) => DiagnosticsReportKind.Startup,
            _ when fileName.StartsWith("ab-", StringComparison.OrdinalIgnoreCase) => DiagnosticsReportKind.AbComparison,
            _ when IsSavedRunName(fileName) => DiagnosticsReportKind.SavedRun,
            _ => null,
        };
    }

    private static bool IsSavedRunName(string fileName) =>
        DateTime.TryParseExact(Path.GetFileNameWithoutExtension(fileName), SavedRunTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static DiagnosticsReportEntry Read(DiagnosticsReportKind kind, string filePath)
    {
        var fileTime = File.GetLastWriteTimeUtc(filePath);
        try
        {
            using var document = JsonDocument.Parse(ReadJson(filePath));
            var root = document.RootElement;
            var timestamp = Field(root, "TimestampUtc") is { ValueKind: JsonValueKind.String } time && time.TryGetDateTime(out var parsed) ? parsed.ToUniversalTime() : fileTime;
            var seed = Number(root, "RandomSeed") ?? Number(root, "Seed");
            return new DiagnosticsReportEntry(kind, filePath, timestamp, seed is { } value ? (int)value : null, Number(root, "StartFrame"), Number(root, "EndFrame"), DetailOf(kind, root), ContentsOf(kind, root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new DiagnosticsReportEntry(kind, filePath, fileTime, null, null, null, $"Unreadable: {exception.Message}", ContentsOf(kind, null));
        }
    }

    /// <summary>What a report of kind holds: fixed for most kinds, a periodic report's by which of its sections are there (root, when it read).</summary>
    private static IReadOnlyCollection<LiveView> ContentsOf(DiagnosticsReportKind kind, JsonElement? root) => kind switch
    {
        DiagnosticsReportKind.Benchmark or DiagnosticsReportKind.SavedRun or DiagnosticsReportKind.AbComparison => [LiveView.FrameCosts],
        DiagnosticsReportKind.Memory => [LiveView.Memory],
        DiagnosticsReportKind.Startup => [LiveView.Startup],
        DiagnosticsReportKind.Gauges => [LiveView.Gauges],
        DiagnosticsReportKind.Periodic when root is { } report =>
        [
            .. new[] { ("FrameBudget", LiveView.FrameCosts), ("Memory", LiveView.Memory), ("Leaks", LiveView.Leaks), ("Gauges", LiveView.Gauges) }
                .Where(section => Field(report, section.Item1) is { ValueKind: JsonValueKind.Object or JsonValueKind.Array })
                .Select(static section => section.Item2),
        ],
        _ => [],
    };

    private static string DetailOf(DiagnosticsReportKind kind, JsonElement root) => kind switch
    {
        DiagnosticsReportKind.Periodic => $"Features: {Text(root, "Features")}",
        DiagnosticsReportKind.Benchmark => $"pid {Number(root, "ProcessId")}, {Double(root, "WallClockMilliseconds") / 1000:N1} s wall clock",
        DiagnosticsReportKind.Memory => $"{Double(root, "TotalPoolBytes") / (1024 * 1024):N1} MB in pools, {Number(root, "LivingEntities"):N0} entities",
        DiagnosticsReportKind.Gauges => $"{(Field(root, "Gauges") is { ValueKind: JsonValueKind.Array } gauges ? gauges.GetArrayLength() - 1 : 0)} gauges, {Number(root, "RecordedFrameCount"):N0} frames",
        DiagnosticsReportKind.Startup => Field(root, "TimeToStableMilliseconds") is { ValueKind: JsonValueKind.Number } stable ? $"stable after {stable.GetDouble():N0} ms" : "never settled",
        DiagnosticsReportKind.SavedRun => $"{Text(root, "Configuration")} {Text(root, "Sampling")}, {Number(root, "Runs")} run(s), map {Number(root, "MapSize")}, commit {Text(root, "GitCommit")}",
        DiagnosticsReportKind.AbComparison => $"{Text(root, "Configuration")} {Text(root, "Sampling")}, {(Field(root, "Baseline") is { } baseline ? Text(baseline, "GitCommit") : "?")} vs {Text(root, "CurrentCommit")}",
        _ => string.Empty,
    };

    /// <summary>A report file's bytes, less the UTF-8 byte order mark PowerShell writes the skill's files with, which the JSON parser refuses.</summary>
    internal static ReadOnlyMemory<byte> ReadJson(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        return bytes.AsSpan().StartsWith(Utf8ByteOrderMark) ? bytes.AsMemory(Utf8ByteOrderMark.Length) : bytes;
    }

    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>The property name holds, matched in either case convention.</summary>
    internal static JsonElement? Field(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    internal static long? Number(JsonElement element, string name) =>
        Field(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : null;

    private static double Double(JsonElement element, string name) =>
        Field(element, name) is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : 0;

    private static string Text(JsonElement element, string name) =>
        Field(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? string.Empty : "?";
}

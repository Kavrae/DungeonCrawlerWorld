using System.Globalization;
using System.Text;

namespace Engine.Diagnostics;

/// <summary>A text log written a line at a time into one file per day, named for its date, and another file for the day each time one reaches its size limit.</summary>
/// <remarks>
/// basePath names the log ("Log/player-activity.log"): a day's first file is "player-activity-2026-11-05.log", the next
/// ones "player-activity-2026-11-05_001.log", "_002" and on. A line is never split: one that would take a file past its
/// limit starts the next file, and a line bigger than the limit is a file of its own. Opening continues the day's latest
/// file while it has room. The day is the local date at each write, so a session running past midnight moves to the next
/// day's file. Every line is flushed as it is written. One writer: not safe to share between threads. Nothing here
/// deletes old files: DeleteExpiredFiles does, at startup (Engine.Persistence.StartupCleanup).
/// </remarks>
public sealed class RollingLogFile : IDisposable
{
    /// <summary>The size limit a file is kept under: 10 MB, the default of log4net's and logback's rolling file appenders.</summary>
    public const long DefaultMaximumFileBytes = 10L * 1024 * 1024;

    private static readonly UTF8Encoding Utf8WithoutMark = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly System.Text.RegularExpressions.Regex DatedFileName = new(@"^(?<stem>.+)-(?<date>\d{4}-\d{2}-\d{2})(_\d{3,})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly int NewLineByteCount = Utf8WithoutMark.GetByteCount(Environment.NewLine);

    private readonly string _directory;
    private readonly string _stem;
    private readonly string _extension;
    private readonly long _maximumFileBytes;
    private readonly TimeProvider _time;
    private StreamWriter _writer;
    private DateOnly _day;
    private int _sequence;

    /// <param name="basePath">The log's name and folder; the folder is created if it doesn't exist.</param>
    /// <param name="maximumFileBytes">The size a file is kept under, a line at a time.</param>
    /// <param name="time">Where the date comes from; the system clock when null.</param>
    public RollingLogFile(string basePath, long maximumFileBytes = DefaultMaximumFileBytes, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);
        var fullPath = Path.GetFullPath(basePath);
        _directory = Path.GetDirectoryName(fullPath)!;
        _stem = Path.GetFileNameWithoutExtension(fullPath);
        _extension = Path.GetExtension(fullPath);
        _maximumFileBytes = maximumFileBytes;
        _time = time ?? TimeProvider.System;
        Directory.CreateDirectory(_directory);

        _day = Today();
        _sequence = LatestSequenceOn(_day);
        CurrentPath = PathOf(_day, _sequence);
        _writer = Open(CurrentPath);
        MoveToFileWithRoomFor(1);
    }

    /// <summary>The file lines are written to now.</summary>
    public string CurrentPath { get; private set; }

    /// <summary>Writes line and a line break, first moving to the next file when the day has changed or line won't fit.</summary>
    public void WriteLine(string line)
    {
        var lineBytes = Utf8WithoutMark.GetByteCount(line) + NewLineByteCount;
        var today = Today();
        if (today != _day)
        {
            _day = today;
            _sequence = LatestSequenceOn(today);
            SwitchTo(_sequence);
        }

        MoveToFileWithRoomFor(lineBytes);
        _writer.WriteLine(line);
    }

    public void Dispose() => _writer.Dispose();

    /// <summary>Deletes target's dated files from before its last RetentionDays days, today among them: every file beside its base path named its stem, "-yyyy-MM-dd", an optional "_NNN", and its extension.</summary>
    /// <param name="time">Where today's date comes from; the system clock when null.</param>
    /// <param name="problems">Gets one line per file that couldn't be deleted, which is left as it is.</param>
    /// <returns>The files deleted.</returns>
    public static IReadOnlyList<string> DeleteExpiredFiles(RollingLogTarget target, List<string> problems, TimeProvider? time = null)
    {
        var retentionDays = target.RetentionDays;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retentionDays);
        var fullPath = Path.GetFullPath(target.BasePath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var extension = Path.GetExtension(fullPath);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var oldestKept = DateOnly.FromDateTime((time ?? TimeProvider.System).GetLocalNow().DateTime).AddDays(1 - retentionDays);
        var deleted = new List<string>();
        foreach (var path in Directory.EnumerateFiles(directory, $"{stem}-*{extension}"))
        {
            if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)
                || DatedFileName.Match(Path.GetFileNameWithoutExtension(path)) is not { Success: true } match
                || match.Groups["stem"].Value != stem
                || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || day >= oldestKept)
            {
                continue;
            }

            try
            {
                File.Delete(path);
                deleted.Add(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{path} couldn't be deleted: {exception.Message}");
            }
        }

        return deleted;
    }

    private void MoveToFileWithRoomFor(int lineBytes)
    {
        while (_writer.BaseStream.Length > 0 && _writer.BaseStream.Length + lineBytes > _maximumFileBytes)
        {
            _sequence++;
            SwitchTo(_sequence);
        }
    }

    private void SwitchTo(int sequence)
    {
        _writer.Dispose();
        CurrentPath = PathOf(_day, sequence);
        _writer = Open(CurrentPath);
    }

    private static StreamWriter Open(string path) =>
        new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), Utf8WithoutMark) { AutoFlush = true };

    private DateOnly Today() => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    private string DayStem(DateOnly day) => $"{_stem}-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    private string PathOf(DateOnly day, int sequence) =>
        Path.Combine(_directory, sequence == 0
            ? $"{DayStem(day)}{_extension}"
            : $"{DayStem(day)}_{sequence.ToString("000", CultureInfo.InvariantCulture)}{_extension}");

    /// <summary>The highest sequence among day's files already in the folder; 0 when there are none.</summary>
    private int LatestSequenceOn(DateOnly day)
    {
        var prefix = $"{DayStem(day)}_";
        var latest = 0;
        foreach (var path in Directory.EnumerateFiles(_directory, $"{prefix}*{_extension}"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && sequence > latest)
            {
                latest = sequence;
            }
        }

        return latest;
    }
}

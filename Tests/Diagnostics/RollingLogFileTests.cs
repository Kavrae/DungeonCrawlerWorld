using Engine.Diagnostics;

namespace Tests.Diagnostics;

/// <summary>A rolling log: a file per day named for its date, another for the day past the size limit, never a line split.</summary>
[TestClass]
public sealed class RollingLogFileTests
{
    private string _directory = null!;

    [TestInitialize]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory().FullName;

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    private string BasePath => Path.Combine(_directory, "activity.log");

    private string DayFile(string name) => Path.Combine(_directory, name);

    private static string LineOf(int length) => new('x', length);

    private static int LineBytes(string line) => line.Length + Environment.NewLine.Length;

    [TestMethod]
    public void Lines_GoToAFileNamedForTheDay()
    {
        var time = new SettableTime(new DateTime(2026, 11, 5, 9, 0, 0));
        using (var log = new RollingLogFile(BasePath, time: time))
        {
            log.WriteLine("first");
            log.WriteLine("second");
            Assert.AreEqual(DayFile("activity-2026-11-05.log"), log.CurrentPath);
        }

        CollectionAssert.AreEqual(new[] { "first", "second" }, File.ReadAllLines(DayFile("activity-2026-11-05.log")));
        Assert.IsFalse(File.Exists(BasePath));
    }

    [TestMethod]
    public void ANewDay_StartsANewFile()
    {
        var time = new SettableTime(new DateTime(2026, 11, 5, 23, 59, 0));
        using (var log = new RollingLogFile(BasePath, time: time))
        {
            log.WriteLine("late");
            time.Now = new DateTime(2026, 11, 6, 0, 1, 0);
            log.WriteLine("early");
        }

        CollectionAssert.AreEqual(new[] { "late" }, File.ReadAllLines(DayFile("activity-2026-11-05.log")));
        CollectionAssert.AreEqual(new[] { "early" }, File.ReadAllLines(DayFile("activity-2026-11-06.log")));
    }

    [TestMethod]
    public void ALineThatWontFit_StartsTheDaysNextFile_AndIsNeverSplit()
    {
        var time = new SettableTime(new DateTime(2026, 11, 5, 9, 0, 0));
        var line = LineOf(40);
        using (var log = new RollingLogFile(BasePath, maximumFileBytes: LineBytes(line) * 2, time: time))
        {
            log.WriteLine(line);
            log.WriteLine(line);
            log.WriteLine(line);
            log.WriteLine(LineOf(500));
            log.WriteLine(line);
        }

        Assert.HasCount(2, File.ReadAllLines(DayFile("activity-2026-11-05.log")));
        CollectionAssert.AreEqual(new[] { line }, File.ReadAllLines(DayFile("activity-2026-11-05_001.log")));
        CollectionAssert.AreEqual(new[] { LineOf(500) }, File.ReadAllLines(DayFile("activity-2026-11-05_002.log")));
        CollectionAssert.AreEqual(new[] { line }, File.ReadAllLines(DayFile("activity-2026-11-05_003.log")));
    }

    [TestMethod]
    public void Opening_ContinuesTheDaysLatestFile_OrTheNextOneWhenItIsFull()
    {
        var time = new SettableTime(new DateTime(2026, 11, 5, 9, 0, 0));
        var line = LineOf(40);
        var limit = LineBytes(line) * 2;
        File.WriteAllText(DayFile("activity-2026-11-05.log"), line + Environment.NewLine + line + Environment.NewLine);
        File.WriteAllText(DayFile("activity-2026-11-05_001.log"), line + Environment.NewLine);

        using (var log = new RollingLogFile(BasePath, maximumFileBytes: limit, time: time))
        {
            Assert.AreEqual(DayFile("activity-2026-11-05_001.log"), log.CurrentPath);
            log.WriteLine(line);
        }

        Assert.HasCount(2, File.ReadAllLines(DayFile("activity-2026-11-05_001.log")));

        using (var log = new RollingLogFile(BasePath, maximumFileBytes: limit, time: time))
        {
            Assert.AreEqual(DayFile("activity-2026-11-05_002.log"), log.CurrentPath);
        }
    }

    [TestMethod]
    public void StartupCleanup_DeletesALogsFilesFromBeforeItsRetention_AndNothingElse()
    {
        var time = new SettableTime(new DateTime(2026, 11, 5, 9, 0, 0));
        string[] expired = ["activity-2026-10-05.log", "activity-2026-10-05_001.log"];
        string[] kept = ["activity-2026-10-06.log", "activity-2026-11-05.log", "activity.log", "activity-notes.log", "activity-2026-10-01.txt", "other-2026-10-01.log", "activity-1234-2026-09-30.log", "act-2026-09-30.log"];
        foreach (var name in expired.Concat(kept))
        {
            File.WriteAllText(DayFile(name), "line");
        }

        var result = Engine.Persistence.StartupCleanup.Run([new RollingLogTarget(BasePath)], save: null, time);

        CollectionAssert.AreEquivalent(expired.Select(DayFile).ToArray(), result.DeletedLogFiles.ToArray());
        CollectionAssert.AreEquivalent(kept.Select(DayFile).ToArray(), Directory.GetFiles(_directory));
        CollectionAssert.AreEqual(new[] { "[Startup] Deleted 2 expired log files" }, result.ReportLines().ToArray());
    }

    [TestMethod]
    public void StartupCleanup_KeepsTheDaysItsTargetNames()
    {
        var time = new SettableTime(new DateTime(2026, 11, 5, 9, 0, 0));
        File.WriteAllText(DayFile("activity-2026-10-29.log"), "line");
        File.WriteAllText(DayFile("activity-2026-10-30.log"), "line");

        var result = Engine.Persistence.StartupCleanup.Run([new RollingLogTarget(BasePath, RetentionDays: 7)], save: null, time);

        CollectionAssert.AreEqual(new[] { DayFile("activity-2026-10-29.log") }, result.DeletedLogFiles.ToArray());
    }

    [TestMethod]
    public void StartupCleanup_WithNothingToDo_ReportsNothing()
    {
        var result = Engine.Persistence.StartupCleanup.Run([new RollingLogTarget(Path.Combine(_directory, "missing", "activity.log"))], save: null);

        Assert.IsFalse(result.ReportLines().Any());
    }

    private sealed class SettableTime(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
    }
}

namespace Engine.Diagnostics;

/// <summary>Where a RollingLogFile writes and how many days of its dated files StartupCleanup keeps, today among them.</summary>
/// <remarks>The one value both read, so the files a log writes are always the files its cleanup deletes.</remarks>
public sealed record RollingLogTarget(string BasePath, int RetentionDays = RollingLogTarget.DefaultRetentionDays)
{
    /// <summary>31 days, Serilog's default count of retained files.</summary>
    public const int DefaultRetentionDays = 31;
}

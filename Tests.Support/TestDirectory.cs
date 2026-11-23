namespace TestSupport;

/// <summary>Cleans up a test's temporary folders.</summary>
public static class TestDirectory
{
    /// <summary>Deletes path and everything in it, leaving what can't be deleted: a mod DLL a test loaded stays locked for the rest of the run.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
        }
    }
}

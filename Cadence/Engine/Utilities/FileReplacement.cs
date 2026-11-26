namespace Engine.Utilities;

/// <summary>Moves a fully written temporary file over the file it replaces.</summary>
/// <remarks>
/// Windows refuses to replace a file while any other handle is open on it, and a virus scanner or the search indexer opens one
/// it has just seen written, so the move is tried again for a short while before the refusal is passed on.
/// </remarks>
public static class FileReplacement
{
    private const int Attempts = 10;

    private const int RetryDelayMilliseconds = 50;

    /// <summary>Moves temporaryPath over path, replacing it.</summary>
    public static void Replace(string temporaryPath, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, path, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException && attempt < Attempts)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestSupport;

/// <summary>The one way a test waits for something that raises no event it can await.</summary>
public static class TestWait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);

    /// <summary>Completes once condition is true, holding no thread between checks; fails the test, naming description, if it never is.</summary>
    /// <param name="description">What is being waited for, as it reads after "Timed out waiting until".</param>
    public static Task UntilAsync(Func<bool> condition, string description, TimeSpan? timeout = null) =>
        UntilAsync(condition, () => description, timeout);

    /// <summary>Completes once condition is true, holding no thread between checks; fails the test, naming what describe says at that moment, if it never is.</summary>
    /// <param name="describe">What is being waited for and how things stand, read only on a timeout.</param>
    public static async Task UntilAsync(Func<bool> condition, Func<string> describe, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting until {describe()}.");
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
    }
}

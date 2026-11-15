namespace Workshop.Tests;

[TestClass]
public static class AssemblySetup
{
    /// <summary>How many thread-pool worker threads are ready from the start of the run.</summary>
    /// <remarks>
    /// Tests run in parallel on pool threads, and many wait by sleeping, so at the pool's default minimum (one per core)
    /// they can hold every ready thread; what a test waits for -- a process's exit and output, a socket connecting --
    /// needs a pool thread too, and the pool adds them slowly enough for such a wait to run out.
    /// </remarks>
    private const int MinimumWorkerThreads = 64;

    [AssemblyInitialize]
    public static void SetUp(TestContext context)
    {
        global::Game.GameFrameRate.Apply();

        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        ThreadPool.SetMinThreads(Math.Max(workerThreads, MinimumWorkerThreads), completionPortThreads);
    }
}

namespace Engine.Math;

/// <summary>A session's independent random streams, one per purpose, each seeded from the session seed and its key.</summary>
/// <remarks>
/// A roll in one stream never shifts another, so what one feature draws can't change what any other feature gets. A
/// stream is created the first time its key is asked for and kept for the session; its sequence depends only on the
/// session seed and the key, never on which streams were asked for first. Streams are main-thread only, like the
/// systems that draw from them.
/// </remarks>
/// <param name="sessionSeed">The seed every stream of the session is derived from.</param>
public sealed class RandomStreams(int sessionSeed)
{
    private readonly Dictionary<RandomStreamKey, MathUtility> _streams = [];

    public int SessionSeed { get; } = sessionSeed;

    /// <summary>The stream named key, created on first ask.</summary>
    public MathUtility Get(RandomStreamKey key)
    {
        if (!_streams.TryGetValue(key, out var stream))
        {
            stream = new MathUtility(new SeededRandom(SeedFor(key)));
            _streams.Add(key, stream);
        }

        return stream;
    }

    /// <summary>The seed the stream named key starts from, for something that keeps a sequence of its own.</summary>
    public ulong SeedFor(RandomStreamKey key) => SeedFor(SessionSeed, key);

    /// <summary>The seed the stream named key starts from in a session seeded with sessionSeed.</summary>
    public static ulong SeedFor(int sessionSeed, RandomStreamKey key) =>
        SeedMix.Mix(SeedMix.Mix(SeedMix.Of(sessionSeed), SeedMix.Of(key.Owner)), SeedMix.Of(key.Name));
}

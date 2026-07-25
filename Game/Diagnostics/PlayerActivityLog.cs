using Engine.Events;
using Game.World;

namespace Game.Diagnostics;

/// <summary>
/// Narrow, purpose-built debug tool: logs the player's Burning damage and movement to a file
/// so Burning's tick-by-tick behavior is observable without a debugger. Deliberately not a
/// general logging facility -- no levels, no other event types, no other entities. See
/// TODO.md's "Debug/event logging with levels" item for what a real version of this would need.
/// </summary>
public sealed class PlayerActivityLog : IDisposable
{
    private readonly Game.World.World _world;
    private readonly StreamWriter _writer;

    private int _currentFrameCount;
    private DateTime _currentTimestamp;

    public PlayerActivityLog(Game.World.World world, EventBus eventBus, string logFilePath)
    {
        _world = world;

        var logDirectory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        _writer = new StreamWriter(logFilePath, append: true) { AutoFlush = true };

        eventBus.Subscribe<EntityMoved>(OnEntityMoved);
        eventBus.Subscribe<EntityDamaged>(OnEntityDamaged);
    }

    /// <summary>
    /// Called once per simulation frame, immediately before EcsContext.Update -- caches the
    /// frame's identity so the event handlers below (fired synchronously during that Update
    /// call) log against the right frame/time.
    /// </summary>
    public void BeginFrame(int frameCount, DateTime timestamp)
    {
        _currentFrameCount = frameCount;
        _currentTimestamp = timestamp;
    }

    private void OnEntityMoved(EntityMoved moved)
    {
        if (moved.EntityId != _world.PlayerEntityId)
        {
            return;
        }

        Write($"MOVE from={moved.OldPosition} to={moved.NewPosition}");
    }

    private void OnEntityDamaged(EntityDamaged damaged)
    {
        if (damaged.EntityId != _world.PlayerEntityId)
        {
            return;
        }

        Write($"DAMAGE amount={damaged.Amount} type={damaged.DamageType} source={damaged.Source} currentHealth={damaged.CurrentHealth} maximumHealth={damaged.MaximumHealth}");
    }

    private void Write(string message) =>
        _writer.WriteLine($"[{_currentTimestamp:O}] [Frame {_currentFrameCount}] {message}");

    public void Dispose() => _writer.Dispose();
}

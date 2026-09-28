using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Spawning;
using Game.Modules.Core.Components;
using Game.World;
using Game.Blueprints;

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
    private readonly EntityNaming _naming;
    private readonly StreamWriter _writer;

    private int _currentFrameCount;
    private DateTime _currentTimestamp;

    public PlayerActivityLog(Game.World.World world, ComponentManager componentManager, EventBus eventBus, string logFilePath, BlueprintRegistry creatures)
    {
        _world = world;
        _naming = EntityNaming.For(componentManager, creatures);

        var logDirectory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        _writer = new StreamWriter(logFilePath, append: true) { AutoFlush = true };

        eventBus.Subscribe<EntityMovedEvent>(OnEntityMoved);
        eventBus.Subscribe<EntityDamagedEvent>(OnEntityDamaged);
        eventBus.Subscribe<EntityHealedEvent>(OnEntityHealed);
        eventBus.Subscribe<StatusEffectImmunityBlockedEvent>(OnStatusEffectImmunityBlocked);
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

    private void OnEntityMoved(EntityMovedEvent moved)
    {
        if (moved.EntityId != _world.PlayerEntityId)
        {
            return;
        }

        Write($"MOVE from={moved.OldPosition} to={moved.NewPosition}");
    }

    /// <summary>
    /// Logs both directions the player can appear in a damage event: damage taken (EntityId is
    /// the player) and damage dealt to an NPC (Source is the player -- HealthDamage.Apply
    /// already publishes this case too, see its own playerInvolved check; only this log's own
    /// EntityId-only filter was dropping it). Target is logged explicitly now that EntityId
    /// isn't always the player, unlike before.
    /// </summary>
    private void OnEntityDamaged(EntityDamagedEvent damaged)
    {
        var playerIsTarget = damaged.EntityId == _world.PlayerEntityId;
        var playerIsSource = damaged.Source.IsEntity(_world.PlayerEntityKey);

        if (!playerIsTarget && !playerIsSource)
        {
            return;
        }

        Write($"DAMAGE amount={damaged.Amount} type={damaged.DamageType} source={DescribeSource(damaged.Source)} target={DescribeEntity(damaged.EntityId)} currentHealth={damaged.CurrentHealth} maximumHealth={damaged.MaximumHealth}");
    }

    /// <summary>Mirrors OnEntityDamaged exactly -- both directions the player can appear in a heal event (healed, or the source that healed someone else) are logged, same reasoning as the DAMAGE line above.</summary>
    private void OnEntityHealed(EntityHealedEvent healed)
    {
        var playerIsTarget = healed.EntityId == _world.PlayerEntityId;
        var playerIsSource = healed.SourceEntityId == _world.PlayerEntityId;

        if (!playerIsTarget && !playerIsSource)
        {
            return;
        }

        Write($"HEAL amount={healed.Amount:0.##} type={healed.HealType} source={(healed.SourceEntityId is { } healerEntityId ? DescribeEntity(healerEntityId) : "Admin")} target={DescribeEntity(healed.EntityId)} currentHealth={healed.CurrentHealth:0.##} maximumHealth={healed.MaximumHealth:0.##}");
    }

    /// <summary>Mirrors OnEntityDamaged/OnEntityHealed exactly -- StatusEffectImmunity.IsImmune already gates the publish itself on player-involvement, so this handler's own check is defense in depth, not the only guard.</summary>
    private void OnStatusEffectImmunityBlocked(StatusEffectImmunityBlockedEvent blocked)
    {
        var playerIsTarget = blocked.EntityId == _world.PlayerEntityId;
        var playerIsSource = blocked.Source.IsEntity(_world.PlayerEntityKey);

        if (!playerIsTarget && !playerIsSource)
        {
            return;
        }

        Write($"BLOCKED type={blocked.EffectType} source={DescribeSource(blocked.Source)} target={DescribeEntity(blocked.EntityId)} (immune)");
    }

    /// <summary>entityId alone, or "Name (#entityId)" when anything names it (its own DisplayTextComponent, or its race -- see EntityNaming) -- shared by both the source and target sides of a DAMAGE line.</summary>
    private string DescribeEntity(int entityId) =>
        _naming.TryGetName(entityId, out var name)
            ? $"{name} (#{entityId})"
            : entityId.ToString();

    /// <summary>An entity source by the name and crawler number it had when the source was created (its runtime id may already belong to someone else); anything else by ActionSource.ToString().</summary>
    private static string DescribeSource(ActionSource source) =>
        source.Kind == ActionSourceKind.Entity
            ? source.Identity.DisplayName
            : source.ToString();

    private void Write(string message) =>
        _writer.WriteLine($"[{_currentTimestamp:O}] [Frame {_currentFrameCount}] {message}");

    public void Dispose() => _writer.Dispose();
}

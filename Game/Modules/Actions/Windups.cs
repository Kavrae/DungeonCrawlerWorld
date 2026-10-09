using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;

namespace Game.Modules.Actions;

/// <summary>Resolves an item's windup when it ends: what activating the stack means is Inventory's.</summary>
/// <remarks>Registered with WindupResolvers by the feature that owns items, so DelayedActionSystem resolves an item windup without knowing what an item is.</remarks>
public interface IItemWindupResolver
{
    /// <summary>Carries out entityId's item activation whose windup just ended. The stack may have left the entity meanwhile, in which case nothing happens.</summary>
    void Resolve(int entityId, in PendingWindupComponent windup, long now);
}

/// <summary>The resolvers DelayedActionSystem hands windups to that Actions doesn't resolve itself.</summary>
/// <remarks>Filled in RegisterBehavior; read only when a windup ends.</remarks>
public sealed class WindupResolvers
{
    /// <summary>Resolves item windups; null when the build has no feature owning items.</summary>
    public IItemWindupResolver? ItemResolver { get; private set; }

    /// <summary>Registers what resolves item windups. One resolver.</summary>
    public void RegisterItemResolver(IItemWindupResolver resolver)
    {
        if (ItemResolver is not null)
        {
            throw new InvalidOperationException("Item windups already have a resolver.");
        }

        ItemResolver = resolver;
    }
}

/// <summary>Starts a windup: the one place a Delayed activation, of an action or an item, sets the lock and records what it resolves into.</summary>
public static class Windups
{
    /// <summary>Sets entityId's action lock for lockFrames and records windup, ending when the lock does.</summary>
    /// <remarks>
    /// The windup's end is the lock's own deadline, read straight back off the component this just
    /// locked, so the pending windup and the lock can never disagree about when it resolves. The
    /// caller has checked the lock is clear.
    /// </remarks>
    /// <param name="windup">What the windup resolves into; its ReadyAtFrame is overwritten.</param>
    public static void Begin(
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<PendingWindupComponent> pendingWindups,
        int entityId,
        long now,
        ushort lockFrames,
        PendingWindupComponent windup)
    {
        ActionLockGate.Lock(actionLocks, entityId, now, lockFrames);

        // The fallback is unreachable in practice -- an entity with no ActionLockComponent reads as
        // blocked, so no caller gets here -- but it keeps the deadline honest if that ever changes.
        windup.ReadyAtFrame = actionLocks.TryGetReadonly(entityId, out var actionLock)
            ? actionLock.UnlockedAtFrame
            : FrameDeadline.After(now, lockFrames);

        pendingWindups.Merge(entityId, windup);
    }
}

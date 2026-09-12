using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Activators;

namespace Game.Modules.Actions.Systems;

/// <summary>Removes each PotionCooldownComponent on the frame it expires -- it carries no other cleanup.</summary>
/// <remarks>
/// Driven by a timer wheel (PackedTimerWheel): only cooldowns actually ending are touched, on their
/// exact frame at every processing tier (PLAN-timer-wheel.md). This system was once one of the
/// largest simulation costs while scanning every live cooldown (9,110 of them) every frame to
/// decrement integers; tiering cut that, and the wheel removes the scan altogether.
/// PotionCooldownEffects.Reset merging the component is all that schedules it.
/// </remarks>
public sealed class PotionCooldownSystem(PackedComponentPool<PotionCooldownComponent> cooldowns) : ISystem
{
    /// <summary>The one firing is the expiry -- always remove.</summary>
    private static readonly TimerFired<PotionCooldownComponent> RemoveOnExpiry = static (_, _, _) => true;

    private readonly PackedTimerWheel<PotionCooldownComponent> _wheel = new(cooldowns);

    /// <summary>Every frame; the wheel only touches cooldowns actually ending.</summary>
    public byte StripeCount => 1;

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, RemoveOnExpiry);
}

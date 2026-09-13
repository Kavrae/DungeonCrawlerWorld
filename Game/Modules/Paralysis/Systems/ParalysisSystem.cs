using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Paralysis.Components;

namespace Game.Modules.Paralysis.Systems;

/// <summary>
/// Removes Paralysis's timer on the frame it expires. Does not touch ActionLockComponent:
/// ParalysisEffects.Apply already locked it for the same DurationFrames at grant time, so both end
/// together without this system re-asserting anything. Also does not touch SimpleHealthComponent
/// -- Paralysis has no damage component at all, unlike Burning/Poison, proving a status effect can
/// apply to entities without hit points.
/// </summary>
/// <remarks>
/// Driven by a timer wheel (PackedTimerWheel): each paralysis fires once, on its ExpiresAtFrame,
/// at every processing tier (PLAN-timer-wheel.md).
/// </remarks>
public sealed class ParalysisSystem(PackedComponentPool<ParalysisTimerComponent> timers) : ISystem
{
    /// <summary>The one firing is the expiry -- always remove. There's no repeating action to re-arm for, unlike Burning/Poison.</summary>
    private static readonly TimerFired<ParalysisTimerComponent> RemoveOnExpiry = static (_, _, _) => true;

    private readonly PackedTimerWheel<ParalysisTimerComponent> _wheel = new(timers);

    /// <summary>Every frame; the wheel only touches paralyses actually expiring.</summary>
    public byte StripeCount => 1;

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, RemoveOnExpiry);
}

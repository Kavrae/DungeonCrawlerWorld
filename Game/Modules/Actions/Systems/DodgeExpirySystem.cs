using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions.Systems;

/// <summary>Removes DodgingComponent on the frame its window closes.</summary>
/// <remarks>
/// Driven by a timer wheel (PackedTimerWheel): each dodge fires exactly once, on its
/// ExpiresAtFrame, and the callback removes it. Nothing visits a dodging entity before then, and
/// nothing had to tell the wheel about the dodge -- DodgeActivation just merges the component
/// (see PLAN-timer-wheel.md). Exact to the frame at every processing tier, which is what a
/// timing-precision mechanic like Dodge needs; it used to be the one timed system kept
/// deliberately untiered for exactly that reason, visiting every dodging entity every frame.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DodgeExpirySystem(PackedComponentPool<DodgingComponent> dodgingEntities) : ISystem
{
    /// <summary>The window's one and only firing is its expiry -- always remove.</summary>
    private static readonly TimerFired<DodgingComponent> RemoveOnExpiry = static (_, _, _) => true;

    private readonly PackedTimerWheel<DodgingComponent> _expiries = new(dodgingEntities);

    /// <summary>Every frame; the wheel only touches dodges actually expiring.</summary>
    public byte StripeCount => 1;

    public void Update(EngineTime time, byte stripeIndex) => _expiries.Tick(time.FrameCount, RemoveOnExpiry);
}

using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Health.Components;

namespace Game.Modules.Health.Systems;

/// <summary>Clears a victim's damage ledger once it has gone DamageLedger.ResetAfterFrames without recorded damage.</summary>
/// <remarks>
/// Driven by a timer wheel over DamageLedgerExpiryComponent, so it touches only the ledgers whose
/// deadline arrived. Unscoped: clearing a ledger is safe whether or not the victim is simulated, and
/// a frozen victim's stale ledger is let go on time instead of on resume.
/// </remarks>
public sealed class DamageLedgerExpirySystem : ISystem
{
    private readonly PackedTimerWheel<DamageLedgerExpiryComponent> _expiries;
    private readonly TimerFired<DamageLedgerExpiryComponent> _onExpiry;

    public DamageLedgerExpirySystem(PackedComponentPool<DamageLedgerExpiryComponent> expiries, DamageLedger damageLedger)
    {
        _expiries = new PackedTimerWheel<DamageLedgerExpiryComponent>(expiries, SimulationScope.Unscoped);
        _onExpiry = damageLedger.ExpireOrRearm;
    }

    public byte StripeCount => 1;

    public void Update(EngineTime time, byte stripeIndex) => _expiries.Tick(time.FrameCount, _onExpiry);
}

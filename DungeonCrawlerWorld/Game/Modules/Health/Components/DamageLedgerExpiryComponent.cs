using Engine.ECS.Components;

namespace Game.Modules.Health.Components;

/// <summary>When the holder's damage ledger resets: DamageLedger.ResetAfterFrames after it last took recorded damage.</summary>
/// <remarks>
/// A timer (IScheduledTimer) driven by DamageLedgerExpirySystem. A hit writes only LastDamagedFrame,
/// leaving the deadline alone so the wheel isn't rescheduled per hit; when the deadline arrives the
/// system either re-arms it from LastDamagedFrame or clears the ledger.
/// </remarks>
/// <param name="lastDamagedFrame">The frame the holder last took recorded damage.</param>
/// <param name="expiresAtFrame">The frame the wheel next checks whether the ledger has gone quiet.</param>
public struct DamageLedgerExpiryComponent(uint lastDamagedFrame, uint expiresAtFrame) : IScheduledTimer
{
    private uint _timerWheelMark;

    public uint LastDamagedFrame { get; set; } = lastDamagedFrame;

    public uint ExpiresAtFrame { get; set; } = expiresAtFrame;

    uint IScheduledTimer.NextTickFrame { readonly get => ExpiresAtFrame; set => ExpiresAtFrame = value; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"LastDamagedFrame : {LastDamagedFrame}\nExpiresAtFrame : {ExpiresAtFrame}";
}

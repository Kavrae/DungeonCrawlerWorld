namespace Game.Modules.Core.Components;

/// <summary>
/// Shared "am I currently mid-action" state: any action-gating system reads LockFramesRemaining
/// to decide whether an entity may act, and sets both fields when an action is taken. Once
/// LockFramesRemaining reaches 0 the entity may act again.
///
/// LockFramesRemaining is denominated in real game frames -- setting it to N means "locked for
/// N real frames," not N of some system-internal unit -- even though ActionLockSystem only
/// actually visits a given entity once every StripeCount real frames (that's what entity
/// striping means). See ActionLockSystem's own doc comment for how it keeps that true: it
/// subtracts its own StripeCount per visit rather than 1, so the conversion lives in exactly
/// one place and costs nothing extra anywhere this field is read or set.
/// </summary>
public struct ActionLockComponent(short totalLockFrames, short lockFramesRemaining)
{
    public short TotalLockFrames { get; set; } = totalLockFrames;
    public short LockFramesRemaining { get; set; } = lockFramesRemaining;
}

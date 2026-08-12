namespace Game.Modules.Actions.Activators;

/// <summary>
/// Shared "am I still cooling down from my last potion" state, one per consumer (not per item --
/// see PotionCooldownEffects' own doc comment). Present on an entity only while FramesRemaining
/// is still counting down; removed once it reaches 0 by PotionCooldownSystem, the same "no
/// instance means the empty/inactive state" convention ActionLockComponent's sibling systems use
/// elsewhere. TotalFrames stays fixed at whatever Reset last set it to, for consumers (the
/// status-effect bar, the hotbar countdown) that need "how much of the cooldown is left" as
/// something other than raw frames.
/// </summary>
public struct PotionCooldownComponent(short totalFrames, short framesRemaining)
{
    public short TotalFrames { get; set; } = totalFrames;
    public short FramesRemaining { get; set; } = framesRemaining;

    public override readonly string ToString() => $"TotalFrames : {TotalFrames}\nFramesRemaining : {FramesRemaining}";
}

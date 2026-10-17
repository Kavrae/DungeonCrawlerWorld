namespace Game.World;

/// <summary>Published by ActionEffectResolver.Apply when an action tagged GameTags.TraitStaggering hits an entity.</summary>
/// <remarks>
/// A dodged hit never staggers: the resolver skips a dodging target before this point. Immediate, not IBufferedEvent,
/// the same as ActionActivatedEvent from the same method. ActionsModule cancels the target's windup on it, keeping the
/// lock the windup set; Presentation's PlayerCommands drops the player's buffered command. Unrelated to
/// FrameDeadline.AfterStaggered, which spreads periodic timers across frames.
/// </remarks>
public readonly record struct EntityStaggeredEvent(int EntityId, ActionSource Source);

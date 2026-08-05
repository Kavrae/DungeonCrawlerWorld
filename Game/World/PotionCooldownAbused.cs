namespace Game.World;

/// <summary>Published by ConsumableActivationSystem exactly when a potion is consumed while its consumer's PotionCooldownComponent is still counting down -- the "Drinking Problem" achievement's trigger.</summary>
public sealed record PotionCooldownAbused(int EntityId);

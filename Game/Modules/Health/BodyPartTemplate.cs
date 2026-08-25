using Game.Modules.Health.Components;

namespace Game.Modules.Health;

/// <summary>One race-authored body part definition, rolled into a real BodyPartComponent at blueprint Build time.</summary>
/// <remarks>MinimumHealth/MaximumHealth bound the starting-health roll ComplexHealthEffects.GrantBodyParts performs; MaximumHealth alone becomes the resulting part's cap.</remarks>
public readonly record struct BodyPartTemplate(string Name, BodyPartType Type, ushort MinimumHealth, ushort MaximumHealth, bool IsVital);

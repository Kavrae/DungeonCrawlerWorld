using Game.Modules.Health.Components;

namespace Game.Modules.Health;

/// <summary>One race-authored body part definition, built into a real BodyPartComponent at blueprint Build time.</summary>
/// <remarks>MaximumHealth is both the part's cap and the health it starts at -- see ComplexHealthEffects.GrantBodyParts. VerticalPosition is higher-is-higher-up-the-body, meaningful only relative to the same entity's own other parts -- see BodyPartSelection.PickTopmost/PickBottommost.</remarks>
public readonly record struct BodyPartTemplate(string Name, BodyPartType Type, byte VerticalPosition, ushort MaximumHealth, bool IsVital);

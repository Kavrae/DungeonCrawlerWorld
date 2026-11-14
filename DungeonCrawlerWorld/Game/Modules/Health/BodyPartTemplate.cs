using Game.Modules.Health.Components;

namespace Game.Modules.Health;

/// <summary>One race-authored body part definition, built into a real BodyPartComponent at blueprint Build time.</summary>
/// <remarks>BaseMaximumHealth is the part's maximum before the entity's MaximumHealth modifiers, and the health it starts at without any -- see ComplexHealthEffects.GrantBodyParts. VerticalPosition is higher-is-higher-up-the-body, meaningful only relative to the same entity's own other parts -- see BodyPartSelection.PickTopmost/PickBottommost.</remarks>
public readonly record struct BodyPartTemplate(string Name, BodyPartType Type, byte VerticalPosition, ushort BaseMaximumHealth, bool IsVital);

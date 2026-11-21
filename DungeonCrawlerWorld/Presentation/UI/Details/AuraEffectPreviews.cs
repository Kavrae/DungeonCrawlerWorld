using Game.Effects;
using Game.Modules.Auras;

namespace Presentation.UI.Details;

/// <summary>An aura's effects' amounts where its power is power, as one of its ticks applies them (Game.Views.ActivationPreviews.PreviewAura).</summary>
public delegate IReadOnlyDictionary<IEffectEntry, EffectAmountPreview> AuraEffectPreviews(AuraDefinition aura, float power);

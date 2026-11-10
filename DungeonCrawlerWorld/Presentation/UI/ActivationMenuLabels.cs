using Engine.Tags;
using Game.Modules.Actions;
using Game.Tags;

namespace Presentation.UI;

/// <summary>The one set of words a menu uses for using an action or item: what choosing the option does, the same from the hotbar and the inventory.</summary>
public static class ActivationMenuLabels
{
    /// <summary>Arms it, for a target to be picked on the map.</summary>
    public const string Arm = "Arm";

    /// <summary>Uses what is already armed -- a re-press of its hotbar slot -- when it is neither drunk nor cast (a wand, an attack, Dodge).</summary>
    public const string Activate = "Activate";

    /// <summary>Uses an armed potion -- from the hotbar it is drunk.</summary>
    public const string Drink = "Drink";

    /// <summary>Uses an armed spell or scroll.</summary>
    public const string Cast = "Cast";

    public const string TurnOn = "Turn on";

    public const string TurnOff = "Turn off";

    /// <summary>The label for using something with tags in mode: a toggle turns off, or on -- armed first in Ground mode (ToggleArming), so "Arm" until it is; anything else arms, or, already armed from this place, is used (UseVerbFor).</summary>
    public static string For(bool isToggle, bool isToggledOn, bool isArmedHere, TargetingMode mode, GameplayTagSet tags)
    {
        if (isToggle)
        {
            return isToggledOn ? TurnOff : ToggleArming.ArmsFirst(isToggledOn, mode) && !isArmedHere ? Arm : TurnOn;
        }

        return isArmedHere ? UseVerbFor(tags) : Arm;
    }

    /// <summary>What using something armed is called, from its tags (an activator's implied ones among them): a wand is activated, a potion drunk, a spell or scroll cast, anything else activated.</summary>
    public static string UseVerbFor(GameplayTagSet tags)
    {
        if (tags.Has(GameTags.ItemWand))
        {
            return Activate;
        }

        if (tags.Has(GameTags.ItemConsumablePotion))
        {
            return Drink;
        }

        return tags.Has(GameTags.ItemConsumableScroll) || tags.Has(GameTags.ActionSpell) ? Cast : Activate;
    }
}

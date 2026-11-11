using Engine.Tags;
using Game.Modules.Actions;
using Game.Tags;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>Using something armed is named by what it is: a potion is drunk, a spell or scroll cast, a wand or anything else activated.</summary>
[TestClass]
public sealed class ActivationMenuLabelsTests
{
    [TestMethod]
    public void TheArmedSlotsVerb_FollowsItsTags()
    {
        Assert.AreEqual("Drink", ActivationMenuLabels.UseVerbFor([GameTags.ItemConsumablePotion]));
        Assert.AreEqual("Cast", ActivationMenuLabels.UseVerbFor([GameTags.ItemConsumableScroll, GameTags.Spell, GameTags.Magic]));
        Assert.AreEqual("Cast", ActivationMenuLabels.UseVerbFor([GameTags.Spell, GameTags.Magic]));
        Assert.AreEqual("Activate", ActivationMenuLabels.UseVerbFor([GameTags.ItemWand, GameTags.Spell, GameTags.Magic]), "A wand stays Activate, whatever it holds.");
        Assert.AreEqual("Activate", ActivationMenuLabels.UseVerbFor(GameplayTagSet.Empty));
    }

    [TestMethod]
    public void OnlyTheArmedSlot_IsUsed_AndAToggleTurnsOnOrOff()
    {
        GameplayTagSet potion = [GameTags.ItemConsumablePotion];

        Assert.AreEqual("Arm", ActivationMenuLabels.For(isToggle: false, isToggledOn: false, isArmedHere: false, TargetingMode.Target, potion));
        Assert.AreEqual("Drink", ActivationMenuLabels.For(isToggle: false, isToggledOn: false, isArmedHere: true, TargetingMode.Target, potion));
        Assert.AreEqual("Turn off", ActivationMenuLabels.For(isToggle: true, isToggledOn: true, isArmedHere: true, TargetingMode.Target, potion));
    }

    [TestMethod]
    public void AToggleTurnedOnInGroundMode_ReadsArm_UntilItIsArmed_AndTurnsOffAtOnce()
    {
        Assert.AreEqual("Turn on", ActivationMenuLabels.For(isToggle: true, isToggledOn: false, isArmedHere: false, TargetingMode.Target, GameplayTagSet.Empty));
        Assert.AreEqual("Arm", ActivationMenuLabels.For(isToggle: true, isToggledOn: false, isArmedHere: false, TargetingMode.Ground, GameplayTagSet.Empty));
        Assert.AreEqual("Turn on", ActivationMenuLabels.For(isToggle: true, isToggledOn: false, isArmedHere: true, TargetingMode.Ground, GameplayTagSet.Empty));
        Assert.AreEqual("Turn off", ActivationMenuLabels.For(isToggle: true, isToggledOn: true, isArmedHere: false, TargetingMode.Ground, GameplayTagSet.Empty));
    }
}

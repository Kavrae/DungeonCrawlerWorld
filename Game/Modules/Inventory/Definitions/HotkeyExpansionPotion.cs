using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

public static class HotkeyExpansionPotion
{
    public static readonly Guid Id = new("a4f2e8c1-6b3d-4e97-9a1f-2d8c5b7e4a64");

    public static ItemDefinition Build() => new(
        Id, "Hotkey Expansion Potion", "HealthPotion", "k", Color.Orange,
        Tags: [Tag.Consumable, Tag.Potion, Tag.Self],
        Effects: [new ActionEffect([new HotkeyExpansionGrant(5)])],
        Description: "Do you ever feel like you just don't have enough menus blocking your view? Well lets fix that! Lets add 5 more hotkey slots right in the middle of your screen. Note : Your hotkey list caps out at 20 slots.",
        Summary: "Adds 5 new hotkey slots.",
        GoldValue: 12,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Self, Range: 0, AreaSize: 0),
            new ActionTiming(ActionTimingCategory.Immediate, GameTiming.FramesForSeconds(5f), CooldownFrames: null)));
}

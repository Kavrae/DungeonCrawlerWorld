using Engine.Content;
using Game.Modules.Inventory;

namespace Game.Modules.Lootboxes;

/// <summary>The codecs this module owns: RandomSingleStackContents, and LootboxReward -- what an achievement owes the player, saved through it by a snapshot; registered by LootboxModule.</summary>
internal static class LootboxContentCodecs
{
    public static void Register(ContentTypeDeclarations contentTypes)
    {
        contentTypes.RegisterPolymorphic<IItemContents, RandomSingleStackContents>("RandomSingleStack", new RandomSingleStackContentsCodec());
        contentTypes.RegisterValue(new LootboxRewardCodec());
    }

    private sealed class RandomSingleStackContentsCodec : ContentCodec<RandomSingleStackContents>
    {
        public override ContentSchema Schema { get; } = new(nameof(RandomSingleStackContents), []);

        public override void WriteProperties(ContentWriter writer, RandomSingleStackContents value)
        {
        }

        public override RandomSingleStackContents Read(ContentObject element) => RandomSingleStackContents.Instance;
    }

    private sealed class LootboxRewardCodec : ContentCodec<LootboxReward>
    {
        public override ContentSchema Schema { get; } = new(nameof(LootboxReward),
        [
            ContentField.Guid("t", "Loot box type", isRequired: true),
            ContentField.Enum("r", "Rarity", default(LootboxRarity)),
            ContentField.Polymorphic<IItemContents>("ct", "Contents"),
        ]);

        public override void WriteProperties(ContentWriter writer, LootboxReward value)
        {
            writer.Guid("t", value.TypeId);
            writer.Enum("r", value.Rarity);
            writer.Polymorphic("ct", value.Contents);
        }

        public override LootboxReward Read(ContentObject element) =>
            new(element.Guid("t"), element.Enum<LootboxRarity>("r"), element.Polymorphic<IItemContents>("ct"));
    }
}

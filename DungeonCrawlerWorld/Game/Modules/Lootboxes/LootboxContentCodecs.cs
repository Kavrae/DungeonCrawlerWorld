using Engine.Content;
using Game.Modules.Inventory;

namespace Game.Modules.Lootboxes;

/// <summary>The codec for RandomSingleStackContents, which has no fields and reads back as its one shared instance; registered by LootboxModule.</summary>
internal static class LootboxContentCodecs
{
    public static void Register(ContentTypeRegistration registration) =>
        registration.RegisterPolymorphic<IItemContents, RandomSingleStackContents>("RandomSingleStack", new RandomSingleStackContentsCodec());

    private sealed class RandomSingleStackContentsCodec : ContentCodec<RandomSingleStackContents>
    {
        public override ContentSchema Schema { get; } = new(nameof(RandomSingleStackContents), []);

        public override void WriteProperties(ContentWriter writer, RandomSingleStackContents value)
        {
        }

        public override RandomSingleStackContents Read(ContentObject element) => RandomSingleStackContents.Instance;
    }
}

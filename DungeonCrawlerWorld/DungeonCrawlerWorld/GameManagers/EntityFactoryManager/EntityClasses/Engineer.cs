using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Engineer : IModifierFactoryTemplate
    {
        private static string ClassName => "Engineer";

        private static string Description => "TODO default engineer description";

        //TODO swap from color to class glpyhs(s) added as a superscript to the main glyph
        public override void Apply(Guid entityId) 
        {
            if (ComponentRepo.DisplayGlyphComponents.TryGetValue(entityId, out var displayGlyphComponent))
            {
                displayGlyphComponent.GlyphColor = Color.OrangeRed;
                ComponentRepo.DisplayGlyphComponents[entityId] = displayGlyphComponent;
            }
        }

        public override void Remove(Guid entityId)
        {
            //TODO remove value from superscript glyph
        }
    }
}

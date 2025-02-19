using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Engineer : IModifierFactoryTemplate
    {
        private static string Name => "Engineer";

        private static string Description => "TODO default engineer description";

        public override void Apply(Guid entityId) 
        {
            _ = new ClassGlyphComponent(entityId, "e", Color.OrangeRed, new Point(0,0));

            if( !ComponentRepo.DisplayTextComponents.TryGetValue(entityId, out DisplayTextComponent displayTextComponent))
            {
                displayTextComponent = new DisplayTextComponent(entityId);
            }
            displayTextComponent.ClassName = Name;
            displayTextComponent.ClassDescription = Description;
            ComponentRepo.DisplayTextComponents[entityId] = displayTextComponent;
        }

        public override void Remove(Guid entityId)
        {
            ComponentRepo.ClassGlyphComponents.Remove(entityId);
        }
    }
}

using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public class Engineer : ClassComponent
    {
        public Engineer(Guid entityId) : base(entityId)
        {
            Name = "Engineer";
            Description = "TODO default engineer description";

            _ = new ClassGlyphComponent(entityId, "e", Color.OrangeRed, new Point(0, 0));

            if (!ComponentRepo.DisplayTextComponents.TryGetValue(entityId, out DisplayTextComponent displayTextComponent))
            {
                displayTextComponent = new DisplayTextComponent(entityId);
            }
            displayTextComponent.ClassName = Name;
            displayTextComponent.ClassDescription = Description;
            ComponentRepo.DisplayTextComponents[entityId] = displayTextComponent;
        }
    }
}

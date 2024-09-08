using System;
using System.Linq;
using DungeonCrawlerWorld.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DungeonCrawlerWorld.GameComponents.DisplayComponent
{
    public class MapTile
    {
        public Guid TerrainId;

        public Point Size;
        public Point DisplayPosition;
        public Point MapPosition;
        public Rectangle DisplayRectangle;

        public string DisplayCharacter;
        public Point displayCharacterOffset;

        public Color ForegroundColor;
        public Color BackgroundColor;
        public Color HighlightColor;

        public bool IsSelected;

        public MapTile(MapNode mapNode, Point displayPosition, Point size)
        {
            MapPosition = mapNode.Position;
            TerrainId = mapNode.Terrain.EntityData.Id;

            Size = size;
            DisplayPosition = displayPosition;
            DisplayRectangle = new Rectangle(displayPosition.X, displayPosition.Y, size.X, size.Y);

            var topLevelEntityData = DetermineTopLevelEntityData(mapNode);
            DisplayCharacter = topLevelEntityData.DisplayString;
            BackgroundColor = mapNode.Terrain.EntityData.BackgroundColor ?? Color.Black;
            ForegroundColor = topLevelEntityData.ForegroundColor ?? Color.White;
            HighlightColor = Color.Gold;
            displayCharacterOffset = topLevelEntityData.DisplayStringOffset ?? new Point(0,0);

            IsSelected = mapNode.IsSelected;
        }

        public EntityData DetermineTopLevelEntityData(MapNode mapNode)
        {
            EntityData topLevelEntityData;
            if(mapNode.Entities?.Any() == true)
            {
                topLevelEntityData = mapNode.Entities.First().EntityData; //TODO how to order entities?
            }
            else
            {
                topLevelEntityData = mapNode.Terrain.EntityData;
            }
            return topLevelEntityData;
        }

        public void UpdateMapNode(MapNode mapNode)
        {

        }

        public void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D unitRectangle)
        {
            //Tile background. Highlight border if selected.
            if(IsSelected)
            {
                spriteBatch.Draw(unitRectangle, new Rectangle(DisplayPosition, Size), HighlightColor);
                spriteBatch.Draw(unitRectangle, new Rectangle(DisplayPosition.X + 1, DisplayPosition.Y + 1, Size.X - 2, Size.Y - 2), BackgroundColor);
            }
            else
            {
                spriteBatch.Draw(unitRectangle, new Rectangle(DisplayPosition, Size), BackgroundColor);
            }

            //Tile character
            if (DisplayCharacter != null)
            {
                var characterDisplayPosition = new Vector2(DisplayPosition.X + displayCharacterOffset.X, DisplayPosition.Y + displayCharacterOffset.Y);
                spriteBatch.DrawString(font, DisplayCharacter, characterDisplayPosition, ForegroundColor);
            }
        }
    }
}

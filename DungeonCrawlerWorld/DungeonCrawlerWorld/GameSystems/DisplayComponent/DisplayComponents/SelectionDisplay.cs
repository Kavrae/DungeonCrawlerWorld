using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;

namespace DungeonCrawlerWorld.GameComponents.DisplayComponent
{
    public class SelectionDisplay : IDisplayComponent
    {
        // Display Component
        public FontService FontService { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; }
        public Rectangle DisplayRectangle { get; set; }

        private DataAccess _dataAccess;

        public int LineBuffer = 5;
        public MapNode SelectedMapNode;

        public SelectionDisplay(DataAccess dataAccess, Vector2 position, Vector2 size)
        {
            FontService = GameServices.GetService<FontService>();

            _dataAccess = dataAccess;
            Size = size;
            Position = position;
            DisplayRectangle = new Rectangle(position.ToPoint(), Size.ToPoint());
        }

        public void Initialize() { }

        public void LoadContent() { }

        public void Update(GameTime gameTime)
        {
            SelectedMapNode = _dataAccess.RetrieveSelectedNode();
        }

        public Vector2 CalculateTextDisplaySize(int numberOfLines, SpriteFont font)
        {
            var width = Size.X - LineBuffer * 2;
            var height = (font.LineSpacing * numberOfLines) + (LineBuffer * (numberOfLines - 1));
            return new Vector2 (width, height);
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            var font = FontService.GetFont("defaultFont");

            spriteBatch.Draw(unitRectangle, DisplayRectangle, Color.White);

            if (SelectedMapNode != null)
            {
                var descriptionSize = CalculateTextDisplaySize(4, font);

                var terrainNamePosition = GetLinePosition(0, font);
                spriteBatch.DrawString(font, SelectedMapNode.Terrain.EntityData.Name, terrainNamePosition, Color.Blue);

                var selectedMapCoordinatesPosition = GetLinePosition(1, font);
                spriteBatch.DrawString(font, $"{SelectedMapNode.Position.X},{SelectedMapNode.Position.Y}", selectedMapCoordinatesPosition, Color.Blue);

                var terrainDescriptionPosition = GetLinePosition(2, font);
                var formattedTerrainDescription = StringUtility.FormatText(font, SelectedMapNode.Terrain.EntityData.Description, descriptionSize.ToPoint(), true, true);
                spriteBatch.DrawString(font, formattedTerrainDescription, terrainDescriptionPosition, Color.Blue);

                if(SelectedMapNode.Entities?.Any() == true)
                {
                    var entityNamePosition = GetLinePosition(6, font);
                    spriteBatch.DrawString(font, SelectedMapNode.Entities[0].EntityData.Name, entityNamePosition, Color.Black);

                    var entityDescriptionPosition = GetLinePosition(7, font);
                    var formattedEntityDescription = StringUtility.FormatText(font, SelectedMapNode.Entities[0].EntityData.Description, descriptionSize.ToPoint(), true, true);
                    spriteBatch.DrawString(font, formattedEntityDescription, entityDescriptionPosition, Color.Black);

                }
            }
        }

        public Vector2 GetLinePosition(int lineNumber, SpriteFont font)
        {
            return new Vector2((int)Position.X + LineBuffer, (int)Position.Y + (font.LineSpacing * lineNumber) + (LineBuffer * (lineNumber + 1)));
        }
    }
}

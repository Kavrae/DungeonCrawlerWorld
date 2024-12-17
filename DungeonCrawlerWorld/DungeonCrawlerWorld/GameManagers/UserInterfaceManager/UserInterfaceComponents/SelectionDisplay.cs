
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class SelectionDisplay : UserInterfaceComponent
    {
        private readonly World world;

        private readonly int lineBuffer = 5;
        private MapNode[] selectedMapNodes;

        private SpriteFont font;

        private Vector2 tabOffset;
        private int linesPerDescription;
        private Vector2 descriptionSize;

        public SelectionDisplay(World world, Point position, Point size) : base ( position, size )
        {
            this.world = world;
        }

        public override void Initialize()
        {
            FontService = GameServices.GetService<FontService>();
            font = FontService.GetFont("defaultFont");
            tabOffset = new Vector2(15, 0);
            linesPerDescription = 4;
            descriptionSize = CalculateTextDisplaySize(linesPerDescription, font);
        }

        public override void LoadContent() { }

        public override void Update(GameTime gameTime)
        {
            selectedMapNodes = world.SelectedMapNodes;
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            spriteBatch.Draw(unitRectangle, DisplayRectangle, Color.White);

            if (selectedMapNodes != null && selectedMapNodes.Length > 0)
            {
                var currentLine = 0;
                var mapCoordinatesPosition = GetLinePosition(currentLine, font);
                spriteBatch.DrawString(font, $"{selectedMapNodes[0].Position.X},{selectedMapNodes[0].Position.Y}", mapCoordinatesPosition, Color.Blue);
                currentLine++;

                foreach (var mapNode in selectedMapNodes)
                {
                    if( mapNode.EntityId != null)
                    {
                        if ( ComponentRepo.DisplayTextComponents.TryGetValue(mapNode.EntityId.Value, out DisplayTextComponent displayTextComponent) )
                        {
                            var namePosition = GetLinePosition(currentLine, font);
                            spriteBatch.DrawString(font, displayTextComponent.Name, namePosition, Color.Blue);
                            currentLine++;

                            var descriptionPosition = GetLinePosition(currentLine, font);
                            var formattedEntityDescription = StringUtility.FormatText(font, displayTextComponent.Description, descriptionSize.ToPoint(), true, true);
                            spriteBatch.DrawString(font, formattedEntityDescription, descriptionPosition + tabOffset, Color.Blue);
                            currentLine += linesPerDescription;
                        }
                    }
                }
            }
        }

        public Vector2 CalculateTextDisplaySize(int numberOfLines, SpriteFont font)
        {
            var width = Size.X - lineBuffer * 2;
            var height = (font.LineSpacing * numberOfLines) + (lineBuffer * (numberOfLines - 1));
            return new Vector2(width, height);
        }

        public Vector2 GetLinePosition(int lineNumber, SpriteFont font)
        {
            return new Vector2(Position.X + lineBuffer, Position.Y + (font.LineSpacing * lineNumber) + (lineBuffer * (lineNumber + 1)));
        }
    }
}

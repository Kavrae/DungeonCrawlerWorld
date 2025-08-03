using System.Reflection;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Utilities;
using DungeonCrawlerWorld.Services;

//TODO this is debugger mode. Player mode should be MUCH more limited and based on skills
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

        public SelectionDisplay(World world, Point position, Point size) : base(position, size)
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
                spriteBatch.DrawString(font, $"{selectedMapNodes[0].Position.X},{selectedMapNodes[0].Position.Y}", mapCoordinatesPosition, Color.Black);
                currentLine++;

                foreach (var mapNode in selectedMapNodes)
                {
                    if (mapNode.EntityId != null)
                    {
                        //TODO select each component for that entity
                        //Just display the component
                        //Add component click to expand and show all of that component's values
                        //Click again to minimize
                        //  Max/Min is just setting a boolen to display those values or not

                        //TODO scroll?

                        foreach (var component in ComponentRepo.GetAllComponents(mapNode.EntityId.Value))
                        {
                            currentLine = DrawComponentDebugInfo(component, spriteBatch, currentLine) + 1;
                        }
                    }
                }
            }
        }

        public int DrawComponentDebugInfo(IEntityComponent component, SpriteBatch spriteBatch, int currentLine)
        {
            spriteBatch.DrawString(font, component.GetType().Name, GetLinePosition(currentLine, font), Color.Black);
            currentLine++;

            foreach (PropertyInfo propertyInfo in component.GetType().GetProperties()
                .Where(property => !property.GetGetMethod().GetParameters().Any()))
            {
                var displayText = StringUtility.FormatText(new FormatTextCriteria
                {
                    Font = font,
                    MaximumPixelWidth = descriptionSize.X - tabOffset.X,
                    TextToFormat = $"    {propertyInfo.Name} : {propertyInfo.GetValue(component, null)}",
                    WordWrap = true
                });
                foreach (var line in displayText.FormattedTextLines)
                {
                    spriteBatch.DrawString(font, line, GetLinePosition(currentLine, font) + tabOffset, Color.Black);
                    currentLine++;
                }
            }
            return currentLine;
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

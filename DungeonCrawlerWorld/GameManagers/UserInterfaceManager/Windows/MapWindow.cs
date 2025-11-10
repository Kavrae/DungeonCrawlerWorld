using System;
using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Components;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class MapWindow : Window
    {
        private readonly World world;

        private Dictionary<FontType, SpriteFont> fonts;

        private Point currentScrollPosition;
        private Point maxScrollPosition;

        private Point currentTileSize;
        private int tileColumns;
        private int tileRows;
        private int tileDepth;

        private ZoomLevel currentZoomLevel;
        private Dictionary<ZoomLevel, Point> tileSizes;

        public MapWindow(World world, Point? tileSize, WindowOptions windowOptions) : base(null, windowOptions)
        {
            this.world = world;

            currentScrollPosition = new Point(0, 0);
            currentZoomLevel = ZoomLevel.Team;

            tileSizes = new Dictionary<ZoomLevel, Point>
            {
                { ZoomLevel.Team, new Point(12,12)},
                { ZoomLevel.Neighborhood, new Point(6,6)},
                { ZoomLevel.Borough, new Point(3,3)}
            };

            currentTileSize = tileSize ?? tileSizes[currentZoomLevel];

            tileDepth = this.world.Map.Size.Z;
        }

        public override void Initialize()
        {
            base.Initialize();

            var defaultMediumFont = FontService.GetFont("DefaultMediumFont");
            var defaultLargeFont = FontService.GetFont("DefaultLargeFont");
            var defaultHugeFont = FontService.GetFont("DefaultHugeFont");
            fonts = new Dictionary<FontType, SpriteFont>
            {
                { FontType.DefaultMedium, defaultMediumFont },
                { FontType.DefaultLarge, defaultLargeFont },
                { FontType.DefaultHuge, defaultHugeFont }
            };

            //+1 to account for partial tiles when scrolling
            tileColumns = (int)Math.Floor(ContentSize.X / currentTileSize.X) + 1;
            tileRows = (int)Math.Floor(ContentSize.Y / currentTileSize.Y) + 1;

            UpdateMaxScrollPosition();
        }

        public override void LoadContent()
        {
            base.LoadContent();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
        }

        public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            DrawBackgrounds(gameTime, spriteBatch, unitRectangle);
            DrawGlyphs(gameTime, spriteBatch);
        }

        public void DrawBackgrounds(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            var innerTileSize = new Point(currentTileSize.X - 2, currentTileSize.Y - 2);

            for (var column = 0; column < tileColumns; column++)
            {
                for (var row = 0; row < tileRows; row++)
                {
                    var mapNodeX = column + currentScrollPosition.X;
                    var mapNodeY = row + currentScrollPosition.Y;

                    for (var z = tileDepth - 1; z >= 0; z--)
                    {
                        var mapNode = world.Map.MapNodes[mapNodeX, mapNodeY, z];
                        if (mapNode.EntityId != null)
                        {
                            var nullableBackgroundComponent = ComponentRepo.BackgroundComponents[mapNode.EntityId.Value];
                            if (nullableBackgroundComponent != null)
                            {
                                var backgroundComponent = nullableBackgroundComponent.Value;

                                var isSelected = world.SelectedMapNodePosition != null
                                    && world.SelectedMapNodePosition.Value.X == mapNodeX
                                    && world.SelectedMapNodePosition.Value.Y == mapNodeY;

                                if (isSelected)
                                {
                                    spriteBatch.Draw(
                                        unitRectangle,
                                        new Rectangle(column * currentTileSize.X, row * currentTileSize.Y, currentTileSize.X, currentTileSize.Y),
                                        Color.Gold);

                                    spriteBatch.Draw(
                                        unitRectangle,
                                        new Rectangle(column * currentTileSize.X + 1, row * currentTileSize.Y + 1, innerTileSize.X, innerTileSize.Y),
                                        backgroundComponent.BackgroundColor ?? Color.White);
                                }
                                else
                                {
                                    spriteBatch.Draw(
                                        unitRectangle,
                                        new Rectangle(column * currentTileSize.X, row * currentTileSize.Y, currentTileSize.X, currentTileSize.Y),
                                        backgroundComponent.BackgroundColor ?? Color.White);
                                }

                                break;
                            }
                        }
                    }
                }
            }
        }

        public void DrawGlyphs(GameTime gameTime, SpriteBatch spriteBatch)
        {
            for (var column = 0; column < tileColumns; column++)
            {
                for (var row = 0; row < tileRows; row++)
                {
                    var mapNodeX = column + currentScrollPosition.X;
                    var mapNodeY = row + currentScrollPosition.Y;

                    for (var z = tileDepth - 1; z >= 0; z--)
                    {
                        var mapNode = world.Map.MapNodes[mapNodeX, mapNodeY, z];

                        //TODO use flags to determine if glyph is visible
                        if (mapNode.EntityId != null)
                        {
                            var entityId = mapNode.EntityId.Value;
                            var nullableGlyphComponent = ComponentRepo.GlyphComponents[entityId];
                            if (nullableGlyphComponent == null) break;

                            var nullableTransformComponent = ComponentRepo.TransformComponents[entityId];
                            if (nullableTransformComponent == null) break;

                            var glyphComponent = nullableGlyphComponent.Value;
                            var transformComponent = nullableTransformComponent.Value;

                            //Multi-tile glyph fix. Only draw the top left tile to avoid duplication.
                            if (transformComponent.Position.X != mapNodeX) break;
                            if( transformComponent.Position.Y != mapNodeY) break;

                            SpriteFont glyphFont = null;
                            if (transformComponent.Size.X == 1)
                            {
                                glyphFont = fonts[FontType.DefaultMedium];
                            }
                            else if (transformComponent.Size.X == 2)
                            {
                                glyphFont = fonts[FontType.DefaultLarge];
                            }
                            else if (transformComponent.Size.X == 3)
                            {
                                glyphFont = fonts[FontType.DefaultHuge];
                            }

                            spriteBatch.DrawString(
                                glyphFont,
                                glyphComponent.Glyph,
                                new Vector2(
                                    (column * currentTileSize.X) + glyphComponent.GlyphOffset.X,
                                    (row * currentTileSize.Y) + glyphComponent.GlyphOffset.Y),
                                glyphComponent.GlyphColor);

                            break;
                        }
                    }
                }
            }
        }

        private void UpdateMaxScrollPosition()
        {
            maxScrollPosition = new Point(
                world.Map.Size.X - tileColumns,
                world.Map.Size.Y - tileRows
            );
        }

        public void UpdateZoomLevel(ZoomLevel newZoomLevel)
        {
            currentZoomLevel = newZoomLevel;
        }

        public void UpdateScrollPosition(Point scrollChange)
        {
            currentScrollPosition = new Point
            {
                X = MathHelper.Clamp(currentScrollPosition.X + scrollChange.X, 0, maxScrollPosition.X),
                Y = MathHelper.Clamp(currentScrollPosition.Y + scrollChange.Y, 0, maxScrollPosition.Y)
            };
        }

        public void SelectMapNodes(Vector2 mousePosition)
        {
            if (world.Map.MapNodes != null && world.Map.MapNodes.Length > 0)
            {
                var relativeMapDisplayMousePosition = mousePosition - _contentAbsolutePosition;
                var x = (int)(relativeMapDisplayMousePosition.X / currentTileSize.X);

                if (x >= 0 && x < tileColumns)
                {
                    var y = (int)(relativeMapDisplayMousePosition.Y / currentTileSize.Y);
                    if (y >= 0 && y < tileRows)
                    {
                        world.SelectedMapNodePosition = new Point(x, y);
                    }
                }
            }
        }

        protected override void OnContentClickAction(Vector2 mousePosition)
        {
            SelectMapNodes(mousePosition);
        }
    }
}

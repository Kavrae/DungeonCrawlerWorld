using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class MapWindow : Window
    {
        private readonly World world;

        private Dictionary<FontType, SpriteFontBase> fonts;

        private Point currentScrollPosition;
        private Point maxScrollPosition;

        private Point currentTileSize;
        private Point innerTileSize;
        private int tileColumns;
        private int tileRows;
        private int tileDepth;

        private ZoomLevel currentZoomLevel;
        private Dictionary<ZoomLevel, Point> tileSizes;

        Rectangle drawRectangle;
        Rectangle innerDrawRectangle;

        private Color?[] backgroundColorCache;

        public MapWindow(World world, WindowOptions windowOptions) : base(null, windowOptions)
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

            tileDepth = this.world.Map.Size.Z;
        }

        public override void Initialize()
        {
            base.Initialize();

            fonts = new Dictionary<FontType, SpriteFontBase>
            {
                { FontType.DefaultMedium, FontService.GetFont(8) },
                { FontType.DefaultLarge, FontService.GetFont(24) },
                { FontType.DefaultHuge, FontService.GetFont(36) }
            };

            UpdateMaxScrollPosition();
            UpdateTileSizes();
            UpdateDrawRectangles();
            UpdateBackgroundColorCache();
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
            var selectedTileDrawn = false;
            Color? backgroundColor;

            for (var column = 0; column < tileColumns; column++)
            {
                for (var row = 0; row < tileRows; row++)
                {
                    backgroundColor = backgroundColorCache[column + row * tileColumns];

                    if (backgroundColor != null)
                    {
                        drawRectangle.X = column * currentTileSize.X;
                        drawRectangle.Y = row * currentTileSize.Y;

                        if (!selectedTileDrawn && world.SelectedMapNodePosition != null
                            && world.SelectedMapNodePosition.Value.X == column + currentScrollPosition.X
                            && world.SelectedMapNodePosition.Value.Y == row + currentScrollPosition.Y)
                        {
                            spriteBatch.Draw(
                                unitRectangle,
                                drawRectangle,
                                Color.Gold);

                            innerDrawRectangle.X = (column * currentTileSize.X) + 1;
                            innerDrawRectangle.Y = (row * currentTileSize.Y) + 1;

                            spriteBatch.Draw(
                                unitRectangle,
                                innerDrawRectangle,
                                backgroundColor.Value);

                            selectedTileDrawn = true;
                        }
                        else
                        {
                            spriteBatch.Draw(
                                unitRectangle,
                                drawRectangle,
                                backgroundColor.Value);
                        }
                    }
                }
            }
        }

        public void DrawGlyphs(GameTime gameTime, SpriteBatch spriteBatch)
        {
            int mapNodeX;
            int mapNodeY;
            MapNode mapNode;

            int entityId;

            GlyphComponent? nullableGlyphComponent;
            GlyphComponent glyphComponent;
            TransformComponent? nullableTransformComponent;
            TransformComponent transformComponent;

            Vector2 drawPosition;
            SpriteFontBase glyphFont;

            for (var column = 0; column < tileColumns; column++)
            {
                for (var row = 0; row < tileRows; row++)
                {
                    mapNodeX = column + currentScrollPosition.X;
                    mapNodeY = row + currentScrollPosition.Y;

                    for (var z = tileDepth - 1; z >= 0; z--)
                    {
                        mapNode = world.Map.MapNodes[mapNodeX, mapNodeY, z];

                        if (mapNode.EntityId != null)
                        {
                            entityId = mapNode.EntityId.Value;
                            nullableGlyphComponent = ComponentRepo.GlyphComponents[entityId];
                            if (nullableGlyphComponent == null) break;

                            nullableTransformComponent = ComponentRepo.TransformComponents[entityId];
                            if (nullableTransformComponent == null) break;

                            glyphComponent = nullableGlyphComponent.Value;
                            transformComponent = nullableTransformComponent.Value;

                            //Multi-tile glyph fix. Only draw the top left tile to avoid duplication.
                            if (transformComponent.Position.X != mapNodeX) break;
                            if (transformComponent.Position.Y != mapNodeY) break;

                            glyphFont = null;
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

                            drawPosition = new Vector2(
                                (column * currentTileSize.X) + glyphComponent.GlyphOffset.X,
                                (row * currentTileSize.Y) + glyphComponent.GlyphOffset.Y);

                            spriteBatch.DrawString(
                                glyphFont,
                                glyphComponent.Glyph,
                                drawPosition,
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
            UpdateTileSizes();
            UpdateDrawRectangles();
            UpdateBackgroundColorCache();
        }

        public void UpdateScrollPosition(Point scrollChange)
        {
            currentScrollPosition = new Point
            {
                X = MathUtility.ClampInt(currentScrollPosition.X + scrollChange.X, 0, maxScrollPosition.X),
                Y = MathUtility.ClampInt(currentScrollPosition.Y + scrollChange.Y, 0, maxScrollPosition.Y)
            };
            UpdateBackgroundColorCache();
        }

        public void UpdateTileSizes()
        {
            currentTileSize = tileSizes[currentZoomLevel];
            innerTileSize = new Point(currentTileSize.X - 2, currentTileSize.Y - 2);

            //+1 to account for partial tiles when scrolling
            tileColumns = (int)Math.Floor(ContentSize.X / currentTileSize.X) + 1;
            tileRows = (int)Math.Floor(ContentSize.Y / currentTileSize.Y) + 1;

            backgroundColorCache = new Color?[tileColumns * tileRows];
        }

        public void UpdateDrawRectangles()
        {
            drawRectangle = new Rectangle(0, 0, currentTileSize.X, currentTileSize.Y);
            innerDrawRectangle = new Rectangle(0, 0, innerTileSize.X, innerTileSize.Y);
        }

        public void UpdateBackgroundColorCache()
        {
            int mapNodeX;
            int mapNodeY;
            MapNode mapNode;
            BackgroundComponent? nullableBackgroundComponent;

            for (var column = 0; column < tileColumns; column++)
            {
                for (var row = 0; row < tileRows; row++)
                {
                    mapNodeX = column + currentScrollPosition.X;
                    mapNodeY = row + currentScrollPosition.Y;

                    for (var z = tileDepth - 1; z >= 0; z--)
                    {
                        mapNode = world.Map.MapNodes[mapNodeX, mapNodeY, z];
                        if (mapNode.EntityId != null)
                        {
                            nullableBackgroundComponent = ComponentRepo.BackgroundComponents[mapNode.EntityId.Value];
                            if (nullableBackgroundComponent != null)
                            {
                                backgroundColorCache[column + row * tileColumns] = nullableBackgroundComponent.Value.BackgroundColor;

                                break;
                            }
                        }
                    }
                }
            }
        }

        public void SelectMapNodes(Point mousePosition)
        {
            if (world.Map.MapNodes != null && world.Map.MapNodes.Length > 0)
            {
                var relativeMapDisplayMousePosition = new Vector2(mousePosition.X - _contentAbsolutePosition.X, mousePosition.Y - _contentAbsolutePosition.Y);
                var x = (int)(relativeMapDisplayMousePosition.X / currentTileSize.X);

                if (x >= 0 && x < tileColumns)
                {
                    var y = (int)(relativeMapDisplayMousePosition.Y / currentTileSize.Y);
                    if (y >= 0 && y < tileRows)
                    {
                        world.SelectedMapNodePosition = new Point(x + currentScrollPosition.X, y + currentScrollPosition.Y);
                    }
                }
            }
        }

        protected override void OnContentClickAction(Point mousePosition)
        {
            SelectMapNodes(mousePosition);
        }
    }
}

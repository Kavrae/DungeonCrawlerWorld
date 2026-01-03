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
        private DataAccessService dataAccessService;

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

        public MapWindow()
        {
            dataAccessService = GameServices.GetService<DataAccessService>();
            world = dataAccessService.RetrieveWorld();

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

        public void BuildWindow(Window parentWindow, TextWindowOptions windowOptions)
        {
            base.BuildWindow(parentWindow, windowOptions);
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
            ResetBackgroundColorCache();
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
            int columnIndex;
            int rowIndex;

            for (columnIndex = 0; columnIndex < tileColumns; columnIndex++)
            {
                for (rowIndex = 0; rowIndex < tileRows; rowIndex++)
                {
                    backgroundColor = backgroundColorCache[columnIndex + rowIndex * tileColumns];

                    if (backgroundColor != null)
                    {
                        drawRectangle.X = columnIndex * currentTileSize.X;
                        drawRectangle.Y = rowIndex * currentTileSize.Y;

                        if (!selectedTileDrawn && world.SelectedMapNodePosition != null
                            && world.SelectedMapNodePosition.Value.X == columnIndex + currentScrollPosition.X
                            && world.SelectedMapNodePosition.Value.Y == rowIndex + currentScrollPosition.Y)
                        {
                            spriteBatch.Draw(
                                unitRectangle,
                                drawRectangle,
                                Color.Gold);

                            innerDrawRectangle.X = (columnIndex * currentTileSize.X) + 1;
                            innerDrawRectangle.Y = (rowIndex * currentTileSize.Y) + 1;

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

            int columnIndex;
            int rowIndex;
            int heightIndex;

            for (columnIndex = 0; columnIndex < tileColumns; columnIndex++)
            {
                for (rowIndex = 0; rowIndex < tileRows; rowIndex++)
                {
                    mapNodeX = columnIndex + currentScrollPosition.X;
                    mapNodeY = rowIndex + currentScrollPosition.Y;

                    for (heightIndex = tileDepth - 1; heightIndex >= 0; heightIndex--)
                    {
                        mapNode = world.Map.MapNodes[mapNodeX, mapNodeY, heightIndex];

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
                                (columnIndex * currentTileSize.X) + glyphComponent.GlyphOffset.X,
                                (rowIndex * currentTileSize.Y) + glyphComponent.GlyphOffset.Y);

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
            ResetBackgroundColorCache();
        }

        public void UpdateScrollPosition(Point scrollChange)
        {
            var previousScrollPosition = currentScrollPosition;

            currentScrollPosition = new Point
            {
                X = MathUtility.ClampInt(currentScrollPosition.X + scrollChange.X, 0, maxScrollPosition.X),
                Y = MathUtility.ClampInt(currentScrollPosition.Y + scrollChange.Y, 0, maxScrollPosition.Y)
            };

            IncrementalUpdateBackgroundColorCache(
                currentScrollPosition.X - previousScrollPosition.X,
                currentScrollPosition.Y - previousScrollPosition.Y);
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

        public void ResetBackgroundColorCache()
        {
            int mapNodeX;
            int mapNodeY;

            for (var column = 0; column < tileColumns; column++)
            {
                for (var row = 0; row < tileRows; row++)
                {
                    mapNodeX = column + currentScrollPosition.X;
                    mapNodeY = row + currentScrollPosition.Y;

                    backgroundColorCache[column + row * tileColumns] = ResolveTopBackgroundColor(mapNodeX, mapNodeY);
                }
            }
        }

        private void IncrementalUpdateBackgroundColorCache(int scrollDeltaX, int scrollDeltaY)
        {
            int columnIndex;
            int rowIndex;
            int scrollColumn;
            int scrollRow;

            if ((scrollDeltaX == 0 && scrollDeltaY == 0) || backgroundColorCache == null)
            {
                return;
            }

            var temporaryColorCache = new Color?[tileColumns * tileRows];

            // shift existing cells into new positions
            for (columnIndex = 0; columnIndex < tileColumns; columnIndex++)
            {
                for (rowIndex = 0; rowIndex < tileRows; rowIndex++)
                {
                    scrollColumn = columnIndex + scrollDeltaX;
                    scrollRow = rowIndex + scrollDeltaY;

                    if (scrollColumn >= 0 && scrollColumn < tileColumns && scrollRow >= 0 && scrollRow < tileRows)
                    {
                        temporaryColorCache[columnIndex + rowIndex * tileColumns] = backgroundColorCache[scrollColumn + scrollRow * tileColumns];
                    }
                    else
                    {
                        temporaryColorCache[columnIndex + rowIndex * tileColumns] = null; // will be filled below
                    }
                }
            }

            backgroundColorCache = temporaryColorCache;

            // fill only newly exposed columns/rows
            if (scrollDeltaX > 0)
            {
                // new rightmost columns
                for (int column = tileColumns - scrollDeltaX; column < tileColumns; column++)
                {
                    FillBackgroundColumn(column);
                }
            }
            else if (scrollDeltaX < 0)
            {
                // new leftmost columns
                for (int column = 0; column < -scrollDeltaX; column++)
                {
                    FillBackgroundColumn(column);
                }
            }

            if (scrollDeltaY > 0)
            {
                for (int row = tileRows - scrollDeltaY; row < tileRows; row++)
                {
                    FillBackgroundRow(row);
                }
            }
            else if (scrollDeltaY < 0)
            {
                for (int row = 0; row < -scrollDeltaY; row++)
                {
                    FillBackgroundRow(row);
                }
            }
        }

        private void FillBackgroundColumn(int column)
        {
            int mapNodeX = column + currentScrollPosition.X;
            for (int row = 0; row < tileRows; row++)
            {
                int mapNodeY = row + currentScrollPosition.Y;
                backgroundColorCache[column + row * tileColumns] = ResolveTopBackgroundColor(mapNodeX, mapNodeY);
            }
        }

        private void FillBackgroundRow(int row)
        {
            int mapNodeY = row + currentScrollPosition.Y;
            for (int column = 0; column < tileColumns; column++)
            {
                int mapNodeX = column + currentScrollPosition.X;
                backgroundColorCache[column + row * tileColumns] = ResolveTopBackgroundColor(mapNodeX, mapNodeY);
            }
        }

        private Color? ResolveTopBackgroundColor(int mapNodeX, int mapNodeY)
        {
            for (int z = tileDepth - 1; z >= 0; z--)
            {
                var mapNode = world.Map.MapNodes[mapNodeX, mapNodeY, z];
                if (mapNode.EntityId != null)
                {
                    var backgroundComponents = ComponentRepo.BackgroundComponents[mapNode.EntityId.Value];
                    if (backgroundComponents != null)
                    {
                        return backgroundComponents.Value.BackgroundColor;
                    }
                }
            }
            return null;
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

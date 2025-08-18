﻿using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Services;

//TODO minimize and restore
//TODO recalculate tiled sibling windows on minimize and restore
//TODO close
//TODO persist selectionWindow child windows until selection changes
//TODO default selectionWindow child windows to minimized. Keep track of which components stay restored between selections

//TODO click-and-drag create a semi-transparent "ghost" window that follows the curser. On mouse-up, delete the ghost window and position the original window in that spot. Then clamp to parent content rectangle.

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class Window
    {
        protected Guid _windowId;
        public Guid WindowId { get { return _windowId; } }

        private GraphicsDevice graphicsDevice;

        public FontService FontService;

        /*========Window hierarchy========*/
        protected Window _parentWindow;
        public Window ParentWindow { get { return _parentWindow; } }

        protected bool _canContainChildWindows;
        public bool CanContainChildWindows { get { return _canContainChildWindows; } }

        protected WindowTileMode _childWindowTileMode;
        public WindowTileMode ChildWindowTileMode { get { return _childWindowTileMode; } }

        protected List<Window> _childWindows;
        public List<Window> ChildWindows { get { return _childWindows; } }

        /*========Window========*/
        protected WindowDisplayMode _windowDisplayMode;
        public WindowDisplayMode WindowDisplay { get { return _windowDisplayMode; } }

        protected Vector2 _windowAbsolutePosition;
        public Vector2 WindowAbsolutePosition { get { return _windowAbsolutePosition; } }

        protected Vector2 _windowRelativePosition; //Relative to parent window
        public Vector2 WindowRelativePosition { get { return _windowRelativePosition; } }

        protected Vector2 _windowOriginalSize;
        public Vector2 WindowOriginalSize { get { return _windowOriginalSize; } }

        protected Vector2 _windowCurrentSize;
        public Vector2 WindowCurrentSize { get { return _windowCurrentSize; } }

        protected Vector2 _windowMinimumSize;
        public Vector2 WindowMinimumSize { get { return _windowMinimumSize; } }

        protected Vector2 _windowMaximumSize;
        public Vector2 WindowMaximumSize { get { return _windowMaximumSize; } }

        protected Rectangle _windowRectangle;
        public Rectangle WindowRectangle { get { return _windowRectangle; } }

        /*========Title========*/
        protected SpriteFont TitleFont;

        protected bool _showTitle;
        public bool ShowTitle { get { return _showTitle; } }

        protected string _titleText;
        public string TitleText { get { return _titleText; } set { _titleText = value; } }
        public Vector2 TitlePadding = new(5, 2);

        protected Vector2 _originalTitleSize;
        public Vector2 OriginalTitleSize { get { return _originalTitleSize; } }

        protected Vector2 _titleSize;
        public Vector2 TitleSize { get { return _titleSize; } }

        protected Vector2 _titleAbsolutePosition;
        public Vector2 TitleAbsolutePosition { get { return _titleAbsolutePosition; } }

        protected Rectangle _titleRectangle;
        public Rectangle TitleRectangle { get { return _titleRectangle; } }


        /*========Border========*/
        protected bool _showBorder;
        public bool ShowBorder { get { return _showBorder; } }

        protected Vector2 _borderSize;
        public Vector2 BorderSize { get { return _borderSize; } }

        /*========Content========*/
        protected Vector2 _contentAbsolutePosition;
        public Vector2 ContentAbsolutePosition { get { return _contentAbsolutePosition; } }

        protected Vector2 _contentSize;
        public Vector2 ContentSize { get { return _contentSize; } }

        protected Rectangle _contentRectangle;
        public Rectangle ContentRectangle { get { return _contentRectangle; } }

        public Vector2 ContentPadding = new(5, 5);


        /*========Viewport========*/
        private Viewport _windowViewport;
        public Viewport WindowViewport {get { return _windowViewport; }}

        private Matrix _cameraTransform;
        public Matrix CameraTransform{ get { return _cameraTransform; }}

        /*========User Controls========*/
        public bool CanUserClose { get; set; }
        public bool CanUserMinimize { get; set; }
        public bool CanUserMove { get; set; }
        public bool CanUserResize { get; set; }
        public bool CanUserScrollHorizontal { get; set; }
        public bool CanUserScrollVertical { get; set; }
        public bool IsMinimized { get; set; }

        public Window(Window parentWindow, WindowOptions windowOptions)
        {
            _windowId = Guid.NewGuid();

            graphicsDevice = GameServices.GetService<GraphicsDevice>();

            FontService = GameServices.GetService<FontService>();
            TitleFont = FontService.GetFont("defaultFont");

            /*========Window hierarchy========*/
            _parentWindow = parentWindow;
            _canContainChildWindows = windowOptions.CanContainChildWindows ?? false;
            _childWindowTileMode = windowOptions.ChildWindowTileMode ?? WindowTileMode.Floating;
            _childWindows = new List<Window>();

            /*========Window========*/
            _windowDisplayMode = windowOptions.DisplayMode ?? WindowDisplayMode.Static;
            _windowRelativePosition = windowOptions.RelativePosition ?? new Vector2();
            _windowAbsolutePosition = _parentWindow != null
                ? _parentWindow.ContentAbsolutePosition + _windowRelativePosition
                : _windowRelativePosition;

            _windowMinimumSize = windowOptions.MinimumSize ?? new Vector2(0, 0);
            _windowMaximumSize = windowOptions.MaximumSize ?? _parentWindow?.ContentSize ?? windowOptions.Size ?? new Vector2(0, 0);
            _windowOriginalSize = windowOptions.Size ?? new Vector2(0, 0);
            _windowCurrentSize = _windowOriginalSize;

            /*========Title========*/
            _showTitle = windowOptions.ShowTitle ?? false;
            _titleText = windowOptions.TitleText ?? string.Empty;
            _originalTitleSize = new Vector2(_windowOriginalSize.X, TitleFont.MeasureString(" ").Y + TitlePadding.Y * 2);
            _titleSize = _originalTitleSize;

            /*========Border========*/
            _showBorder = windowOptions.ShowBorder ?? false;
            _borderSize = windowOptions.BorderSize ?? new Vector2(1, 1);

            /*========User Controls========*/
            CanUserClose = windowOptions.CanUserClose ?? false;
            CanUserMinimize = windowOptions.CanUserMinimize ?? false;
            CanUserMove = windowOptions.CanUserMove ?? false;
            CanUserResize = windowOptions.CanUserResize ?? false;
            CanUserScrollHorizontal = windowOptions.CanUserScrollHorizontal ?? false;
            CanUserScrollVertical = windowOptions.CanUserScrollVertical ?? false;
            IsMinimized = windowOptions.ContentIsMinimized ?? false;
        }

        public virtual void Initialize()
        {
            RecalculateSizeAndAbsolutePosition();

            _windowViewport = new Viewport(_contentRectangle);
            _cameraTransform = Matrix.CreateRotationZ(0) * // camera rotation, default 0
                         Matrix.CreateScale(new Vector3(1, 1, 1)); //TODO zoom

            foreach (var childWindow in _childWindows)
            {
                childWindow.Initialize();
            }
        }

        public virtual void LoadContent()
        {
            foreach (var childWindow in _childWindows)
            {
                childWindow.LoadContent();
            }
        }

        public virtual void Update(GameTime gameTime)
        {
            foreach (var childWindow in _childWindows)
            {
                childWindow.Update(gameTime);
            }
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            if (_showBorder)
            {
                spriteBatch.Draw(unitRectangle, _windowRectangle, Color.Black);
            }

            if (_showTitle)
            {
                spriteBatch.Draw(unitRectangle, _titleRectangle, Color.LightBlue);
                spriteBatch.DrawString(TitleFont, TitleText, _titleAbsolutePosition + TitlePadding, Color.Black);
            }

            spriteBatch.Draw(unitRectangle, _contentRectangle, Color.White);

            spriteBatch.End();

            var previousViewport = graphicsDevice.Viewport;
            graphicsDevice.Viewport = WindowViewport;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null, CameraTransform);

            DrawContent(gameTime, spriteBatch, unitRectangle);

            spriteBatch.End();
            graphicsDevice.Viewport = previousViewport;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, null, null, null, null);
            
            if (_childWindows != null)
            {
                foreach (var childWindow in _childWindows)
                {
                    childWindow.Draw(gameTime, spriteBatch, unitRectangle);
                }
            }
        }

        public virtual void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle) { }

        public bool IsInDisplayRectangle(Point point)
        {
            return _contentRectangle.Contains(point);
        }

        public virtual void HandleClickDown(Vector2 mousePosition)
        {
            if (_titleRectangle.Contains(mousePosition))
            {
                HandleTitleClickDown(mousePosition);
            }
            else if (_contentRectangle.Contains(mousePosition))
            {
                HandleContentClickDown(mousePosition);
            }
        }


        public virtual void HandleTitleClickDown(Vector2 mousePosition)
        {
            //TODO handle title click options
        }

        public virtual void HandleContentClickDown(Vector2 mousePosition)
        {
            foreach (var childWindow in _childWindows)
            {
                if (childWindow.WindowRectangle.Contains(mousePosition))
                {
                    childWindow.HandleClickDown(mousePosition);
                }
            }
        }

        public void AddChildWindow(Window newChildWindow, int? insertIndex = null)
        {
            if (!_canContainChildWindows)
            {
                return;
            }

            //Default to the end of the list
            insertIndex = Math.Clamp(insertIndex ?? _childWindows.Count(),
                0,
                _childWindows.Count());

            _childWindows.Insert(insertIndex.Value, newChildWindow);

            for (var updateIndex = insertIndex.Value; updateIndex < _childWindows.Count(); updateIndex++)
            {
                if (_childWindowTileMode == WindowTileMode.Floating)
                {
                    //Do nothing. Let the window's creator determine its relative position and draw the windows in index order
                }
                else if (updateIndex == 0)
                {
                    //First item. For now, default it to 0,0 within the parent.
                    newChildWindow._windowRelativePosition = new Vector2(0, 0);
                }
                else
                {
                    //Not first item. Tile either horizontally or vertically with no buffer.
                    var previousChildWindow = _childWindows[updateIndex - 1];
                    if (_childWindowTileMode == WindowTileMode.Horizontal)
                    {
                        newChildWindow._windowRelativePosition = new Vector2(
                            previousChildWindow._windowRelativePosition.X + previousChildWindow._windowCurrentSize.X,
                            previousChildWindow._windowRelativePosition.Y
                        );
                    }
                    else if (_childWindowTileMode == WindowTileMode.Vertical)
                    {
                        newChildWindow._windowRelativePosition = new Vector2(
                            previousChildWindow._windowRelativePosition.X,
                            previousChildWindow._windowRelativePosition.Y + previousChildWindow._windowCurrentSize.Y
                        );
                    }
                }

                newChildWindow.Initialize();
            }
        }

        //Empty and re-add so we recalculate the remaining sibling window positions.
        public void RemoveChildWindow(Guid windowId)
        {
            var childWindowCopy = _childWindows.ToArray();
            _childWindows = new List<Window>();

            foreach (var childWindow in childWindowCopy)
            {
                if (childWindow.WindowId != windowId)
                {
                    AddChildWindow(childWindow);
                    childWindow.Initialize();
                }
            }
        }

        public void RecalculateSizeAndAbsolutePosition()
        {
            //If there is no parent, then it's one of the core windows that are guaranteed to have a maximum set
            if (_parentWindow != null)
            {
                _windowMaximumSize = _parentWindow.ContentSize - _windowRelativePosition;
            }

            switch (_windowDisplayMode)
            {
                case WindowDisplayMode.Static:
                    RecalculateStaticWindowSize();
                    break;
                case WindowDisplayMode.Fill:
                    RecalculateFillWindowSize();
                    break;
                case WindowDisplayMode.Grow:
                    RecalculateGrowWindowSize();
                    break;
                default:
                    throw new NotImplementedException("No default window display mode.");
            }

            RecalculateAbsolutePositions();
            RecalculateRectangles();
            RecalculateChildrenSizeAndPosition();
        }

        public void RecalculateAbsolutePositions()
        {
            _windowAbsolutePosition = _parentWindow != null
                ? _parentWindow.WindowAbsolutePosition + _windowRelativePosition
                : _windowRelativePosition;

            _titleAbsolutePosition = new Vector2(
                _windowAbsolutePosition.X + (_showBorder ? _borderSize.X : 0),
                _windowAbsolutePosition.Y + (_showBorder ? _borderSize.Y : 0));

            _contentAbsolutePosition = new Vector2(
                _windowAbsolutePosition.X + (_showBorder ? _borderSize.X : 0),
                _windowAbsolutePosition.Y + (_showBorder ? _borderSize.Y : 0) + (_showTitle ? _titleSize.Y : 0));
        }

        public virtual void RecalculateStaticWindowSize()
        {
            _windowCurrentSize = new Vector2(
                MathHelper.Clamp(_windowOriginalSize.X, _windowMinimumSize.X, _windowMaximumSize.X),
                MathHelper.Clamp(_windowOriginalSize.Y, _windowMinimumSize.Y, _windowMaximumSize.Y)
            );

            //Resize horizontally to fit the new window size, but keep the vertical size
            _titleSize = new Vector2(
                _windowCurrentSize.X - (_showBorder ? _borderSize.X * 2 : 0),
                _originalTitleSize.Y - (_showBorder ? _borderSize.Y : 0));

            _contentSize = new Vector2(
                _windowCurrentSize.X - (_showBorder ? _borderSize.X * 2 : 0),
                _windowCurrentSize.Y - (_showBorder ? _borderSize.Y : 0) - (_showTitle ? _titleSize.Y : 0));
        }

        public virtual void RecalculateFillWindowSize()
        {
            _windowCurrentSize = _windowMaximumSize;

            //Resize horizontally to fit the new window size, but keep the vertical size
            _titleSize = new Vector2(
                _windowCurrentSize.X - (_showBorder ? _borderSize.X * 2 : 0),
                _originalTitleSize.Y - (_showBorder ? _borderSize.Y : 0));

            _contentSize = _windowCurrentSize
                - (_showTitle ? _titleSize : new Vector2(0, 0))
                - (_showBorder ? Vector2.Multiply(_borderSize, 2) : new Vector2(0, 0));
        }

        public virtual void RecalculateGrowWindowSize()
        {
            if (_canContainChildWindows && _childWindows != null)
            {
                _contentSize = new Vector2(
                    _childWindows.Max(childWindow => childWindow.WindowRectangle.Right),
                    _childWindows.Max(childWindow => childWindow.WindowRectangle.Bottom));
            }
            else
            {
                _contentSize = new Vector2(0, 0);
            }

            _contentSize += ContentPadding;

            _windowCurrentSize = _contentSize;
            if (_showTitle)
            {
                //Resize horizontally to fit the new content size, but keep the vertical size
                _titleSize = new Vector2(
                    _contentSize.X,
                    _originalTitleSize.Y - (_showBorder ? _borderSize.Y : 0));
                _windowCurrentSize += _titleSize;
            }
            if (_showBorder)
            {
                _windowCurrentSize += Vector2.Multiply(_borderSize, 2);
            }
        }

        /// <summary>
        /// Recalculate the rectangles based on changed absolute positions or sizes
        /// These rectangles are used to draw while only recalculating when something changes.
        /// </summary>
        public void RecalculateRectangles()
        {
            _windowRectangle = new Rectangle((int)_windowAbsolutePosition.X, (int)_windowAbsolutePosition.Y, (int)_windowCurrentSize.X, (int)_windowCurrentSize.Y);
            _titleRectangle = new Rectangle((int)_titleAbsolutePosition.X, (int)_titleAbsolutePosition.Y, (int)_titleSize.X, (int)_titleSize.Y);
            _contentRectangle = new Rectangle((int)_contentAbsolutePosition.X, (int)_contentAbsolutePosition.Y, (int)_contentSize.X, (int)_contentSize.Y);
        }

        public void RecalculateChildrenSizeAndPosition()
        {
            if (_childWindows != null)
            {
                foreach (var childWindow in _childWindows)
                {
                    childWindow.RecalculateSizeAndAbsolutePosition();
                }
            }
        }
    }
}
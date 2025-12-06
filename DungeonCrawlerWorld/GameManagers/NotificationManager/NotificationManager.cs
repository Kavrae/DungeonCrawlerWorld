using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.GameManagers.UserInterfaceManager;
using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld.GameManagers.NotificationManager
{
    /// <summary>
    /// Manages the creation, display, and lifecycle of notifications within the game.
    /// IN PROGRESS
    /// </summary>
    public class NotificationManager : IGameManager
    {
        /// <summary>
        /// Notifications should update and display even when the game is paused.
        /// </summary>
        public bool CanUpdateWhilePaused => true;

        private Window _notificationSummaryContainer;
        private List<(NotificationType notificationType, TextWindow summaryWindow, List<Notification> notifications)> _unreadNotifications;

        //TODO once this all works, make a _notificationActiveContainer
        private List<(TextWindow activeWindow, Notification notification)> _activeNotifications; 

        private GraphicsDevice graphicsDevice;
        private SpriteBatchService spriteBatchService;
        private Texture2D unitRectangle;

        public NotificationManager()
        {
            _unreadNotifications = new List<(NotificationType notificationType, TextWindow summaryWindow, List<Notification> notifications)>();
            _activeNotifications = new List<(TextWindow activeWindow, Notification notification)>();

            graphicsDevice = GameServices.GetService<GraphicsDevice>();
            spriteBatchService = GameServices.GetService<SpriteBatchService>();

            unitRectangle = new Texture2D(graphicsDevice, 1, 1);
            unitRectangle.SetData(new[] { Color.White });
        }

        public void Initialize()
        {
            _notificationSummaryContainer = new Window(null, new WindowOptions
            {
                CanContainChildWindows = true,
                ChildWindowTileMode = WindowTileMode.Horizontal,
                DisplayMode = WindowDisplayMode.Static,
                IsTransparent = true,
                RelativePosition = new Vector2(12, 30),
                Size = new Vector2(1531, 25),
                ShowTitle = false,
            });
            _notificationSummaryContainer.Initialize();

            foreach (var notificationType in Enum.GetValues(typeof(NotificationType)).Cast<NotificationType>())
            {
                var notificationCountWindow = new TextWindow(
                    _notificationSummaryContainer,
                    new TextWindowOptions
                    {
                        CanContainChildWindows = false,
                        DisplayMode = WindowDisplayMode.Static,
                        Size = new Vector2(65, 21),
                        IsTransparent = false,
                        ShowBorder = true,
                        ShowTitle = false,
                        Text = $"{notificationType}: 0",
                        ContentColor = Color.LightGray
                    });
                _unreadNotifications.Add( new (notificationType, notificationCountWindow, new List<Notification>()));
                _notificationSummaryContainer.AddChildWindow(notificationCountWindow);
            }

            //TODO remove these tests
            AddNotification(NotificationType.System, "System message", true);
            AddNotification(NotificationType.System, "System message 2", true);
            AddNotification(NotificationType.System, "System message 3", true);
            AddNotification(NotificationType.Quest, "Quest message 1", true);
            AddNotification(NotificationType.Quest, "Quest message 2", true);
        }

        public void LoadContent()
        {
            _notificationSummaryContainer.LoadContent();
            _activeNotifications.ForEach(tuple => tuple.activeWindow.LoadContent());
        }

        public void UnloadContent()
        {
        }

        public void Update(GameTime gameTime, GameVariables gameVariables)
        {
        }

        public void Draw(GameTime gameTime)
        {
            var spriteBatch = spriteBatchService.StartSpriteBatch();

            _notificationSummaryContainer.Draw(gameTime, spriteBatch, unitRectangle);
            _activeNotifications.ForEach(tuple => tuple.activeWindow.Draw(gameTime, spriteBatch, unitRectangle));

            spriteBatchService.EndSpriteBatch();
        }

        public void AddNotification(NotificationType notificationType, string message, bool isActive)
        {
            var offset = _activeNotifications.Count * 10;
            var notification = new Notification(message, notificationType);

            if (isActive)
            {
                var notificationWindow = new TextWindow(
                        null,
                        new TextWindowOptions
                        {
                            RelativePosition = new Vector2(200 + offset, 200 + offset),
                            MaximumSize = new Vector2(400, 300),
                            CanContainChildWindows = false,
                            CanUserMinimize = true,
                            DisplayMode = WindowDisplayMode.Grow,
                            IsTransparent = false,
                            ShowBorder = true,
                            ShowTitle = true,
                            ShowTitleWhenMinimized = true,
                            TitleText = notificationType.ToString(),
                            Text = message
                        });
                _activeNotifications.Add( new(notificationWindow, notification) );
                notificationWindow.Initialize();
            }
            else
            {
                _unreadNotifications
                    .First(notificationTuple => notificationTuple.notificationType == notificationType)
                    .notifications.Add(notification);
                UpdateUnreadNotificationSummary(notificationType);
            }
        }
        
        public void UpdateUnreadNotificationSummary(NotificationType notificationType)
        {
            var notificationTuple = _unreadNotifications
                .First(tuple => tuple.notificationType == notificationType);
            var count = notificationTuple.notifications.Count;
            notificationTuple.summaryWindow.UpdateText($"{notificationType}: {count}");
        }

        public void CloseNotification(Guid notificationId)
        {
            if( _activeNotifications.Any(tuple => tuple.notification.Id == notificationId) )
            {
                var notificationTuple = _activeNotifications.First(tuple => tuple.notification.Id == notificationId);
                _activeNotifications.Remove(notificationTuple);
                notificationTuple.activeWindow.Close();
            }
        }

        public void OpenNextNotification(NotificationType notificationType)
        {
            var notificationList = _unreadNotifications
                .First(tuple => tuple.notificationType == notificationType)
                .notifications;

            var notification = notificationList.FirstOrDefault();

            if (notification != null)
            {
                AddNotification(notificationType, notification.Text, true);
                notificationList.Remove(notification);
                UpdateUnreadNotificationSummary(notificationType);
            }
        }

        public void MinimizeNotification(Guid notificationId)
        {
            var (notificationWindow, notification) = _activeNotifications.First(tuple => tuple.notification.Id == notificationId);

            var (notificationType, unreadNotificationSummaryWindow, unreadNotificationList) = _unreadNotifications
                .First(tuple => tuple.notificationType == notification.NotificationType);

            AddNotification(notificationType, notification.Text, false);
            CloseNotification(notificationId);
        }

        public void MinimizeAllNotifications()
        {
            foreach( var (_, notification) in _activeNotifications.ToList() )
            {
                MinimizeNotification(notification.Id);
            }
        }
    }
}
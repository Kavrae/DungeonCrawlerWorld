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
    public class NotificationManager : IGameManager
    {
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
                RelativePosition = new Vector2(15, 40), //once we know this positions correctly, put the map back at 12/12 so it overlaps, make sure this is on top
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
            AddNotification(NotificationType.System, "System message", false);
            AddNotification(NotificationType.System, "System message 2", false);
            AddNotification(NotificationType.System, "System message 3", true);
            AddNotification(NotificationType.Quest, "Quest message 1", true);
            AddNotification(NotificationType.Quest, "Quest message 2", false);
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

        public void AddNotification(NotificationType notificationType, string message, bool isOpen)
        {
            var notification = new Notification(message, notificationType);

            if (isOpen)
            {
                var notificationWindow = new TextWindow(
                        null,
                        new TextWindowOptions
                        {
                            RelativePosition = new Vector2(200, 200), //TODO middle of the screen. Maybe this should be a display mode?
                            CanContainChildWindows = false,
                            DisplayMode = WindowDisplayMode.Grow,
                            IsTransparent = false,
                            ShowBorder = true,
                            ShowTitle = true,
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

        public void CloseNotification(Guid id)
        {
            /*if (_activeNotificationWindows.ContainsKey(id))
            {
                _activeNotificationWindows.Remove(id);
            }
            if (_notifications.Exists(notification => notification.Id == id))
            {
                _notifications.RemoveAll(notification => notification.Id == id);
            }
            */
            //TODO remove from systemNotifications 
        }

        public void OpenNextNotification(NotificationType notificationType)
        {
            //TODO remove from unreadNotificationWindows
            //TODO add to activeNotificationWindows
        }

        public void MinimizeNotification(Guid notificationId)
        {
            //TODO add to unreadNotificationWindows
            //TODO remove from activeNotificationWindows
        }

        public void MinimizeAllNotifications()
        {
            //TODO add to unreadNotificationWindows
            //TODO remove from activeNotificationWindows
        }
    }
}
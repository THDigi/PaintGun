using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;

namespace Digi.PaintGun.Features
{
    public class Notifications : ModComponent
    {
        public const int DEFAULT_TIMEOUT_MS = 200;

        readonly IMyHudNotification[] notifications = new IMyHudNotification[4];

        public Notifications(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }

        public void Show(int line, string text, string font = MyFontEnum.White, int aliveTime = DEFAULT_TIMEOUT_MS)
        {
            if(line < 0 || line >= notifications.Length)
                throw new ArgumentException($"Notification line ({line}) is either negative or above max of {notifications.Length - 1}.");

            if(text == null)
            {
                if(notifications[line] != null)
                    notifications[line].Hide();

                return;
            }

            if(notifications[line] == null)
                notifications[line] = MyAPIGateway.Utilities.CreateNotification(string.Empty);

            notifications[line].Font = font;
            notifications[line].Text = text;
            notifications[line].AliveTime = aliveTime;
            notifications[line].Show();
        }

        public void Hide(int id)
        {
            Show(id, null);
        }
    }
}
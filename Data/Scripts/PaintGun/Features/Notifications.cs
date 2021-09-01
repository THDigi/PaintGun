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

        public void Show(int line, string text, string font = MyFontEnum.Debug, int aliveTime = DEFAULT_TIMEOUT_MS)
        {
            if(line < 0 || line >= notifications.Length)
                throw new ArgumentException($"Notification line ({line.ToString()}) is either negative or above max of {(notifications.Length - 1).ToString()}.");

            IMyHudNotification notify = notifications[line];

            if(text == null)
            {
                if(notify != null)
                    notify.Hide();

                return;
            }

            if(notify == null)
                notify = notifications[line] = MyAPIGateway.Utilities.CreateNotification(string.Empty);

            notify.Hide(); // required since SE v1.194
            notify.Font = font;
            notify.Text = text;
            notify.AliveTime = aliveTime;
            notify.Show();
        }

        public void Hide(int id)
        {
            Show(id, null);
        }
    }
}
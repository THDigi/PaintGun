using System;
using Digi.ComponentLib;
using Sandbox.ModAPI;

namespace Digi.PaintGun.Features
{
    // HACK MyAPIGateway.Session.Player is null for first few update frames, this component is the checker.
    // Bugreport: https://support.keenswh.com/spaceengineers/pc/topic/01-190-101modapi-myapigateway-session-player-is-null-for-first-3-ticks-for-mp-clients
    // This affects GetPlayers() aswell.
    public class CheckPlayerField : ModComponent
    {
        public bool Ready { get; private set; }

        bool TriggerEvent = true;
        public event Action PlayerReady;

        public CheckPlayerField(PaintGunMod main) : base(main)
        {
            UpdateMethods = UpdateFlags.UPDATE_INPUT;
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }

        protected override void UpdateInput(bool anyKeyOrMouse, bool inMenu, bool paused)
        {
            bool hasPlayer = MyAPIGateway.Session.Player != null;
            if(Ready != hasPlayer)
            {
                Ready = hasPlayer;

                if(Ready && TriggerEvent)
                {
                    TriggerEvent = false;
                    PlayerReady?.Invoke();
                }
            }
        }
    }
}
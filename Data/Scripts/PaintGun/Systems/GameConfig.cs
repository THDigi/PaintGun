using System;
using Digi.ComponentLib;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Systems
{
    public enum HudState
    {
        OFF = 0,
        HINTS = 1,
        BASIC = 2
    }

    public class GameConfig : ModComponent
    {
        /// <summary>
        /// Called when HUD is being manually cycled.
        /// </summary>
        public event EventHandlerHudStateChanged HudStateChanged;
        public delegate void EventHandlerHudStateChanged(HudState prevState, HudState state);

        public event EventHandlerGamepadUseChanged GamepadUseChanged;
        public delegate void EventHandlerGamepadUseChanged(bool isUsed);

        /// <summary>
        /// Called when client exits the options menu.
        /// </summary>
        public event Action ClosedOptionsMenu;

        public HudState HudState;
        public float HudBackgroundOpacity;
        public double AspectRatio;
        public bool UsingGamepad;

        public GameConfig(PaintGunMod main) : base(main)
        {
            UpdateMethods = UpdateFlags.UPDATE_AFTER_SIM | UpdateFlags.UPDATE_INPUT;
        }

        protected override void RegisterComponent()
        {
            MyAPIGateway.Gui.GuiControlRemoved += GUIScreenClosed;

            UpdateConfigValues();
        }

        protected override void UnregisterComponent()
        {
            if(!IsRegistered)
                return;

            MyAPIGateway.Gui.GuiControlRemoved -= GUIScreenClosed;
        }

        protected override void UpdateAfterSim(int tick)
        {
            // this is required in the simulation update because hudstate still has the previous value if used in the input update.
            if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.TOGGLE_HUD))
            {
                UpdateHudState();
            }
        }

        protected override void UpdateInput(bool anyKeyOrMouse, bool inMenu, bool paused)
        {
            bool newUsingGamepad = MyAPIGateway.Input.IsJoystickLastUsed;
            if(UsingGamepad != newUsingGamepad)
            {
                UsingGamepad = newUsingGamepad;
                GamepadUseChanged?.Invoke(newUsingGamepad);
            }
        }

        void GUIScreenClosed(object screen)
        {
            try
            {
                if(screen.ToString().EndsWith("OptionsSpace")) // closing options menu just assumes you changed something so it'll re-check config settings
                {
                    UpdateConfigValues();

                    ClosedOptionsMenu?.Invoke();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        void UpdateConfigValues()
        {
            UpdateHudState();

            HudBackgroundOpacity = MyAPIGateway.Session.Config?.HUDBkOpacity ?? 0.6f;

            Vector2 viewportSize = MyAPIGateway.Session.Camera.ViewportSize;
            AspectRatio = (double)viewportSize.X / (double)viewportSize.Y;
        }

        void UpdateHudState()
        {
            HudState prevState = HudState;
            HudState = (HudState)(MyAPIGateway.Session.Config?.HudState ?? (int)HudState.HINTS);
            HudStateChanged?.Invoke(prevState, HudState);
        }
    }
}
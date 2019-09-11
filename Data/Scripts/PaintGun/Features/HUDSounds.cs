using Sandbox.Game.Entities;
using Sandbox.ModAPI;

namespace Digi.PaintGun.Features
{
    public class HUDSounds : ModComponent
    {
        MyEntity3DSoundEmitter soundEmitter;
        int soundTimeout = 0;

        readonly MySoundPair SOUND_HUD_UNABLE = new MySoundPair("HudUnable");
        readonly MySoundPair SOUND_HUD_CLICK = new MySoundPair("HudClick");
        readonly MySoundPair SOUND_HUD_MOUSE_CLICK = new MySoundPair("HudMouseClick");
        readonly MySoundPair SOUND_HUD_COLOR = new MySoundPair("HudColorBlock");
        readonly MySoundPair SOUND_HUD_ITEM = new MySoundPair("HudItem");

        public HUDSounds(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
            soundEmitter?.Cleanup();
            soundEmitter = null;
        }

        public void PlayUnable()
        {
            PlayHudSound(SOUND_HUD_UNABLE, 0.5f, timeout: 60);
        }

        public void PlayClick()
        {
            PlayHudSound(SOUND_HUD_CLICK, 0.25f);
        }

        public void PlayMouseClick()
        {
            PlayHudSound(SOUND_HUD_MOUSE_CLICK, 0.25f);
        }

        public void PlayColor()
        {
            PlayHudSound(SOUND_HUD_COLOR, 0.8f);
        }

        public void PlayItem()
        {
            PlayHudSound(SOUND_HUD_ITEM, 0.6f);
        }

        void PlayHudSound(MySoundPair soundPair, float volume, int timeout = 0)
        {
            if(timeout > 0)
            {
                if(soundTimeout > Main.Tick)
                    return;

                soundTimeout = Main.Tick + timeout;
            }

            if(soundEmitter == null)
                soundEmitter = new MyEntity3DSoundEmitter(null);

            soundEmitter.SetPosition(MyAPIGateway.Session.Camera.WorldMatrix.Translation);
            soundEmitter.CustomVolume = volume;
            soundEmitter.PlaySound(soundPair, stopPrevious: false, alwaysHearOnRealistic: true, force2D: true);
        }
    }
}
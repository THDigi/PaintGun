using Sandbox.Game.Entities;
using Sandbox.ModAPI;

namespace Digi.PaintGun.Features
{
    public class HUDSounds : ModComponent
    {
        MyEntity3DSoundEmitter SoundEmitter;
        int SoundTimeout = 0;

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
            SoundEmitter?.Cleanup();
            SoundEmitter = null;
        }

        public void PlayUnable(bool ignoreSetting = false)
        {
            if(ignoreSetting || Main.Settings.extraSounds)
                PlayHudSound(SOUND_HUD_UNABLE, 0.5f, timeout: 60);
        }

        public void PlayClick(bool ignoreSetting = false)
        {
            if(ignoreSetting || Main.Settings.extraSounds)
                PlayHudSound(SOUND_HUD_CLICK, 0.25f);
        }

        public void PlayMouseClick(bool ignoreSetting = false)
        {
            if(ignoreSetting || Main.Settings.extraSounds)
                PlayHudSound(SOUND_HUD_MOUSE_CLICK, 0.25f);
        }

        public void PlayColor(bool ignoreSetting = false)
        {
            if(ignoreSetting || Main.Settings.extraSounds)
                PlayHudSound(SOUND_HUD_COLOR, 0.8f);
        }

        public void PlayItem(bool ignoreSetting = false)
        {
            if(ignoreSetting || Main.Settings.extraSounds)
                PlayHudSound(SOUND_HUD_ITEM, 0.6f);
        }

        void PlayHudSound(MySoundPair soundPair, float volume, int timeout = 0)
        {
            if(timeout > 0)
            {
                if(SoundTimeout > Main.Tick)
                    return;

                SoundTimeout = Main.Tick + timeout;
            }

            if(SoundEmitter == null)
            {
                SoundEmitter = new MyEntity3DSoundEmitter(null);

                // remove all effects and conditions from this emitter
                SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CanHear].ClearImmediate();
                SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ShouldPlay2D].ClearImmediate();
                SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CueType].ClearImmediate();
                SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ImplicitEffect].ClearImmediate();
            }

            SoundEmitter.SetPosition(MyAPIGateway.Session.Camera.WorldMatrix.Translation);
            SoundEmitter.CustomVolume = volume;
            SoundEmitter.PlaySound(soundPair, stopPrevious: false, alwaysHearOnRealistic: true, force2D: true);
        }
    }
}
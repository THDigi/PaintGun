using System;
using Digi.ComponentLib;
using VRage.Utils;

namespace Digi.PaintGun.Features.Palette
{
    public class PaletteScheduledSync : ModComponent
    {
        int syncDelayTicks;
        int syncCooldownTicks;

        bool syncColor;
        bool syncSkin;
        bool syncApplyColor;
        bool syncApplySkin;

        PlayerInfo LocalInfo => Main.Palette.LocalInfo;

        const int DELAY_BEFORE_SYNC = Constants.TICKS_PER_SECOND / 4; // when a property is changed, this countdown starts and after that it will sync them
        const int COOLDOWN_UNTIL_RESYNC = Constants.TICKS_PER_SECOND / 2; // this cooldown starts after the sync is done and blocks future re-syncs.

        public PaletteScheduledSync(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }

        protected override void UpdateAfterSim(int tick)
        {
            if(syncCooldownTicks > 0 && --syncCooldownTicks > 0)
                return;

            if(syncDelayTicks > 0 && --syncDelayTicks == 0)
            {
                syncCooldownTicks = COOLDOWN_UNTIL_RESYNC;

                int? colorSlot = (syncColor ? (int?)LocalInfo.SelectedColorSlot : null);
                MyStringHash? skin = (syncSkin ? (MyStringHash?)LocalInfo.SelectedSkin : null);
                bool? applyColor = (syncApplyColor ? (bool?)LocalInfo.ApplyColor : null);
                bool? applySkin = (syncApplySkin ? (bool?)LocalInfo.ApplySkin : null);

                Main.NetworkLibHandler.PacketPaletteUpdate.Send(colorSlot, skin, applyColor, applySkin);

                syncColor = false;
                syncSkin = false;
                syncApplyColor = false;
                syncApplySkin = false;
            }

            if(syncDelayTicks == 0 && syncCooldownTicks == 0)
            {
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);
            }
        }

        /// <summary>
        /// NOTE: Parameters are additive until sync, leaving as false does not disable anything.
        /// </summary>
        public void ScheduleSyncFor(bool color = false, bool skin = false, bool applyColor = false, bool applySkin = false)
        {
            if(!color && !skin && !applyColor && !applySkin)
                throw new ArgumentException("At least one parameter needs to be true!");

            if(color)
                syncColor = true;

            if(skin)
                syncSkin = true;

            if(applyColor)
                syncApplyColor = true;

            if(applySkin)
                syncApplySkin = true;

            if(syncDelayTicks == 0)
            {
                syncDelayTicks = DELAY_BEFORE_SYNC;
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);
            }
        }
    }
}
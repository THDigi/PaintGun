using VRage.Game;

namespace Digi.PaintGun
{
    public class Constants : ModComponent
    {
        public const string PAINTGUN_ID = "PaintGun";
        public const string PAINTGUN_WEAPONID = "WeaponPaintGun";
        public const string PAINT_MAGAZINE_ID = "PaintGunMag";

        public const ushort NETWORK_CHANNEL = 9319; // network packet ID used for this mod; must be unique from other mods

        public const int COLOR_PALETTE_SIZE = 14;

        public const int SAFE_ZONE_ACCES_FOR_PAINT = 0x40; // MySafeZoneAction.Building = 0x40

        // DEBUG set these false after testing.
        public static bool DEBUGGING => true;
        public static bool OWNERSHIP_TEST_EXTRA_LOGGING => true;
        public static bool NETWORK_EXTRA_LOGGING => true;

        public readonly MyObjectBuilder_AmmoMagazine PAINT_MAG_ITEM = new MyObjectBuilder_AmmoMagazine()
        {
            SubtypeName = PAINT_MAGAZINE_ID,
            ProjectilesCount = 1
        };

        public const int TICKS_PER_SECOND = 60;

        public Constants(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }
    }
}
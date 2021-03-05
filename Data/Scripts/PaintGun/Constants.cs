using Sandbox.ModAPI;
using VRage.Game;

namespace Digi.PaintGun
{
    public class Constants : ModComponent
    {
        public const string PAINTGUN_ID = "PaintGun";
        public const string PAINTGUN_PHYSITEMID = "PhysicalPaintGun";
        public const string PAINTGUN_WEAPONID = "WeaponPaintGun";
        public const string PAINT_MAGAZINE_ID = "PaintGunMag";

        public readonly MyObjectBuilder_AmmoMagazine PAINT_MAG_ITEM = new MyObjectBuilder_AmmoMagazine()
        {
            SubtypeName = PAINT_MAGAZINE_ID,
            ProjectilesCount = 1
        };

        public const ushort NETWORK_CHANNEL = 9319; // network packet ID used for this mod; must be unique from other mods

        public const int COLOR_PALETTE_SIZE = 14;

        public static readonly object SAFE_ZONE_ACCES_FOR_PAINT = (object)0x40; // MySafeZoneAction.Building = 0x40

        public static bool DEBUG_COMPONENT => false;
        public static bool SKIN_INIT_LOGGING => true;
        public static bool OWNERSHIP_TEST_LOGGING => true;
        public static bool NETWORK_ACTION_LOGGING => true;
        public static bool NETWORK_DESYNC_ERROR_LOGGING => false; // MyAPIGateway.Multiplayer.IsServer;

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
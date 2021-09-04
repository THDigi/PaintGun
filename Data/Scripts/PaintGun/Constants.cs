using VRage.Game;
using VRage.Input;

namespace Digi.PaintGun
{
    public class Constants : ModComponent
    {
        public const string PAINTGUN_ID = "PaintGun";
        public const string PAINTGUN_PHYSITEMID = "PhysicalPaintGun";
        public const string PAINTGUN_WEAPONID = "WeaponPaintGun";
        public const string PAINT_MAG_SUBTYPEID = "PaintGunMag";

        public readonly MyDefinitionId PAINT_MAG_ID = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), PAINT_MAG_SUBTYPEID);

        public readonly MyObjectBuilder_AmmoMagazine PAINT_MAG_ITEM = new MyObjectBuilder_AmmoMagazine()
        {
            SubtypeName = PAINT_MAG_SUBTYPEID,
            ProjectilesCount = 1
        };

        public const ushort NETWORK_CHANNEL = 9320; // network packet ID used for this mod; must be unique from other mods

        public const int COLOR_PALETTE_SIZE = 14;

        public static readonly object SAFE_ZONE_ACCES_FOR_PAINT = (object)0x40; // MySafeZoneAction.Building = 0x40

        public static bool EnableTestComponent = false;
        public static bool SKIN_INIT_LOGGING = true;
        public static bool OWNERSHIP_TEST_LOGGING = true;
        public static bool NETWORK_ACTION_LOGGING = true;
        public static bool NETWORK_DESYNC_ERROR_LOGGING = false; // MyAPIGateway.Multiplayer.IsServer;

        public const int TICKS_PER_SECOND = 60;

        public const MyJoystickAxesEnum GamepadBind_Paint = MyJoystickAxesEnum.Zneg; // RT
        public const MyJoystickAxesEnum GamepadBind_DeepPaintMode = MyJoystickAxesEnum.Zpos; // LT; can't be changed as it's the internal bind for ironsight which is used for deep paint mode
        public const MyJoystickButtonsEnum GamepadBind_CyclePalette = MyJoystickButtonsEnum.J03; // X
        public const MyJoystickButtonsEnum GamepadBind_CycleSkinsModifier = MyJoystickButtonsEnum.J05; // LB

        public string GamepadBindName_Paint = string.Empty;
        public string GamepadBindName_DeepPaintMode = string.Empty;
        public string GamepadBindName_CycleColors = string.Empty;
        public string GamepadBindName_CycleSkins = string.Empty;

        public Constants(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
            GamepadBindName_Paint = InputHandler.xboxCodes[GamepadBind_Paint].ToString();
            GamepadBindName_DeepPaintMode = InputHandler.xboxCodes[GamepadBind_DeepPaintMode].ToString();
            GamepadBindName_CycleColors = InputHandler.xboxCodes[GamepadBind_CyclePalette].ToString();
            GamepadBindName_CycleSkins = $"{InputHandler.xboxCodes[GamepadBind_CycleSkinsModifier]}+{InputHandler.xboxCodes[GamepadBind_CyclePalette]}";
        }

        protected override void UnregisterComponent()
        {
        }
    }
}
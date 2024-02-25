using Digi.ComponentLib;
using Digi.PaintGun.Features;
using Digi.PaintGun.Features.ChatCommands;
using Digi.PaintGun.Features.ConfigMenu;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Features.Sync;
using Digi.PaintGun.Features.Testing;
using Digi.PaintGun.Features.Tool;
using Digi.PaintGun.Systems;
using Digi.PaintGun.Utilities;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;

// TODO: material painting mode (skin+color in one slot)
// TODO: mode for slight randomization of final color
// TODO: design for more modes in the future?
// TODO: replace color on other blocks with same type+subtype... or some other categorization
// TODO: opt-in to prevent blocks placed pre-colored? or maybe an option for "disable vanilla paint" mod...

namespace Digi.PaintGun
{
    public class PaintGunMod : ModBase<PaintGunMod>
    {
        public const string MOD_NAME = "Paint Gun";

        // Systems
        public Caches Caches;
        public Constants Constants;
        public TextAPI TextAPI;
        public DrawUtils DrawUtils;
        public GameConfig GameConfig;
        public PlayerHandler PlayerHandler;
        public NetworkLibHandler NetworkLibHandler;

        // Features
        public Palette Palette;
        public Painting Painting;
        public CheckPlayerField CheckPlayerField;
        public Settings Settings;
        public ServerSettings ServerSettings;
        public HUDSounds HUDSounds;
        public PaletteInputHandler PaletteInputHandler;
        public PaletteScheduledSync PaletteScheduledSync;
        public LocalToolHandler LocalToolHandler;
        public LocalToolDescription LocalToolDescription;
        public PaletteHUD PaletteHUD;
        public SelectionGUI SelectionGUI;
        public ToolHandler ToolHandler;
        public ChatCommands ChatCommands;
        public Notifications Notifications;
        public ColorPickerGUIWarning ColorPickerGUIWarning;
        public ConfigMenuHandler ConfigMenuHandler;
        public TestComp TestComp;

        // Rights checkers, can be used both serverside and clientside

        public bool AccessReplaceColor(ulong? steamId) => ServerSettings.ReplacePaintSurvival || Utils.IsCreativeToolOrMode(steamId);
        public readonly string ReplaceColorAccessInfo = "Replace color mode is only available in creative game mode or with SM creative tools on.";

        public bool AccessSymmetry(ulong? steamId) => Utils.IsCreativeToolOrMode(steamId);

        public bool AccessInstantPaint(ulong? steamId) => ServerSettings.PaintSpeedMultiplier == 0 || Utils.IsCreativeToolOrMode(steamId);

        public bool AccessRequiresAmmo(ulong? steamId) => ServerSettings.RequireAmmo && !MyAPIGateway.Session.CreativeMode && !MyAPIGateway.Session.SessionSettings.InfiniteAmmo;

        /// <summary>
        /// Returns null if it's allowed, otherwise returns a string with the reason.
        /// </summary>
        public string CanPaintBlock(IMySlimBlock block, ulong? steamId)
        {
            if(ServerSettings.PaintUnfinishedBlocks)
                return null;

            if(AccessInstantPaint(steamId))
                return null;

            MyCubeBlockDefinition def = (MyCubeBlockDefinition)block.BlockDefinition;

            // used as $"Block is {reason}"

            if(!block.ComponentStack.IsBuilt)
                return "unfinished";

            const float DamageRatioDisallowPaint = 0.1f;
            if(block.CurrentDamage > (block.MaxIntegrity * DamageRatioDisallowPaint))
                return "damaged";

            return null;
        }

        public PaintGunMod(PaintGun_GameSession session) : base(MOD_NAME, session)
        {
            string msg = "### PaintGun v24";
            VRage.Utils.MyLog.Default.WriteLineAndConsole(msg);
            Log.Info(msg);

            session.SetUpdateOrder(MyUpdateOrder.AfterSimulation); // define what update types the modAPI session comp should trigger

            DisablePaintGunVanillaShoot();

            // Systems
            Caches = new Caches(this);
            Constants = new Constants(this);
            PlayerHandler = new PlayerHandler(this);
            NetworkLibHandler = new NetworkLibHandler(this);

            if(IsPlayer)
            {
                TextAPI = new TextAPI(this);
                DrawUtils = new DrawUtils(this);
                GameConfig = new GameConfig(this);
            }

            // Features
            ServerSettings = new ServerSettings(this);
            Palette = new Palette(this);
            Painting = new Painting(this);

            if(IsPlayer)
            {
                CheckPlayerField = new CheckPlayerField(this);
                Settings = new Settings(this);
                PaletteInputHandler = new PaletteInputHandler(this);
                PaletteScheduledSync = new PaletteScheduledSync(this);
                HUDSounds = new HUDSounds(this);
                LocalToolHandler = new LocalToolHandler(this);
                LocalToolDescription = new LocalToolDescription(this);
                SelectionGUI = new SelectionGUI(this);
                ToolHandler = new ToolHandler(this);
                ChatCommands = new ChatCommands(this);
                Notifications = new Notifications(this);
                PaletteHUD = new PaletteHUD(this);
                ColorPickerGUIWarning = new ColorPickerGUIWarning(this);
                ConfigMenuHandler = new ConfigMenuHandler(this);
            }

            if(Constants.EnableTestComponent)
                TestComp = new TestComp(this);
        }

        void DisablePaintGunVanillaShoot()
        {
            // make the paintgun (which is a rifle) not be able to shoot normally, to avoid needing to add ammo back and skips that stupid hardcoded screen shake
            MyWeaponDefinition weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(new MyDefinitionId(typeof(MyObjectBuilder_WeaponDefinition), Constants.PAINTGUN_WEAPONID));

            for(int i = 0; i < weaponDef.WeaponAmmoDatas.Length; i++)
            {
                MyWeaponDefinition.MyWeaponAmmoData ammoData = weaponDef.WeaponAmmoDatas[i];
                if(ammoData != null)
                    ammoData.ShootIntervalInMiliseconds = int.MaxValue;
            }
        }
    }
}
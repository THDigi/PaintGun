using Digi.ComponentLib;
using Digi.PaintGun.Features;
using Digi.PaintGun.Features.ChatCommands;
using Digi.PaintGun.Features.Debug;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Features.SkinOwnershipTest;
using Digi.PaintGun.Features.Sync;
using Digi.PaintGun.Features.Tool;
using Digi.PaintGun.Systems;
using Digi.PaintGun.Utilities;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;

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
        public HUDSounds HUDSounds;
        public PaletteInputHandler PaletteInputHandler;
        public PaletteScheduledSync PaletteScheduledSync;
        public LocalToolHandler LocalToolHandler;
        public PaletteHUD PaletteHUD;
        public SelectionGUI SelectionGUI;
        public ToolHandler ToolHandler;
        public ChatCommands ChatCommands;
        public Notifications Notifications;
        public SkinTestServer OwnershipTestServer;
        public SkinTestPlayer OwnershipTestPlayer;
        public ColorPickerGUIWarning ColorPickerGUIWarning;
        public DebugComp Debug;

        // Rights
        public bool IgnoreAmmoConsumption => (MyAPIGateway.Session.CreativeMode || MyAPIGateway.Session.SessionSettings.InfiniteAmmo); // NOTE: checked both clientside (visually) and serverside (functionally)
        public bool InstantPaintAccess => (MyAPIGateway.Session.CreativeMode || Utils.CreativeToolsEnabled);
        public bool ReplaceColorAccess => (MyAPIGateway.Session.CreativeMode || Utils.CreativeToolsEnabled);
        public bool SymmetryAccess => (MyAPIGateway.Session.CreativeMode || Utils.CreativeToolsEnabled);

        public string ReplaceColorAccessInfo => "Replace color mode is only available in creative game mode or with SM creative tools on.";

        public PaintGunMod(PaintGun_GameSession session) : base(MOD_NAME, session)
        {
            var msg = "### PaintGun v21";
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
            Palette = new Palette(this);
            Painting = new Painting(this);

            if(IsServer)
            {
                OwnershipTestServer = new SkinTestServer(this);
            }

            if(IsPlayer)
            {
                CheckPlayerField = new CheckPlayerField(this);
                Settings = new Settings(this);
                PaletteInputHandler = new PaletteInputHandler(this);
                PaletteScheduledSync = new PaletteScheduledSync(this);
                HUDSounds = new HUDSounds(this);
                LocalToolHandler = new LocalToolHandler(this);
                SelectionGUI = new SelectionGUI(this);
                ToolHandler = new ToolHandler(this);
                ChatCommands = new ChatCommands(this);
                Notifications = new Notifications(this);
                OwnershipTestPlayer = new SkinTestPlayer(this);
                PaletteHUD = new PaletteHUD(this);
                ColorPickerGUIWarning = new ColorPickerGUIWarning(this);
            }

            if(Constants.DEBUG_COMPONENT)
                Debug = new DebugComp(this);
        }

        void DisablePaintGunVanillaShoot()
        {
            // make the paintgun (which is a rifle) not be able to shoot normally, to avoid needing to add ammo back and skips that stupid hardcoded screen shake
            var weaponDef = MyDefinitionManager.Static.GetWeaponDefinition(new MyDefinitionId(typeof(MyObjectBuilder_WeaponDefinition), Constants.PAINTGUN_WEAPONID));

            for(int i = 0; i < weaponDef.WeaponAmmoDatas.Length; i++)
            {
                var ammoData = weaponDef.WeaponAmmoDatas[i];
                if(ammoData != null)
                    ammoData.ShootIntervalInMiliseconds = int.MaxValue;
            }
        }
    }
}
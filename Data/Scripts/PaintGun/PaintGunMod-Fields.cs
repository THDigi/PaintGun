using System.Collections.Generic;
using Digi.PaintGun.SkinOwnershipTester;
using Draygo.API;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

// HACK allows the use of these whitelisted enums without triggering prohibited issues with accessing their parent classes
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Digi.PaintGun
{
    public partial class PaintGunMod
    {
        public static PaintGunMod instance = null;

        public static bool DEBUG => false;
        public static bool UIEDIT => false;

        public bool init = false;
        public bool isDS = false;
        public bool playerObjectFound = false;
        public uint tick = 0;
        public Settings settings = null;
        public bool gameHUD = true;
        public float gameHUDBkOpacity = 1f;
        public bool TextAPIReady = false;
        public OwnershipTestPlayer ownershipTestPlayer;
        public OwnershipTestServer ownershipTestServer;

        private UIEdit uiEdit;
        private HudAPIv2 textAPI;
        private bool viewProjInvCompute = true;
        private MatrixD viewProjInvCache;
        private double aspectRatio;
        private bool textAPIvisible = false;

        private bool CreativeTools => MyAPIGateway.Session.EnableCopyPaste; // HACK Session.EnableCopyPaste used as spacemaster check
        public bool IgnoreAmmoConsumption => (MyAPIGateway.Session.CreativeMode || MyAPIGateway.Session.SessionSettings.InfiniteAmmo);
        public bool InstantPaintAccess => (MyAPIGateway.Session.CreativeMode || CreativeTools);
        public bool ReplaceColorAccess => (MyAPIGateway.Session.CreativeMode || CreativeTools);
        public bool SymmetryAccess => (MyAPIGateway.Session.CreativeMode || CreativeTools);

        public bool triggerInputPressed = false;
        public bool colorPickInputPressed = false;
        public bool colorPickModeInputPressed = false;
        public bool replaceAllModeInputPressed = false;
        public bool colorPickMode = false;
        public bool replaceAllMode = false;
        public bool replaceGridSystem = false;
        public long replaceGridSystemTimeout = 0;
        public bool symmetryInputAvailable = false;
        public string symmetryStatus = null;
        public MyCubeGrid selectedGrid = null;
        public IMySlimBlock selectedSlimBlock = null;
        public IMyPlayer selectedPlayer = null;
        public Vector3 selectedPlayerColorMask;
        public MyStringHash selectedPlayerBlockSkin;
        public bool selectedInvalid = false;
        public Vector3 prevColorMaskPreview;
        public IMySlimBlock prevSlimBlock = null;
        private MyEntity3DSoundEmitter hudSoundEmitter;
        private uint hudSoundTimeout = 0;
        public string[] blockInfoStatus = new string[3];
        public IMyHudNotification[] toolStatus = new IMyHudNotification[3];
        public MyHudBlockInfo.ComponentInfo[] blockInfoLines = new MyHudBlockInfo.ComponentInfo[]
        {
            new MyHudBlockInfo.ComponentInfo(),
            new MyHudBlockInfo.ComponentInfo(),
            new MyHudBlockInfo.ComponentInfo(),
            new MyHudBlockInfo.ComponentInfo(),
        };

        private int prevSelectedColorSlot = 0;
        private int prevSelectedSkinIndex = 0;

        public PaintGunItem localHeldTool = null;
        public PlayerColorData localColorData = null;
        public List<SkinInfo> BlockSkins;
        public int OwnedSkins = 0;
        public readonly Dictionary<ulong, PlayerColorData> playerColorData = new Dictionary<ulong, PlayerColorData>();
        public readonly HashSet<ulong> playersColorPickMode = new HashSet<ulong>();

        public const string MOD_NAME = "PaintGun";
        public const ulong WORKSHOP_ID = 500818376;
        public const string PAINTGUN_ID = "PaintGun";
        public const string PAINTGUN_WEAPONID = "WeaponPaintGun";
        public const string PAINT_MAGAZINE_ID = "PaintGunMag";
        public const float PAINT_SPEED = 0.5f;
        public const float DEPAINT_SPEED = 0.6f;
        public const int PAINT_SKIP_TICKS = 6; // 10 times a second
        public const int COLOR_PALETTE_SIZE = 14;
        public const float SAME_COLOR_RANGE = 0.001f;
        public const int TOOLSTATUS_TIMEOUT = 200;
        public const ushort PACKET = 9319; // network packet ID used for this mod; must be unique from other mods
        public const float COLOR_EPSILON = 0.000001f;

        public Vector3 DEFAULT_COLOR = new Vector3(0, -1, 0);

        public const float SOUND_HUD_UNABLE_VOLUME = 0.5f;
        public const uint SOUND_HUD_UNABLE_TIMEOUT = 60;
        public readonly MySoundPair SOUND_HUD_UNABLE = new MySoundPair("HudUnable");
        public readonly MySoundPair SOUND_HUD_CLICK = new MySoundPair("HudClick");
        public readonly MySoundPair SOUND_HUD_MOUSE_CLICK = new MySoundPair("HudMouseClick");
        public readonly MySoundPair SOUND_HUD_COLOR = new MySoundPair("HudColorBlock");
        public readonly MySoundPair SOUND_HUD_ITEM = new MySoundPair("HudItem");

        public readonly MyStringId MATERIAL_GIZMIDRAWLINE = MyStringId.GetOrCompute("GizmoDrawLine");
        public readonly MyStringId MATERIAL_GIZMIDRAWLINERED = MyStringId.GetOrCompute("GizmoDrawLineRed");
        public readonly MyStringId MATERIAL_VANILLA_SQUARE = MyStringId.GetOrCompute("Square");
        public readonly MyStringId MATERIAL_PALETTE_COLOR = MyStringId.GetOrCompute("PaintGunPalette_Color");
        public readonly MyStringId MATERIAL_PALETTE_SELECTED = MyStringId.GetOrCompute("PaintGunPalette_Selected");
        public readonly MyStringId MATERIAL_PALETTE_BACKGROUND = MyStringId.GetOrCompute("PaintGunPalette_Background");
        public readonly MyStringId MATERIAL_ICON_GENERIC_BLOCK = MyStringId.GetOrCompute("PaintGunIcon_GenericBlock");
        public readonly MyStringId MATERIAL_ICON_GENERIC_CHARACTER = MyStringId.GetOrCompute("PaintGunIcon_GenericCharacter");
        public readonly MyStringId MATERIAL_ICON_PAINT_AMMO = MyStringId.GetOrCompute("PaintGunBlockIcon_PaintAmmo");
        public readonly MyStringId MATERIAL_WHITEDOT = MyStringId.GetOrCompute("WhiteDot");

        public const BlendTypeEnum HELPERS_BLEND_TYPE = BlendTypeEnum.SDR;
        public const BlendTypeEnum SPRAY_BLEND_TYPE = BlendTypeEnum.SDR;

        public readonly MySoundPair SPRAY_SOUND = new MySoundPair("PaintGunSpray");
        public readonly MyStringId MATERIAL_SPRAY = MyStringId.GetOrCompute("PaintGun_Spray");
        public readonly List<PaintGunItem> ToolDraw = new List<PaintGunItem>();

        public readonly MyObjectBuilder_AmmoMagazine PAINT_MAG = new MyObjectBuilder_AmmoMagazine()
        {
            SubtypeName = PAINT_MAGAZINE_ID,
            ProjectilesCount = 1
        };

        public readonly List<IMyPlayer> players = new List<IMyPlayer>();
        private readonly HashSet<MyCubeGrid> gridsInSystemCache = new HashSet<MyCubeGrid>();

        private readonly PacketData packetUpdateColorList = new PacketData()
        {
            Type = PacketAction.UPDATE_COLOR_LIST,
            PackedColors = new Color[COLOR_PALETTE_SIZE],
        };

        private readonly PacketData packetPaintGunFiring = new PacketData();

        private readonly PacketData packetConsumeAmmo = new PacketData()
        {
            Type = PacketAction.CONSUME_AMMO,
        };

        private readonly PacketData packetPaint = new PacketData()
        {
            Type = PacketAction.PAINT_BLOCK,
        };

        private readonly PacketData packetReplaceColor = new PacketData()
        {
            Type = PacketAction.BLOCK_REPLACE_COLOR,
        };

        private readonly PacketData packetSelectedSlots = new PacketData()
        {
            Type = PacketAction.SELECTED_SLOTS,
        };

        private readonly PacketData packetColorPickMode = new PacketData();

        private readonly PacketData packetSetColor = new PacketData()
        {
            Type = PacketAction.SET_COLOR,
        };

        private readonly PacketData packetRequestColorList = new PacketData()
        {
            Type = PacketAction.REQUEST_COLOR_LIST,
        };

        private readonly PacketData packetUpdateColor = new PacketData()
        {
            Type = PacketAction.UPDATE_COLOR,
        };
    }
}
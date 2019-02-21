using System.Collections.Generic;
using System.Text;
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

        public bool init = false;
        public bool isPlayer = false;
        public bool playerObjectFound = false;
        public uint tick = 0;
        public Settings settings = null;
        public bool gameHUD = true;
        public float gameHUDBkOpacity = 1f;

        public bool GUIUsed => (textAPI != null && textAPI.Heartbeat);

        private HudAPIv2 textAPI;
        private HudAPIv2.HUDMessage titleObject;
        private HudAPIv2.HUDMessage textObject;
        private bool viewProjInvCompute = true;
        private MatrixD viewProjInvCache;
        private double aspectRatio;
        private bool textAPIvisible = false;

        // HACK Session.EnableCopyPaste used as spacemaster check
        public bool IgnoreAmmoConsumption => MyAPIGateway.Session.CreativeMode;
        public bool InstantPaintAccess => (MyAPIGateway.Session.CreativeMode || MyAPIGateway.Session.EnableCopyPaste);
        public bool ReplaceColorAccess => (MyAPIGateway.Session.CreativeMode || MyAPIGateway.Session.EnableCopyPaste);
        public bool SymmetryAccess => (MyAPIGateway.Session.CreativeMode || MyAPIGateway.Session.EnableCopyPaste);

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
        public bool selectedInvalid = false;
        public Vector3 prevColorMaskPreview;
        public IMySlimBlock prevSlimBlock = null;
        public long selectedBlockBuiltBy = 0;
        public MyEntity3DSoundEmitter emitter;
        public string[] blockInfoStatus = new string[2];
        public IMyHudNotification[] toolStatus = new IMyHudNotification[3];
        public MyHudBlockInfo.ComponentInfo[] blockInfoLines = new MyHudBlockInfo.ComponentInfo[]
        {
            new MyHudBlockInfo.ComponentInfo(),
            new MyHudBlockInfo.ComponentInfo(),
            new MyHudBlockInfo.ComponentInfo(),
            new MyHudBlockInfo.ComponentInfo(),
        };

        private int prevSelectedColorSlot = 0;

        public PaintGunItem localHeldTool = null;
        public PlayerColorData localColorData = null;
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

        private const float ASPECT_RATIO_54_FIX = 0.938f;
        private readonly Vector2 BLOCKINFO_POSITION = new Vector2(0.98f, -0.8f); // bottom-right of the box
        private readonly Vector2 BLOCKINFO_SIZE = new Vector2(0.02f, 0.0142f);
        private readonly Vector3D BLOCKINFO_ICONS_OFFSET = new Vector3D(-0.0178, 0.03, 0.0124);
        private readonly Vector2D BLOCKINFO_TITLE_OFFSET = new Vector2D(-0.42, 0.527);
        private readonly Vector2D BLOCKINFO_TEXT_OFFSET = new Vector2D(-0.371, 0.41);
        private const float BLOCKINFO_BAR_OFFSET_X = -0.0065f;
        private const float BLOCKINFO_BAR_WIDTH = 0.0048f;
        private const float BLOCKINFO_BAR_HEIGHT_SCALE = 0.935f;
        private const float BLOCKINFO_TITLE_SCALE = 1.55f;
        private const float BLOCKINFO_TEXT_SCALE = 1.25f;
        private const float BLOCKINFO_ICON_SIZE = 0.0035f;
        private readonly Color BLOCKINFO_TITLE_BG_COLOR = new Vector4(0.20784314f, 0.266666681f, 0.298039228f, 1f);
        private readonly Color BLOCKINFO_LOWER_BG_COLOR = new Vector4(0.13333334f, 0.180392161f, 0.203921571f, 1f) * 0.9f;
        private readonly Color BLOCKINFO_BAR_BG_COLOR = new Vector4(0.266666681f, 0.3019608f, 0.3372549f, 0.9f);
        private readonly Color BLOCKINFO_BAR_COLOR = new Vector4(0.478431374f, 0.549019635f, 0.6039216f, 1f);
        private const BlendTypeEnum FOREGROUND_BLEND_TYPE = BlendTypeEnum.SDR;
        private const BlendTypeEnum BACKGROUND_BLEND_TYPE = BlendTypeEnum.Standard;

        public readonly List<IMyPlayer> players = new List<IMyPlayer>();
        private readonly StringBuilder assigned = new StringBuilder();
        private readonly HashSet<MyCubeGrid> gridsInSystemCache = new HashSet<MyCubeGrid>();
    }
}
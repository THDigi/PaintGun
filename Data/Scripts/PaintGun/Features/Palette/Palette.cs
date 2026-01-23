using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Digi.ComponentLib;
using Digi.PaintGun.Utilities;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using VRageRender.Messages;

namespace Digi.PaintGun.Features.Palette
{
    public class Palette : ModComponent
    {
        const int CHECK_PALETTE_PASSIVE_TICKS = Constants.TICKS_PER_SECOND * 1;
        const int PLAYER_INFO_CLEANUP_TICKS = Constants.TICKS_PER_SECOND * 60 * 5;

        const string ARMOR_SUFFIX = "_Armor";
        const string TEST_ARMOR_SUBTYPE = "TestArmor";

        const string SKIN_ICON_PREFIX = "PaintGun_SkinIcon_";
        const string SKIN_ICON_UNKNOWN = SKIN_ICON_PREFIX + "Unknown";

        public SortedDictionary<MyStringHash, SkinInfo> Skins;

        class SkinSorter : IComparer<MyStringHash>
        {
            public int Compare(MyStringHash a, MyStringHash b) => a.String.CompareTo(b.String);
        }

        public List<SkinInfo> SkinsForHUD;
        public bool HasAnySkin { get; private set; }

        public PlayerInfo LocalInfo;
        public bool ReplaceMode = false;
        public bool ReplaceShipWide = false;
        public bool ColorPickMode
        {
            get { return LocalInfo.ColorPickMode; }
            set { LocalInfo.ColorPickMode = value; }
        }

        public IEnumerable<object> OwnedSkins { get; private set; }

        public Vector3 DefaultColorMask = new Vector3(0, -1, 0);

        public Dictionary<ulong, PlayerInfo> PlayerInfo = new Dictionary<ulong, PlayerInfo>();

        bool LogDLCInstalledEvent = false; // need to skip early events as it triggers for every single DLC and gets spammy

        public Palette(PaintGunMod main) : base(main)
        {
            MyAPIGateway.DLC.DLCInstalled += DLCInstalled;

            InitBlockSkins();
        }

        protected override void RegisterComponent()
        {
            if(Main.IsPlayer)
            {
                UpdateMethods = UpdateFlags.UPDATE_AFTER_SIM;

                LocalInfo = GetOrAddPlayerInfo(MyAPIGateway.Multiplayer.MyId);
                Main.CheckPlayerField.PlayerReady += PlayerReady;
                Main.PlayerHandler.PlayerDisconnected += PlayerDisconnected;
                Main.Settings.SettingsChanged += SettingsChanged;

                CheckDLCs("world join");
            }

            LogDLCInstalledEvent = true;
        }

        protected override void UnregisterComponent()
        {
            if(MyAPIGateway.DLC != null)
                MyAPIGateway.DLC.DLCInstalled -= DLCInstalled;

            if(!IsRegistered)
                return;

            if(Main.IsPlayer)
            {
                Main.CheckPlayerField.PlayerReady -= PlayerReady;
                Main.PlayerHandler.PlayerDisconnected -= PlayerDisconnected;
                Main.Settings.SettingsChanged -= SettingsChanged;
            }
        }

        public bool ValidateSkinOwnership(MyStringHash? skinId, ulong steamId, bool notifySender = true)
        {
            if(skinId.HasValue && MyAPIGateway.Multiplayer.IsServer)
            {
                SkinInfo skinInfo = GetSkinInfo(skinId.Value);
                if(skinInfo == null)
                {
                    Main.NetworkLibHandler.PacketWarningMessage.Send(steamId, $"Failed to apply skin server side, skin {skinId.Value.String} does not exist.");
                    return false;
                }
                else if(!MyAPIGateway.DLC.HasDefinitionDLC(skinInfo.Definition, steamId))
                {
                    Main.NetworkLibHandler.PacketWarningMessage.Send(steamId, $"Failed to apply skin server side, skin {skinId.Value.String} not owned.");
                    return false;
                }
            }
            return true;
        }

        void PlayerDisconnected(IMyPlayer player)
        {
            PlayerInfo.Remove(player.SteamUserId);
        }

        void PlayerReady()
        {
            DefaultColorMask = Utils.ColorMaskNormalize(MyAPIGateway.Session.Player.DefaultBuildColorSlots.ItemAt(0));
            LocalInfo.SelectedColorSlot = MyAPIGateway.Session.Player.SelectedBuildColorSlot;
            UpdatePalette();

            // broadcast local player's palette to everyone and server sends everyone's palettes back.
            Main.NetworkLibHandler.PacketJoinSharePalette.Send(LocalInfo);
        }

        void DLCInstalled(ulong steamId, uint dlcId)
        {
            try
            {
                if(MyAPIGateway.Multiplayer.MyId == steamId)
                {
                    CheckDLCs("DLCInstalled event");
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        void CheckDLCs(string reason)
        {
            foreach(SkinInfo skin in Skins.Values)
            {
                if(skin.AlwaysOwned || skin.Definition == null)
                    continue;

                skin.LocallyOwned = MyAPIGateway.DLC.HasDefinitionDLC(skin.Definition, MyAPIGateway.Multiplayer.MyId);
            }

            if(LogDLCInstalledEvent)
                Log.Info($"Rechecking owned skins, reason: {reason}\nSkins not owned: {string.Join(",", Skins.Values.Where(s => !s.LocallyOwned).Select(s => s.Name))}");
        }

        void InitBlockSkins()
        {
            if(Constants.SKIN_INIT_LOGGING)
                Log.Info("Finding block skins...");

            HashSet<string> definedIcons = new HashSet<string>();
            foreach(MyTransparentMaterialDefinition def in MyDefinitionManager.Static.GetTransparentMaterialDefinitions())
            {
                if(def.Id.SubtypeName.StartsWith(SKIN_ICON_PREFIX))
                    definedIcons.Add(def.Id.SubtypeName);
            }

            int foundSkins = 0;
            foreach(MyAssetModifierDefinition assetDef in MyDefinitionManager.Static.GetAssetModifierDefinitions())
            {
                if(IsBlockSkin(assetDef))
                    foundSkins++;
            }

            Dictionary<string, MyObjectBuilder_AssetModifierDefinition> vanillaSkins = new Dictionary<string, MyObjectBuilder_AssetModifierDefinition>();

            {
                string path = Path.Combine(MyAPIGateway.Utilities.GamePaths.ContentPath, @"Data\AssetModifiers\ArmorModifiers.sbc");

                MyObjectBuilder_Definitions definitions;
                if(MyObjectBuilderSerializer.DeserializeXML(path, out definitions))
                {
                    if(definitions?.AssetModifiers != null)
                    {
                        foreach(MyObjectBuilder_AssetModifierDefinition def in definitions.AssetModifiers)
                        {
                            vanillaSkins[def.Id.SubtypeId] = def;
                        }

                        Log.Info($"Confirmed {vanillaSkins.Count} vanilla skins.");
                    }
                    else
                    {
                        Log.Error("Game's ArmorModifiers.sbc does not declare any skins!");
                    }
                }
                else
                    Log.Error("Game's ArmorModifiers.sbc does not exist!");
            }

            int capacity = foundSkins + 1; // include "No Skin" too.
            Skins = new SortedDictionary<MyStringHash, SkinInfo>(new SkinSorter());
            SkinsForHUD = new List<SkinInfo>(capacity);

            StringBuilder sb = new StringBuilder(256);

            foreach(MyAssetModifierDefinition assetDef in MyDefinitionManager.Static.GetAssetModifierDefinitions())
            {
                if(IsBlockSkin(assetDef))
                {
                    MyObjectBuilder_AssetModifierDefinition vanillaDef;
                    vanillaSkins.TryGetValue(assetDef.Id.SubtypeId.String, out vanillaDef);

                    //bool isCustomSkin = (!assetDef.Context.IsBaseGame && (assetDef.DLCs == null || assetDef.DLCs.Length == 0));
                    bool isCustomSkin = !assetDef.Context.IsBaseGame && vanillaDef == null;
                    bool requiresDLC = assetDef.DLCs != null && assetDef.DLCs.Length > 0;

                    if(isCustomSkin && requiresDLC)
                    {
                        Log.Error($"Warning: '{assetDef.Id.SubtypeName}' from {assetDef.Context.GetModName()} requires DLCs. Mod-added skins don't support requiring DLCs... yet. Let me know if you need this and I'll try and get it implemented.");
                        continue;
                    }

                    if(!assetDef.Context.IsBaseGame && vanillaDef != null && vanillaDef.DLCs != null && vanillaDef.DLCs.Length > 0
                    && (assetDef.DLCs == null || assetDef.DLCs[0] != vanillaDef.DLCs[0]))
                    {
                        Log.Error($"Warning: '{assetDef.Id.SubtypeName}' is a vanilla skin that had its DLCs modded out by {assetDef.Context.GetModName()}, refusing to use.");
                        continue;
                    }

                    #region Generate user friendly name 
                    string name = assetDef.Id.SubtypeName;
                    sb.Clear();
                    sb.Append(name);

                    if(name.EndsWith(ARMOR_SUFFIX))
                        sb.Length -= ARMOR_SUFFIX.Length;

                    string nameId = sb.ToString();

                    // Add spaces before upper case letters except first.
                    // Or replace _ with space.
                    for(int i = 1; i < sb.Length; ++i)
                    {
                        char c = sb[i];

                        if(c == '_')
                            sb[i] = ' ';

                        if(char.IsUpper(c) && sb[i - 1] != ' ')
                            sb.Insert(i, ' ');
                    }

                    name = sb.ToString();
                    #endregion

                    string icon = SKIN_ICON_PREFIX + nameId;

                    if(!definedIcons.Contains(icon))
                    {
                        if(isCustomSkin || Utils.IsLocalMod())
                            Log.Error($"'{icon}' not found in transparent materials definitions (skin from {assetDef.Context.GetModName()}).", Log.PRINT_MESSAGE);

                        icon = SKIN_ICON_UNKNOWN;
                    }

                    if(isCustomSkin && !FixModTexturePaths(assetDef))
                    {
                        continue;
                    }

                    SkinInfo skinInfo = new SkinInfo(assetDef, name, icon);
                    Skins[skinInfo.SubtypeId] = skinInfo;

                    // HACK: this skin isn't colorable, so might as well make it ignore color palette like gold and silver
                    if(assetDef.Id.SubtypeId.String == "RustNonColorable_Armor")
                    {
                        assetDef.DefaultColor = Color.Gray;
                    }
                }
            }

            SkinInfo noSkin = new SkinInfo(null, "No Skin", SKIN_ICON_PREFIX + "NoSkin");
            Skins[noSkin.SubtypeId] = noSkin;

            CleanUpFixMods();

            bool neonSkinExists = false;

            const int SubtypeWidth = -26;
            const int NameWidth = -26;
            const int DLCsWidth = -20;

            Log.Info($"{"Skin SubtypeId",SubtypeWidth} {"Name",NameWidth} {"DLCs",DLCsWidth} Mod");

            foreach(SkinInfo skin in Skins.Values)
            {
                if(skin.SubtypeId.String == "Neon_Colorable_Surface")
                    neonSkinExists = true;

                if(Constants.SKIN_INIT_LOGGING)
                {
                    string dlcList = "";
                    if(skin.Definition?.DLCs != null && skin.Definition.DLCs.Length > 0)
                        dlcList = String.Join(",", skin.Definition.DLCs);

                    string modName = skin.Definition?.Context?.ModName ?? "";

                    Log.Info($"{$"'{skin.SubtypeId.String}'",SubtypeWidth} {skin.Name,NameWidth} {dlcList,DLCsWidth} {modName}");

#if false
                    sb.Clear();
                    sb.Append("Defined skin id='").Append(skin.SubtypeId.String).Append("'; Name='").Append(skin.Name).Append("'");

                    if(skin.Icon.String == SKIN_ICON_UNKNOWN)
                    {
                        sb.Append("; No Icon!");
                    }

                    if(skin.Definition?.DLCs != null && skin.Definition.DLCs.Length > 0)
                    {
                        sb.Append("; DLC=");

                        int preLen = sb.Length;

                        foreach(string dlcId in skin.Definition.DLCs)
                        {
                            IMyDLC dlc;
                            if(!MyAPIGateway.DLC.TryGetDLC(dlcId, out dlc))
                            {
                                Log.Error($"Skin '{skin.SubtypeId.String}' uses unknown DLC={dlcId}");
                                continue;
                            }

                            sb.Append(dlc.Name).Append(", ");
                        }

                        if(sb.Length > preLen)
                            sb.Length -= 2; // remove last comma
                    }

                    if(skin.Definition?.Context != null && !skin.Definition.Context.IsBaseGame)
                    {
                        sb.Append("; Mod='").Append(skin.Definition.Context.ModName).Append("'");
                    }

                    Log.Info(sb.ToString());
#endif
                }
            }

            if(!neonSkinExists)
            {
                Log.Error("WARNING: Expected to find 'Neon_Colorable_Surface' but did not!\nCheck the skins list in the mod's log to ensure all of them are there.");
            }
        }

        /// <summary>
        /// Attempt to identify if the given asset definition is for blocks (as opposed to skins for characters or tools)
        /// </summary>
        static bool IsBlockSkin(MyAssetModifierDefinition assetDef)
        {
            if(assetDef == null)
                return false;

            string modName = assetDef.Context.GetModName();

            try
            {
                string subtype = assetDef.Id.SubtypeName;

                if(subtype == TEST_ARMOR_SUBTYPE)
                    return false; // skip unusable vanilla test armor

                if(subtype == "RustNonColorable_Armor")
                    return false; // HACK: DLC-less skin that has no steam item, not sure what to do about this so I'm just gonna make it not exist for now

                if(subtype.EndsWith(ARMOR_SUFFIX))
                    return true;

                // now to guess the ones that don't have _Armor suffix...

                if(assetDef.Icons != null)
                {
                    foreach(string icon in assetDef.Icons)
                    {
                        if(icon == null)
                            continue;

                        if(icon.IndexOf("armor", StringComparison.OrdinalIgnoreCase) != -1)
                            return true;
                    }
                }

                if(assetDef.Textures != null)
                {
                    foreach(MyObjectBuilder_AssetModifierDefinition.MyAssetTexture texture in assetDef.Textures)
                    {
                        if(texture.Location == null)
                            continue;

                        if(texture.Location.Equals("SquarePlate", StringComparison.OrdinalIgnoreCase))
                            return true;

                        if(texture.Location.Equals("PaintedMetal_Colorable", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error($"Error in {nameof(FixModTexturePaths)} for asset {assetDef.Id.ToString()} from {modName}\n{e}");
            }

            return false;
        }

        void UpdatePalette()
        {
            LocalInfo.SetColors(MyAPIGateway.Session.Player.BuildColorSlots);
        }

        protected override void UpdateAfterSim(int tick)
        {
            if(tick % CHECK_PALETTE_PASSIVE_TICKS == 0)
            {
                CheckLocalPaletteChanges();
            }
        }

        void CheckLocalPaletteChanges()
        {
            if(!Main.CheckPlayerField.Ready)
                return;

            if (MyAPIGateway.Session.Player == null)
                return;

            List<Vector3> newColors = MyAPIGateway.Session.Player.BuildColorSlots;
            IReadOnlyList<Vector3> storedColors = LocalInfo.ColorsMasks;

            if(storedColors.Count == 0)
            {
                UpdatePalette();
                return;
            }

            if(newColors.Count != storedColors.Count)
            {
                Log.Error($"{GetType().Name} :: Failed to compare colors, different sizes: new={newColors.Count.ToString()} vs old={storedColors.Count.ToString()}.", Log.PRINT_MESSAGE);
                UpdatePalette();
                return;
            }

            bool changed = false;

            for(int i = 0; i < newColors.Count; i++)
            {
                if(!Utils.ColorMaskEquals(storedColors[i], Utils.ColorMaskNormalize(newColors[i])))
                {
                    changed = true;
                    Main.NetworkLibHandler.PacketPaletteSetColor.Send(i, newColors[i]);
                }
            }

            if(changed)
                UpdatePalette();
        }

        public bool IsColorMaskInPalette(Vector3 colorMask)
        {
            for(int i = 0; i < LocalInfo.ColorsMasks.Count; i++)
            {
                if(Utils.ColorMaskEquals(LocalInfo.ColorsMasks[i], colorMask))
                    return true;
            }

            return false;
        }

        public SkinInfo GetSkinInfo(MyStringHash skinSubtypeId)
        {
            SkinInfo skin;
            if(Skins.TryGetValue(skinSubtypeId, out skin))
                return skin;
            else
                return null;
        }

        /// <summary>
        /// Gets color and skin from supplied paint material which can be from blocks or players' palette.
        /// If color doesn't exist in palette it will replace currently selected slot, otherwise it selects the existing color slot.
        /// Respects nullability from PaintMaterial (applycolor/skin toggles from other players).
        /// </summary>
        public void GrabPaletteFromPaint(PaintMaterial pickedMaterial, bool changeApply = false)
        {
            if(SetColorAndSkin(pickedMaterial, changeApply))
                Main.HUDSounds.PlayMouseClick();
            else
                Main.HUDSounds.PlayUnable();
        }

        bool SetColorAndSkin(PaintMaterial pickedMaterial, bool changeApply)
        {
            // in case of players with both ApplyColor and ApplySkin turned off.
            if(!pickedMaterial.ColorMask.HasValue && !pickedMaterial.Skin.HasValue)
            {
                Main.Notifications.Show(0, $"Target has no color or skin enabled.", MyFontEnum.Red, 3000);
                return false;
            }

            PaintMaterial currentMaterial = GetLocalPaintMaterial();

            if(currentMaterial.PaintEquals(pickedMaterial))
            {
                Main.Notifications.Show(0, $"Color and skin already selected.", MyFontEnum.Debug, 2000);
                return false;
            }

            bool success = false;

            if(pickedMaterial.ColorMask.HasValue)
            {
                if(changeApply)
                    LocalInfo.ApplyColor = true;

                if(Utils.ColorMaskEquals(LocalInfo.SelectedColorMask, pickedMaterial.ColorMask.Value))
                {
                    Main.Notifications.Show(0, $"Color already selected.", MyFontEnum.Debug, 2000);
                }
                else
                {
                    bool inPalette = false;

                    for(int i = 0; i < LocalInfo.ColorsMasks.Count; i++)
                    {
                        if(Utils.ColorMaskEquals(LocalInfo.ColorsMasks[i], pickedMaterial.ColorMask.Value))
                        {
                            inPalette = true;
                            LocalInfo.SelectedColorSlot = i;
                            MyAPIGateway.Session.Player.SelectedBuildColorSlot = i;

                            Main.Notifications.Show(0, $"Color exists in slot [{(i + 1).ToString()}], selected.", MyFontEnum.Debug, 2000);
                            break;
                        }
                    }

                    if(!inPalette)
                    {
                        MyAPIGateway.Session.Player.ChangeOrSwitchToColor(pickedMaterial.ColorMask.Value);

                        Main.NetworkLibHandler.PacketPaletteSetColor.Send(LocalInfo.SelectedColorSlot, pickedMaterial.ColorMask.Value);

                        Main.Notifications.Show(0, $"Color slot [{(LocalInfo.SelectedColorSlot + 1).ToString()}] set to [{Utils.ColorMaskToString(pickedMaterial.ColorMask.Value)}]", MyFontEnum.Debug, 2000);
                    }

                    success = true;
                }
            }
            else
            {
                if(changeApply)
                    LocalInfo.ApplyColor = false;
            }

            if(pickedMaterial.Skin.HasValue)
            {
                if(changeApply)
                    LocalInfo.ApplySkin = true;

                if(LocalInfo.SelectedSkin != pickedMaterial.Skin.Value)
                {
                    SkinInfo skin = GetSkinInfo(pickedMaterial.Skin.Value);
                    if(skin != null)
                    {
                        if(!skin.LocallyOwned)
                        {
                            Main.Notifications.Show(1, $"Skin [{skin.Name}] is not owned, not selected.", MyFontEnum.Red, 2000);
                        }
                        else
                        {
                            LocalInfo.SelectedSkin = skin.SubtypeId;
                            success = true;

                            Main.Notifications.Show(1, $"Selected skin: [{skin.Name}]", MyFontEnum.Debug, 2000);
                        }
                    }
                }
            }
            else
            {
                if(changeApply)
                    LocalInfo.ApplySkin = false;
            }

            return success;
        }

        public PaintMaterial GetLocalPaintMaterial()
        {
            if(LocalInfo == null)
                return new PaintMaterial();

            Vector3? colorMask = null;
            MyStringHash? skin = null;

            if(LocalInfo.UseColor)
                colorMask = LocalInfo.SelectedColorMask;

            if(LocalInfo.ApplySkin)
                skin = LocalInfo.SelectedSkin;

            return new PaintMaterial(colorMask, skin);
        }

        public PlayerInfo GetPlayerInfo(ulong steamId)
        {
            return PlayerInfo.GetValueOrDefault(steamId, null);
        }

        public PlayerInfo GetOrAddPlayerInfo(ulong steamId)
        {
            PlayerInfo pi;
            if(!PlayerInfo.TryGetValue(steamId, out pi))
            {
                pi = new PlayerInfo(steamId);
                PlayerInfo.Add(steamId, pi);
            }

            return pi;
        }

        void SettingsChanged()
        {
            if(SkinsForHUD == null)
                return;

            SkinsForHUD.Clear();

            foreach(SkinInfo skin in Skins.Values)
            {
                skin.Refresh();

                if(skin.Selectable)
                    SkinsForHUD.Add(skin);
            }

            HasAnySkin = (SkinsForHUD.Count > 1);

            // change selection if it's an unselectable skin
            if(!LocalInfo.SelectedSkinInfo.Selectable)
            {
                LocalInfo.SelectedSkin = MyStringHash.NullOrEmpty;
            }
        }

        #region Fix mod texture paths
        Dictionary<string, MyTextureChange> TextureChanges = null;

        void CleanUpFixMods() // gets called after all asset modifier definitions have been iterated.
        {
            TextureChanges = null;
        }

        bool FixModTexturePaths(MyAssetModifierDefinition assetDef)
        {
            if(assetDef == null)
                throw new ArgumentNullException(nameof(assetDef));

            string modName = assetDef.Context.GetModName();

            try
            {
                if(assetDef.Textures == null || assetDef.Textures.Count <= 0)
                {
                    Log.Error($"Skin '{assetDef.Id.SubtypeName}' has no textures!");
                    return false;
                }

                if(TextureChanges == null)
                    TextureChanges = new Dictionary<string, MyTextureChange>();
                else
                    TextureChanges.Clear();

                // NOTE: this is not actually a useful path! it's only to get a normalized path with same slashes and stuff.
                string testGamePath = Path.GetFullPath(@"Textures\Models\Cubes\armor\Skins");

                for(int i = 0; i < assetDef.Textures.Count; i++)
                {
                    MyObjectBuilder_AssetModifierDefinition.MyAssetTexture texture = assetDef.Textures[i];

                    if(string.IsNullOrEmpty(texture.Filepath))
                        continue;

                    bool textureExistsInMod = MyAPIGateway.Utilities.FileExistsInModLocation(texture.Filepath, assetDef.Context.ModItem);
                    string testPath = Path.GetFullPath(texture.Filepath);

                    if(!textureExistsInMod && testPath.StartsWith(testGamePath, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Error($"Skin '{assetDef.Id.SubtypeName}' from {modName} uses game's skin textures, that is not allowed.");
                        return false;
                    }

                    if(texture.Filepath.StartsWith(".."))
                        continue; // some fix already applied

                    if(!textureExistsInMod)
                        continue;

                    string fixedPath = Path.Combine(assetDef.Context.ModPath, texture.Filepath);

                    // has no direct effect on the skin, the render thing after does, but this is for consistency
                    texture.Filepath = fixedPath;
                    assetDef.Textures[i] = texture;

                    MyTextureChange texChange = TextureChanges.GetValueOrDefault(texture.Location);
                    switch((TextureType)texture.Type)
                    {
                        case TextureType.ColorMetal: texChange.ColorMetalFileName = fixedPath; break;
                        case TextureType.NormalGloss: texChange.NormalGlossFileName = fixedPath; break;
                        case TextureType.Extensions: texChange.ExtensionsFileName = fixedPath; break;
                        case TextureType.Alphamask: texChange.AlphamaskFileName = fixedPath; break;
                    }
                    TextureChanges[texture.Location] = texChange;
                }

                MyDefinitionManager.MyAssetModifiers assetModifierForRender = MyDefinitionManager.Static.GetAssetModifierDefinitionForRender(assetDef.Id.SubtypeId);

                foreach(var kv in TextureChanges)
                {
                    assetModifierForRender.SkinTextureChanges[MyStringId.GetOrCompute(kv.Key)] = kv.Value;
                }

                Log.Info($"Fixed mod-relative paths for skin '{assetDef.Id.SubtypeName}' from {modName}.");
                return true;
            }
            catch(Exception e)
            {
                Log.Error($"Error in {nameof(FixModTexturePaths)} for asset {assetDef.Id.ToString()} from {modName}\n{e}");
                return false;
            }
        }

        [Flags]
        enum TextureType
        {
            Unspecified = 0x0,
            ColorMetal = 0x1,
            NormalGloss = 0x2,
            Extensions = 0x4,
            Alphamask = 0x8
        }
        #endregion
    }
}
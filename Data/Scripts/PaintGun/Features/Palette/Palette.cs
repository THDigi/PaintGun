using System;
using System.Collections.Generic;
using System.Text;
using Digi.ComponentLib;
using Digi.PaintGun.Utilities;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun.Features.Palette
{
    public class Palette : ModComponent
    {
        const int CHECK_PALETTE_PASSIVE_TICKS = Constants.TICKS_PER_SECOND * 1;
        const int PLAYER_INFO_CLEANUP_TICKS = Constants.TICKS_PER_SECOND * 60 * 5;

        public List<SkinInfo> BlockSkins;
        public List<SkinInfo> OwnedSkins;
        public List<SkinInfo> SkinsForHUD;
        public int OwnedSkinsCount => (OwnedSkins == null ? 0 : OwnedSkins.Count);

        public PlayerInfo LocalInfo;
        public bool ReplaceMode = false;
        public bool ReplaceShipWide = false;
        public bool ColorPickMode
        {
            get { return LocalInfo.ColorPickMode; }
            set { LocalInfo.ColorPickMode = value; }
        }

        public Vector3 DefaultColorMask = new Vector3(0, -1, 0);

        public Dictionary<ulong, PlayerInfo> PlayerInfo = new Dictionary<ulong, PlayerInfo>();

        public Palette(PaintGunMod main) : base(main)
        {
            UpdateMethods = UpdateFlags.UPDATE_AFTER_SIM;

            InitBlockSkins();
        }

        protected override void RegisterComponent()
        {
            if(Main.IsPlayer)
            {
                LocalInfo = GetOrAddPlayerInfo(MyAPIGateway.Multiplayer.MyId);
                Main.CheckPlayerField.PlayerReady += PlayerReady;
                Main.PlayerHandler.PlayerDisconnected += PlayerDisconnected;
                Main.Settings.SettingsChanged += ComputeShownSkins;
            }
        }

        protected override void UnregisterComponent()
        {
            if(!IsRegistered)
                return;

            if(Main.IsPlayer)
            {
                Main.CheckPlayerField.PlayerReady -= PlayerReady;
                Main.PlayerHandler.PlayerDisconnected -= PlayerDisconnected;
                Main.Settings.SettingsChanged -= ComputeShownSkins;
            }
        }

        void PlayerDisconnected(IMyPlayer player)
        {
            PlayerInfo.Remove(player.SteamUserId);
        }

        void PlayerReady()
        {
            DefaultColorMask = Utils.ColorMaskNormalize(MyAPIGateway.Session.Player.DefaultBuildColorSlots.ItemAt(0));
            LocalInfo.SelectedColorIndex = MyAPIGateway.Session.Player.SelectedBuildColorSlot;
            UpdatePalette();

            // broadcast local player's palette to everyone and server sends everyone's palettes back.
            Main.NetworkLibHandler.PacketJoinSharePalette.Send(LocalInfo, MyAPIGateway.Multiplayer.MyId);
        }

        const string ARMOR_SUFFIX = "_Armor";
        const string TEST_ARMOR_SUBTYPE = "TestArmor";

        const string SKIN_ICON_PREFIX = "PaintGun_SkinIcon_";
        const string SKIN_ICON_UNKNOWN = SKIN_ICON_PREFIX + "Unknown";

        void InitBlockSkins()
        {
            if(Constants.SKIN_INIT_LOGGING)
                Log.Info("Finding block skins...");

            var definedIcons = new HashSet<string>();
            foreach(var def in MyDefinitionManager.Static.GetTransparentMaterialDefinitions())
            {
                if(def.Id.SubtypeName.StartsWith(SKIN_ICON_PREFIX))
                    definedIcons.Add(def.Id.SubtypeName);
            }

            int foundSkins = 0;
            foreach(var assetDef in MyDefinitionManager.Static.GetAssetModifierDefinitions())
            {
                if(IsSkinAsset(assetDef))
                    foundSkins++;
            }

            BlockSkins = new List<SkinInfo>(foundSkins + 1); // include "No Skin" too.
            OwnedSkins = new List<SkinInfo>(BlockSkins.Capacity);
            SkinsForHUD = new List<SkinInfo>(OwnedSkins.Capacity);
            var sb = new StringBuilder(128);

            foreach(var assetDef in MyDefinitionManager.Static.GetAssetModifierDefinitions())
            {
                if(IsSkinAsset(assetDef))
                {
                    string name = assetDef.Id.SubtypeName;
                    sb.Clear();
                    sb.Append(name);

                    if(name.EndsWith(ARMOR_SUFFIX))
                        sb.Length -= ARMOR_SUFFIX.Length;

                    var nameId = sb.ToString();

                    // Add spaces before upper case letters except first.
                    // Or replace _ with space.
                    for(int i = 1; i < sb.Length; ++i)
                    {
                        var c = sb[i];

                        if(c == '_')
                            sb[i] = ' ';

                        if(char.IsUpper(c) && sb[i - 1] != ' ')
                            sb.Insert(i, ' ');
                    }

                    name = sb.ToString();
                    var icon = SKIN_ICON_PREFIX + nameId;

                    if(!definedIcons.Contains(icon))
                    {
                        if(Utils.IsLocalMod())
                            Log.Error($"{icon} not found in transparent materials definitions.", Log.PRINT_MESSAGE);

                        icon = SKIN_ICON_UNKNOWN;
                    }

                    var skinInfo = new SkinInfo(assetDef, name, icon);
                    BlockSkins.Add(skinInfo);
                }
            }

            // consistent order for network sync, also matches with what the game UI sorts them by
            BlockSkins.Sort((a, b) => a.SubtypeId.String.CompareTo(b.SubtypeId.String));

            // "no skin" is always first
            BlockSkins.Insert(0, new SkinInfo(null, "No Skin", SKIN_ICON_PREFIX + "NoSkin"));

            bool neonSkinExists = false;

            // assign final index to the value too
            for(int i = 0; i < BlockSkins.Count; ++i)
            {
                var skin = BlockSkins[i];
                skin.Index = i;

                if(skin.SubtypeId.String == "Neon_Colorable_Surface")
                    neonSkinExists = true;

                if(Constants.SKIN_INIT_LOGGING)
                {
                    Log.Info($"Defined skin #{i.ToString()} - {skin.Name} ({skin.SubtypeId.String}){(skin.Icon.String == SKIN_ICON_UNKNOWN ? "; No Icon!" : "")}{(skin.Definition?.Context != null ? $"; Mod={skin.Definition.Context.ModName}" : "")}");
                }
            }

            if(!neonSkinExists)
            {
                Log.Error("WARNING: Expected to find 'Neon_Colorable_Surface' but did not!" +
                    "\nCheck the skins list in the mod's log to ensure all of them are there." +
                    "\nSkin index is what gets sent by clients and if skin number mismatches it will cause issues." +
                    "\nThis skin (and few others) do not have the '_Armor' suffix, therefore this mod looks for 'armor' in icons or 'SquarePlate' in textures, and it didn't find it by that criteria either."
                );
            }
        }

        static bool IsSkinAsset(MyAssetModifierDefinition assetDef)
        {
            if(assetDef == null)
                return false;

            try
            {
                if(assetDef.Id.SubtypeName == TEST_ARMOR_SUBTYPE)
                    return false; // skip unusable vanilla test armor

                if(assetDef.Id.SubtypeName.EndsWith(ARMOR_SUFFIX))
                    return true;

                if(assetDef.Icons != null)
                {
                    foreach(var icon in assetDef.Icons)
                    {
                        if(icon == null)
                            continue;

                        if(icon.IndexOf("armor", StringComparison.OrdinalIgnoreCase) != -1)
                            return true;
                    }
                }

                if(assetDef.Textures != null)
                {
                    foreach(var texture in assetDef.Textures)
                    {
                        if(texture.Location == null)
                            continue;

                        if(texture.Location.Equals("SquarePlate", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error($"Error in IsSkinAsset() for asset={assetDef.Id.ToString()}\n{e}");
            }

            return false;
        }

        void UpdatePalette()
        {
            LocalInfo.SetColors(MyAPIGateway.Session.Player.BuildColorSlots);
        }

        protected override void UpdateAfterSim(int tick)
        {
            if(Main.IsPlayer && tick % CHECK_PALETTE_PASSIVE_TICKS == 0)
            {
                CheckLocalPaletteChanges();
            }
        }

        void CheckLocalPaletteChanges()
        {
            if(!Main.CheckPlayerField.Ready)
                return;

            var newColors = MyAPIGateway.Session.Player.BuildColorSlots;
            var storedColors = LocalInfo.ColorsMasks;

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
            for(int i = 0; i < BlockSkins.Count; i++)
            {
                var skin = BlockSkins[i];

                if(skin.SubtypeId == skinSubtypeId)
                    return skin;
            }

            return null;
        }

        public SkinInfo GetSkinInfo(int index)
        {
            if(index < 0 || index >= BlockSkins.Count)
                throw new ArgumentException($"Given index={index.ToString()} is negative or above {(BlockSkins.Count - 1).ToString()}");

            return BlockSkins[index];
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

            var currentMaterial = GetLocalPaintMaterial();

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
                            LocalInfo.SelectedColorIndex = i;
                            MyAPIGateway.Session.Player.SelectedBuildColorSlot = i;

                            Main.Notifications.Show(0, $"Color exists in slot [{(i + 1).ToString()}], selected.", MyFontEnum.Debug, 2000);
                            break;
                        }
                    }

                    if(!inPalette)
                    {
                        MyAPIGateway.Session.Player.ChangeOrSwitchToColor(pickedMaterial.ColorMask.Value);

                        Main.NetworkLibHandler.PacketPaletteSetColor.Send(LocalInfo.SelectedColorIndex, pickedMaterial.ColorMask.Value);

                        Main.Notifications.Show(0, $"Color slot [{(LocalInfo.SelectedColorIndex + 1).ToString()}] set to [{Utils.ColorMaskToString(pickedMaterial.ColorMask.Value)}]", MyFontEnum.Debug, 2000);
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

                if(GetSkinInfo(LocalInfo.SelectedSkinIndex).SubtypeId != pickedMaterial.Skin.Value)
                {
                    var skin = GetSkinInfo(pickedMaterial.Skin.Value);

                    if(skin != null)
                    {
                        if(!skin.LocallyOwned)
                        {
                            Main.Notifications.Show(1, $"Skin [{skin.Name}] is not owned, not selected.", MyFontEnum.Red, 2000);
                        }
                        else
                        {
                            LocalInfo.SelectedSkinIndex = skin.Index;
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

            if(LocalInfo.ApplyColor)
                colorMask = LocalInfo.SelectedColorMask;

            if(LocalInfo.ApplySkin)
                skin = GetSkinInfo(LocalInfo.SelectedSkinIndex).SubtypeId;

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

        public void ComputeOwnedSkins()
        {
            OwnedSkins.Clear();

            foreach(SkinInfo skin in BlockSkins)
            {
                if(skin.LocallyOwned)
                    OwnedSkins.Add(skin);
            }

            ComputeShownSkins();
        }

        void ComputeShownSkins()
        {
            if(SkinsForHUD == null)
                return;

            SkinsForHUD.Clear();

            foreach(var skin in OwnedSkins)
            {
                skin.ShowOnPalette = !Main.Settings.hideSkinsFromPalette.Contains(skin.SubtypeId.String);
                if(skin.Selectable)
                    SkinsForHUD.Add(skin);
            }

            // change selection if it's an unselectable skin
            var selectedSkin = GetSkinInfo(LocalInfo.SelectedSkinIndex);
            if(!selectedSkin.Selectable)
            {
                LocalInfo.SelectedSkinIndex = 0;
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Text;
using Digi.ComponentLib;
using Digi.PaintGun.Utilities;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun.Features.Palette
{
    public class Palette : ModComponent
    {
        const int CHECK_PALETTE_PASSIVE_TICKS = Constants.TICKS_PER_SECOND * 1;
        const int PLAYER_INFO_CLEANUP_TICKS = Constants.TICKS_PER_SECOND * 60 * 5;

        public List<SkinInfo> BlockSkins;
        public int OwnedSkins = 0;

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
        }

        protected override void RegisterComponent()
        {
            InitBlockSkins();

            if(Main.IsPlayer)
            {
                LocalInfo = GetOrAddPlayerInfo(MyAPIGateway.Multiplayer.MyId);
                Main.CheckPlayerField.PlayerReady += PlayerReady;

                MyVisualScriptLogicProvider.PlayerDisconnected += PlayerDisconnected;
            }
        }

        protected override void UnregisterComponent()
        {
            if(Main.IsPlayer)
            {
                Main.CheckPlayerField.PlayerReady -= PlayerReady;

                MyVisualScriptLogicProvider.PlayerDisconnected -= PlayerDisconnected;
            }
        }

        void PlayerDisconnected(long identityId)
        {
            try
            {
                var player = Utils.GetPlayerByIdentityId(identityId);

                if(player == null)
                {
                    Log.Error($"Unknown player disconnected! IdentityId={identityId.ToString()}");
                    return;
                }

                PlayerInfo.Remove(player.SteamUserId);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        void PlayerReady()
        {
            DefaultColorMask = Utils.ColorMaskNormalize(MyAPIGateway.Session.Player.DefaultBuildColorSlots.ItemAt(0));
            LocalInfo.SelectedColorIndex = MyAPIGateway.Session.Player.SelectedBuildColorSlot;
            UpdatePalette();

            // broadcast local player's palette to everyone and server sends everyone's palettes back.
            NetworkLibHandler.PacketJoinSharePalette.Send(LocalInfo, MyAPIGateway.Multiplayer.MyId);
        }

        void InitBlockSkins()
        {
            const string ARMOR_SUFFIX = "_Armor";
            const string SKIN_ICON_PREFIX = "PaintGun_SkinIcon_";

            var definedIcons = new HashSet<string>();
            foreach(var def in MyDefinitionManager.Static.GetTransparentMaterialDefinitions())
            {
                if(def.Id.SubtypeName.StartsWith(SKIN_ICON_PREFIX))
                    definedIcons.Add(def.Id.SubtypeName);
            }

            int skins = 0;
            foreach(var assetDef in MyDefinitionManager.Static.GetAssetModifierDefinitions())
            {
                if(assetDef.Id.SubtypeName.EndsWith(ARMOR_SUFFIX))
                    skins++;
            }

            BlockSkins = new List<SkinInfo>(skins);
            var sb = new StringBuilder(64);

            foreach(var assetDef in MyDefinitionManager.Static.GetAssetModifierDefinitions())
            {
                if(assetDef.Id.SubtypeName.EndsWith(ARMOR_SUFFIX))
                {
                    sb.Clear();
                    sb.Append(assetDef.Id.SubtypeName);
                    sb.Length -= ARMOR_SUFFIX.Length;

                    var nameId = sb.ToString();

                    // skip first character
                    for(int i = sb.Length - 1; i >= 1; --i)
                    {
                        var c = sb[i];

                        if(char.IsUpper(c))
                            sb.Insert(i, ' ');
                    }

                    var name = sb.ToString();
                    var icon = SKIN_ICON_PREFIX + nameId;

                    if(!definedIcons.Contains(icon))
                    {
                        if(Utils.IsLocalMod())
                            Log.Info($"WARNING: {icon} not found in transparent materials definitions.", Log.PRINT_MESSAGE);

                        icon = SKIN_ICON_PREFIX + "Unknown";
                    }

                    BlockSkins.Add(new SkinInfo(assetDef.Id.SubtypeId, name, icon));
                }
            }

            // consistent order for network sync, also matches with what the game UI sorts them by
            BlockSkins.Sort((a, b) => a.SubtypeId.String.CompareTo(b.SubtypeId.String));

            // "no skin" is always first
            BlockSkins.Insert(0, new SkinInfo(MyStringHash.NullOrEmpty, "No Skin", SKIN_ICON_PREFIX + "NoSkin", true));

            // assign final index to the value too
            for(int i = 0; i < BlockSkins.Count; ++i)
            {
                BlockSkins[i].Index = i;
            }
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
            if(!CheckPlayerField.Ready)
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
                    NetworkLibHandler.PacketPaletteSetColor.Send(i, newColors[i]);
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

            throw new ArgumentException($"Given skin subtype='{skinSubtypeId.ToString()}' is not known by this mod.");
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
                HUDSounds.PlayMouseClick();
            else
                HUDSounds.PlayUnable();
        }

        bool SetColorAndSkin(PaintMaterial pickedMaterial, bool changeApply)
        {
            // in case of players with both ApplyColor and ApplySkin turned off.
            if(!pickedMaterial.ColorMask.HasValue && !pickedMaterial.Skin.HasValue)
            {
                Notifications.Show(0, $"Target has no color or skin enabled.", MyFontEnum.Red, 3000);
                return false;
            }

            var currentMaterial = GetLocalPaintMaterial();

            if(currentMaterial.PaintEquals(pickedMaterial))
            {
                Notifications.Show(0, $"Color and skin already selected.", MyFontEnum.White, 2000);
                return false;
            }

            bool success = false;

            if(pickedMaterial.ColorMask.HasValue)
            {
                if(changeApply)
                    LocalInfo.ApplyColor = true;

                if(Utils.ColorMaskEquals(LocalInfo.SelectedColorMask, pickedMaterial.ColorMask.Value))
                {
                    Notifications.Show(0, $"Color already selected.", MyFontEnum.White, 2000);
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

                            Notifications.Show(0, $"Color exists in slot [{(i + 1).ToString()}], selected.", MyFontEnum.White, 2000);
                            break;
                        }
                    }

                    if(!inPalette)
                    {
                        MyAPIGateway.Session.Player.ChangeOrSwitchToColor(pickedMaterial.ColorMask.Value);

                        NetworkLibHandler.PacketPaletteSetColor.Send(LocalInfo.SelectedColorIndex, pickedMaterial.ColorMask.Value);

                        Notifications.Show(0, $"Color slot [{(LocalInfo.SelectedColorIndex + 1).ToString()}] set to [{Utils.ColorMaskToString(pickedMaterial.ColorMask.Value)}]", MyFontEnum.White, 2000);
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
                            Notifications.Show(1, $"Skin [{skin.Name}] is not owned, not selected.", MyFontEnum.Red, 2000);
                        }
                        else
                        {
                            LocalInfo.SelectedSkinIndex = skin.Index;
                            success = true;

                            Notifications.Show(1, $"Selected skin: [{GetSkinInfo(pickedMaterial.Skin.Value).Name}]", MyFontEnum.White, 2000);
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
    }
}
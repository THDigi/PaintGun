using System;
using System.Collections.Generic;
using Digi.PaintGun.Features.Tool;
using Digi.PaintGun.Utilities;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun.Features.Palette
{
    public class PlayerInfo
    {
        public readonly ulong SteamId;

        /// <summary>
        /// Read-only list of this player's color palette.
        /// Use <see cref="SetColors(List{Vector3})"/> or <see cref="SetColorAt(int, Vector3)"/> to edit it.
        /// </summary>
        public IReadOnlyList<Vector3> ColorsMasks => _colorsMasks;
        readonly Vector3[] _colorsMasks;

        /// <summary>
        /// Owned skins for this player server-side.
        /// NOTE: Does not include index 0 (default skin).
        /// </summary>
        public List<int> OwnedSkinIndexes;

        public void SetColors(List<Vector3> copyFrom)
        {
            if(copyFrom.Count != _colorsMasks.Length)
            {
                Log.Error($"SetColors(List<Vector3>) got unexpected list size={copyFrom.Count.ToString()}");
                // continue execution
            }

            for(int i = 0; i < Math.Min(_colorsMasks.Length, copyFrom.Count); i++)
            {
                // Normalize color because it can differ to the palette after being painted and converted like this, causing weird issues
                _colorsMasks[i] = Utils.ColorMaskNormalize(copyFrom[i]);
            }

            OnColorListChanged?.Invoke(this, null);
        }

        public void SetColors(uint[] copyFrom)
        {
            if(copyFrom.Length != _colorsMasks.Length)
            {
                Log.Error($"SetColors(uint[]) got unexpected array size={copyFrom.Length.ToString()}");
                // continue execution
            }

            for(int i = 0; i < Math.Min(_colorsMasks.Length, copyFrom.Length); i++)
            {
                _colorsMasks[i] = ColorExtensions.UnpackHSVFromUint(copyFrom[i]);
            }

            OnColorListChanged?.Invoke(this, null);
        }

        public void SetColorAt(int index, Vector3 colorMask)
        {
            colorMask = Utils.ColorMaskNormalize(colorMask);
            if(colorMask != _colorsMasks[index])
            {
                _colorsMasks[index] = colorMask;
                OnColorListChanged?.Invoke(this, index);
            }
        }

        public Vector3 SelectedColorMask => ColorsMasks[_selectedColorSlot];

        int _selectedColorSlot;
        public int SelectedColorIndex
        {
            get { return _selectedColorSlot; }
            set
            {
                if(_selectedColorSlot != value)
                {
                    var oldValue = _selectedColorSlot;
                    _selectedColorSlot = value;
                    OnColorSlotSelected?.Invoke(this, oldValue, _selectedColorSlot);

                    if(Main.IsPlayer && SteamId == MyAPIGateway.Multiplayer.MyId)
                        Main.PaletteScheduledSync.ScheduleSyncFor(colorIndex: true);
                }
            }
        }

        int _selectedSkinIndex;
        public int SelectedSkinIndex
        {
            get { return _selectedSkinIndex; }
            set
            {
                if(_selectedSkinIndex != value)
                {
                    var oldValue = _selectedSkinIndex;
                    _selectedSkinIndex = value;
                    OnSkinIndexSelected?.Invoke(this, oldValue, _selectedSkinIndex);

                    if(Main.IsPlayer && SteamId == MyAPIGateway.Multiplayer.MyId)
                        Main.PaletteScheduledSync.ScheduleSyncFor(skinIndex: true);
                }
            }
        }

        bool _applyColor = true;
        public bool ApplyColor
        {
            get
            {
                if(!_applyColor)
                    return false;

                SkinInfo skin = Main.Palette.GetSkinInfo(_selectedSkinIndex);
                if(skin?.Definition != null && skin.Definition.DefaultColor.HasValue)
                    return false;

                return true;
            }
            set
            {
                if(_applyColor != value)
                {
                    _applyColor = value;
                    OnApplyColorChanged?.Invoke(this);

                    if(Main.IsPlayer && SteamId == MyAPIGateway.Multiplayer.MyId)
                        Main.PaletteScheduledSync.ScheduleSyncFor(applyColor: true);
                }
            }
        }

        bool _applySkin = true;
        public bool ApplySkin
        {
            get { return _applySkin; }
            set
            {
                if(_applySkin != value)
                {
                    _applySkin = value;
                    OnApplySkinChanged?.Invoke(this);

                    if(Main.IsPlayer && SteamId == MyAPIGateway.Multiplayer.MyId)
                        Main.PaletteScheduledSync.ScheduleSyncFor(applySkin: true);
                }
            }
        }

        bool _colorPickMode = false;
        public bool ColorPickMode
        {
            get { return _colorPickMode; }
            set
            {
                if(_colorPickMode != value)
                {
                    _colorPickMode = value;
                    OnColorPickModeChanged?.Invoke(this);

                    if(Main.IsPlayer)
                    {
                        if(SteamId == MyAPIGateway.Multiplayer.MyId)
                            Main.NetworkLibHandler.PacketPaletteUpdate.Send(colorPickMode: ColorPickMode);

                        var tool = Main.ToolHandler.GetToolHeldBy(SteamId);
                        if(tool != null)
                            tool.SprayCooldown = PaintGunItem.SPRAY_COOLDOWN_COLORPICKMODE;
                    }
                }
            }
        }

        public event IndexSelectedDelegate OnColorSlotSelected;
        public event IndexSelectedDelegate OnSkinIndexSelected;
        public delegate void IndexSelectedDelegate(PlayerInfo pi, int prevIndex, int newIndex);

        public event PlayerInfoDelegate OnApplyColorChanged;
        public event PlayerInfoDelegate OnApplySkinChanged;
        public event PlayerInfoDelegate OnColorPickModeChanged;
        public delegate void PlayerInfoDelegate(PlayerInfo pi);

        /// <summary>
        /// When color list gets updated. If index has no value then the entire list was refreshed.
        /// </summary>
        public event ColorListChangedDelegate OnColorListChanged;
        public delegate void ColorListChangedDelegate(PlayerInfo pcd, int? index);

        PaintGunMod Main => PaintGunMod.Instance;

        public PlayerInfo(ulong steamId)
        {
            SteamId = steamId;

            _colorsMasks = new Vector3[Constants.COLOR_PALETTE_SIZE];

            var redColor = new Vector3(0, 1, 1);
            for(int i = 0; i < Constants.COLOR_PALETTE_SIZE; i++)
            {
                _colorsMasks[i] = redColor;
            }
        }

        public PaintMaterial GetPaintMaterial()
        {
            Vector3? colorMask = null;
            MyStringHash? skin = null;

            if(ApplyColor)
                colorMask = SelectedColorMask;

            if(ApplySkin)
                skin = PaintGunMod.Instance.Palette.GetSkinInfo(SelectedSkinIndex).SubtypeId;

            return new PaintMaterial(colorMask, skin);
        }
    }
}
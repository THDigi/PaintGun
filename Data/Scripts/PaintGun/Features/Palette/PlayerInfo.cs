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

            ColorListChanged?.Invoke(this, null);
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

            ColorListChanged?.Invoke(this, null);
        }

        public void SetColorAt(int index, Vector3 colorMask)
        {
            colorMask = Utils.ColorMaskNormalize(colorMask);
            if(colorMask != _colorsMasks[index])
            {
                _colorsMasks[index] = colorMask;
                ColorListChanged?.Invoke(this, index);
            }
        }

        public Vector3 SelectedColorMask => ColorsMasks[_selectedColorSlot];

        int _selectedColorSlot;
        public int SelectedColorSlot
        {
            get { return _selectedColorSlot; }
            set
            {
                if(_selectedColorSlot != value)
                {
                    int oldValue = _selectedColorSlot;
                    _selectedColorSlot = value;
                    ColorSlotSelected?.Invoke(this, oldValue, _selectedColorSlot);

                    if(Main.IsPlayer && SteamId == MyAPIGateway.Multiplayer.MyId)
                        Main.PaletteScheduledSync.ScheduleSyncFor(color: true);
                }
            }
        }

        MyStringHash _selectedSkin;
        public MyStringHash SelectedSkin
        {
            get { return _selectedSkin; }
            set
            {
                if(_selectedSkin != value)
                {
                    MyStringHash oldValue = _selectedSkin;
                    _selectedSkin = value;
                    SkinSelected?.Invoke(this, oldValue, _selectedSkin);

                    SelectedSkinInfo = Main.Palette.GetSkinInfo(_selectedSkin);
                    SkinAllowsColor = !_applySkin || (SelectedSkinInfo?.Definition == null || !SelectedSkinInfo.Definition.DefaultColor.HasValue);

                    if(Main.IsPlayer && SteamId == MyAPIGateway.Multiplayer.MyId)
                        Main.PaletteScheduledSync.ScheduleSyncFor(skin: true);
                }
            }
        }

        public SkinInfo SelectedSkinInfo { get; private set; }

        /// <summary>
        /// If <see cref="ApplyColor"/> and <see cref="SkinAllowsColor"/> are true.
        /// </summary>
        public bool UseColor => _applyColor && SkinAllowsColor;

        /// <summary>
        /// True if selected skin can be recolored OR <see cref="ApplySkin"/> is false.
        /// </summary>
        public bool SkinAllowsColor { get; private set; } = true;

        bool _applyColor = true;
        public bool ApplyColor
        {
            get { return _applyColor; }
            set
            {
                if(_applyColor != value)
                {
                    _applyColor = value;
                    ApplyColorChanged?.Invoke(this);

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
                    ApplySkinChanged?.Invoke(this);

                    SkinAllowsColor = !_applySkin || (SelectedSkinInfo?.Definition == null || !SelectedSkinInfo.Definition.DefaultColor.HasValue);

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
                    ColorPickModeChanged?.Invoke(this);

                    if(Main.IsPlayer)
                    {
                        if(SteamId == MyAPIGateway.Multiplayer.MyId)
                            Main.NetworkLibHandler.PacketPaletteUpdate.Send(colorPickMode: ColorPickMode);
                    }
                }
            }
        }

        public event ColorSelectedDelegate ColorSlotSelected;
        public delegate void ColorSelectedDelegate(PlayerInfo pi, int prevIndex, int newIndex);

        public event SkinSelectedDelegate SkinSelected;
        public delegate void SkinSelectedDelegate(PlayerInfo pi, MyStringHash prevSkin, MyStringHash newSkin);

        public event PlayerInfoDelegate ApplyColorChanged;
        public event PlayerInfoDelegate ApplySkinChanged;
        public event PlayerInfoDelegate ColorPickModeChanged;
        public delegate void PlayerInfoDelegate(PlayerInfo pi);

        /// <summary>
        /// When color list gets updated. If index has no value then the entire list was refreshed.
        /// </summary>
        public event ColorListChangedDelegate ColorListChanged;
        public delegate void ColorListChangedDelegate(PlayerInfo pcd, int? index);

        PaintGunMod Main => PaintGunMod.Instance;

        public PlayerInfo(ulong steamId)
        {
            SteamId = steamId;
            SelectedSkinInfo = Main.Palette.GetSkinInfo(_selectedSkin); // needs assigning

            _colorsMasks = new Vector3[Constants.COLOR_PALETTE_SIZE];

            Vector3 redColor = new Vector3(0, 1, 1);
            for(int i = 0; i < Constants.COLOR_PALETTE_SIZE; i++)
            {
                _colorsMasks[i] = redColor;
            }
        }

        public PaintMaterial GetPaintMaterial()
        {
            Vector3? colorMask = null;
            MyStringHash? skin = null;

            if(UseColor)
                colorMask = SelectedColorMask;

            if(ApplySkin)
                skin = SelectedSkin;

            return new PaintMaterial(colorMask, skin);
        }
    }
}
using System;
using System.Collections.Generic;
using ProtoBuf;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun
{
    public class PlayerColorData
    {
        public ulong SteamId;
        public List<Vector3> Colors;
        public int SelectedSlot = 0;

        int _selectedSkinIndex = 0;
        public int SelectedSkinIndex
        {
            get { return _selectedSkinIndex; }
            set
            {
                OnSkinSelected?.Invoke(_selectedSkinIndex, value);
                _selectedSkinIndex = value;
            }
        }

        public bool ApplyColor = true;
        public bool ApplySkin = true;

        public delegate void SkinSelectedDelegate(int prevIndex, int newIndex);
        public event SkinSelectedDelegate OnSkinSelected;

        public PlayerColorData(ulong steamId, List<Vector3> colors)
        {
            SteamId = steamId;
            Colors = colors;
        }
    }

    public enum PacketAction
    {
        PAINT_BLOCK = 0,
        CONSUME_AMMO,
        GUN_FIRING_ON,
        GUN_FIRING_OFF,
        COLOR_PICK_ON,
        COLOR_PICK_OFF,
        BLOCK_REPLACE_COLOR,
        SELECTED_SLOTS,
        SET_COLOR,
        UPDATE_COLOR,
        UPDATE_COLOR_LIST,
        REQUEST_COLOR_LIST,
        SKINTEST_REQUEST,
        SKINTEST_RESULT,
    }

    [Flags]
    public enum OddAxis
    {
        NONE = 0,
        X = 1,
        Y = 2,
        Z = 4
    }

    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketData
    {
        [ProtoMember(1)]
        public PacketAction Type;

        [ProtoMember(2)]
        public ulong SteamId;

        [ProtoMember(3)]
        public long EntityId;

        [ProtoMember(10)]
        public Vector3I? GridPosition;

        [ProtoMember(11)]
        public SerializedPaintMaterial Paint;

        [ProtoMember(12)]
        public SerializedBlockMaterial OldPaint;

        [ProtoMember(13)]
        public int? Slot;

        [ProtoMember(20)]
        public OddAxis OddAxis;

        [ProtoMember(21)]
        public bool UseGridSystem;

        [ProtoMember(22)]
        public Vector3I? MirrorPlanes;

        [ProtoMember(30)]
        public Color[] PackedColors;

        [ProtoMember(31)]
        public bool[] OwnedSkins;

        public PacketData() { } // empty ctor is required for deserialization

        public override string ToString()
        {
            return $"Type={Type}\n" +
                   $"SteamId={SteamId}\n" +
                   $"EntityId={EntityId}\n" +
                   $"GridPosition={(GridPosition.HasValue ? GridPosition.Value.ToString() : "NULL")}\n" +
                   $"Paint={Paint}\n" +
                   $"OldPaint={OldPaint}\n" +
                   $"Slot={Slot}\n" +
                   $"OddAxis={OddAxis}\n" +
                   $"UseGridSystem={UseGridSystem}\n" +
                   $"MirrorPlanes={(MirrorPlanes.HasValue ? MirrorPlanes.Value.ToString() : "NULL")}\n" +
                   $"PackedColors={(PackedColors != null ? string.Join(",", PackedColors) : "NULL")}\n" +
                   $"OwnedSkins={(OwnedSkins != null ? string.Join(",", OwnedSkins) : "NULL")}";
        }
    }

    public class Particle
    {
        public Color Color;
        public Vector3 RelativePosition;
        public Vector3 VelocityPerTick;
        public short Life;
        public float Radius;
        public float Angle;

        public Particle() { }
    }

    public struct DetectionInfo
    {
        public readonly IMyEntity Entity;
        public readonly Vector3D DetectionPoint;

        public DetectionInfo(IMyEntity entity, Vector3D detectionPoint)
        {
            Entity = entity;
            DetectionPoint = detectionPoint;
        }
    }

    public struct PaintMaterial
    {
        public readonly Vector3? ColorMask;
        public readonly MyStringHash? Skin;

        public PaintMaterial(Vector3? colorMask, MyStringHash? skin)
        {
            ColorMask = colorMask;
            Skin = skin;
        }

        public PaintMaterial(SerializedPaintMaterial material)
        {
            ColorMask = null;
            Skin = null;

            if(material.Color.HasValue)
                ColorMask = PaintGunMod.RGBToColorMask(material.Color.Value);

            if(material.SkinIndex.HasValue)
                Skin = PaintGunMod.instance.BlockSkins[material.SkinIndex.Value].SubtypeId;
        }

        public bool PaintEquals(BlockMaterial blockMaterial)
        {
            return (!ColorMask.HasValue || PaintGunMod.ColorMaskEquals(ColorMask.Value, blockMaterial.ColorMask)) && (!Skin.HasValue || Skin.Value.Equals(blockMaterial.Skin));
        }

        public bool PaintEquals(IMySlimBlock block)
        {
            return (!ColorMask.HasValue || PaintGunMod.ColorMaskEquals(ColorMask.Value, block.ColorMaskHSV)) && (!Skin.HasValue || Skin.Value.Equals(block.SkinSubtypeId));
        }

        public override string ToString()
        {
            return $"{(ColorMask.HasValue ? ColorMask.Value.ToString() : "(NoColor)")}/{(Skin.HasValue ? Skin.Value.String : "(NoSkin)")}";
        }
    }

    public struct BlockMaterial
    {
        public readonly Vector3 ColorMask;
        public readonly MyStringHash Skin;

        public BlockMaterial(Vector3 colorMask, MyStringHash skin)
        {
            ColorMask = colorMask;
            Skin = skin;
        }

        public BlockMaterial(IMySlimBlock block)
        {
            ColorMask = block.ColorMaskHSV;
            Skin = block.SkinSubtypeId;
        }

        public bool MaterialEquals(BlockMaterial material)
        {
            return Skin == material.Skin && PaintGunMod.ColorMaskEquals(ColorMask, material.ColorMask);
        }

        public override string ToString()
        {
            return $"{ColorMask.ToString()}/{Skin.String}";
        }
    }

    [ProtoContract]
    public struct SerializedPaintMaterial
    {
        [ProtoMember(1)]
        public readonly Color? Color;

        [ProtoMember(2)]
        public readonly int? SkinIndex;

        public SerializedPaintMaterial(Color? color, int? skinIndex)
        {
            Color = color;
            SkinIndex = skinIndex;
        }

        public SerializedPaintMaterial(PaintMaterial material)
        {
            Color = (material.ColorMask.HasValue ? (Vector3?)PaintGunMod.ColorMaskToRGB(material.ColorMask.Value) : null);

            SkinIndex = null;
            if(material.Skin.HasValue)
            {
                var skin = PaintGunMod.GetSkinInfo(material.Skin.Value);
                SkinIndex = skin.Index;
            }
        }

        public override string ToString()
        {
            return $"{(Color.HasValue ? Color.Value.ToString() : "(NoColor)")}/{(SkinIndex.HasValue ? SkinIndex.Value.ToString() : "(NoSkinIndex)")}";
        }

        public PaintMaterial GetMaterial()
        {
            Vector3? colorMask = (Color.HasValue ? (Vector3?)PaintGunMod.RGBToColorMask(Color.Value) : null);
            MyStringHash? skinSubtype = (SkinIndex.HasValue ? (MyStringHash?)PaintGunMod.instance.BlockSkins[SkinIndex.Value].SubtypeId : null);

            return new PaintMaterial(colorMask, skinSubtype);
        }
    }

    [ProtoContract]
    public struct SerializedBlockMaterial
    {
        [ProtoMember(1)]
        public readonly Color Color;

        [ProtoMember(2)]
        public readonly int SkinIndex;

        public SerializedBlockMaterial(Color color, int skinIndex)
        {
            Color = color;
            SkinIndex = skinIndex;
        }

        public SerializedBlockMaterial(BlockMaterial material)
        {
            Color = PaintGunMod.ColorMaskToRGB(material.ColorMask);

            var skin = PaintGunMod.GetSkinInfo(material.Skin);
            SkinIndex = skin.Index;
        }

        public override string ToString()
        {
            return $"{Color.ToString()}/{SkinIndex.ToString()}";
        }

        public BlockMaterial GetMaterial()
        {
            return new BlockMaterial(PaintGunMod.RGBToColorMask(Color), PaintGunMod.instance.BlockSkins[SkinIndex].SubtypeId);
        }
    }

    public class SkinInfo
    {
        public int Index;
        public readonly MyStringHash SubtypeId;
        public readonly string Name;
        public readonly MyStringId Icon;
        public bool LocallyOwned = true;

        public SkinInfo(MyStringHash subtypeId, string name, string icon)
        {
            SubtypeId = subtypeId;
            Name = name;
            Icon = MyStringId.GetOrCompute(icon);
        }
    }
}

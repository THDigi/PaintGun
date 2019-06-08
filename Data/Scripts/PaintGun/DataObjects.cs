using System;
using System.Collections.Generic;
using ProtoBuf;
using VRage.ModAPI;
using VRageMath;

namespace Digi.PaintGun
{
    public class PlayerColorData
    {
        public ulong SteamId;
        public List<Vector3> Colors;
        public int SelectedSlot = 0;

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
        SELECTED_COLOR_SLOT,
        SET_COLOR,
        UPDATE_COLOR,
        UPDATE_COLOR_LIST,
        REQUEST_COLOR_LIST,
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

        [ProtoMember(4)]
        public uint PackedColor;

        [ProtoMember(5)]
        public uint PackedColor2;

        [ProtoMember(6)]
        public byte Slot;

        [ProtoMember(7)]
        public OddAxis OddAxis;

        [ProtoMember(8)]
        public bool UseGridSystem;

        [ProtoMember(9)]
        public Vector3I? GridPosition;

        [ProtoMember(10)]
        public Vector3I? MirrorPlanes;

        [ProtoMember(11)]
        public uint[] PackedColors;

        public PacketData() { } // empty ctor is required for deserialization

        public override string ToString()
        {
            return $"Type={Type}\n" +
                $"SteamId={SteamId}\n" +
                $"EntityId={EntityId}\n" +
                $"PackedColor={PackedColor}\n" +
                $"PackedColor2={PackedColor2}\n" +
                $"Slot={Slot}\n" +
                $"OddAxis={OddAxis}\n" +
                $"UseGridSystem={UseGridSystem}\n" +
                $"GridPosition={(GridPosition.HasValue ? GridPosition.Value.ToString() : "NULL")}\n" +
                $"MirrorPlanes={(MirrorPlanes.HasValue ? MirrorPlanes.Value.ToString() : "NULL")}\n" +
                $"PackedColors={(PackedColors != null ? string.Join(",", PackedColors) : "NULL")}";
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
}

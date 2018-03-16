using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun
{
    public class PlayerColorData
    {
        public ulong steamId;
        public List<Vector3> colors;
        public int selectedSlot = 0;

        public PlayerColorData(ulong steamId, List<Vector3> colors)
        {
            this.steamId = steamId;
            this.colors = colors;
        }
    }

    public enum PacketAction
    {
        AMMO_REMOVE = 0,
        AMMO_ADD,
        COLOR_PICK_ON,
        COLOR_PICK_OFF,
        PAINT_BLOCK,
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

    public class Particle
    {
        public Color color;
        public Vector3 relativePosition;
        public Vector3 velocity;
        public Vector3 playerVelocity;
        public short life;
        public float radius;
        public float angle;

        public Particle() { }
    }
}

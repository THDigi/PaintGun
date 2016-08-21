using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.Game;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
using VRageMath;
using VRage;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;

using Digi.Utils;

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

    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class PaintGunMod : MySessionComponentBase
    {
        public static PaintGunMod instance = null;

        public bool init = false;
        public bool isThisHostDedicated = false;
        public Settings settings = null;

        public bool pickColorMode = false;
        public bool replaceAllMode = false;
        public bool replaceAllModeInputPressed = false;
        public bool replaceGridSystem = false;
        public long replaceGridSystemTimeout = 0;
        public bool symmetryInput = false;
        public string symmetryStatus = null;
        public MyCubeGrid selectedGrid = null;
        // currently selected grid by the local paint gun
        public IMySlimBlock selectedSlimBlock = null;
        public IMyCharacter selectedCharacter = null;
        public bool selectedInvalid = false;
        public Vector3 prevColorPreview;
        public Vector3 prevCustomColor = DEFAULT_COLOR;
        public IMyHudNotification[] toolStatus = new IMyHudNotification[4];

        private int prevSelectedColorSlot = 0;
        private byte skipColorUpdate = 0;

        public readonly Dictionary<ulong, PlayerColorData> playerColorData = new Dictionary<ulong, PlayerColorData>();
        public PlayerColorData localColorData = null;

        public Dictionary<ulong, IMyEntity> holdingTools = new Dictionary<ulong, IMyEntity>(); // TODO remove ? seems unused
        public PaintGun localHeldTool = null;

        public HashSet<ulong> playersColorPickMode = new HashSet<ulong>();

        private Dictionary<long, MyObjectBuilder_Character> entObjCache = new Dictionary<long, MyObjectBuilder_Character>();

        public const string MOD_NAME = "PaintGun";
        public const string PAINT_GUN_ID = "PaintGun";
        public const string PAINT_MAG_ID = "PaintGunMag";
        public const float PAINT_SPEED = 1.0f;
        public const float DEPAINT_SPEED = 1.5f;
        public const int SKIP_UPDATES = 10;
        public static Vector3 DEFAULT_COLOR = new Vector3(0, -1, 0);
        public const float SAME_COLOR_RANGE = 0.001f;
        public static MyObjectBuilder_AmmoMagazine PAINT_MAG = new MyObjectBuilder_AmmoMagazine()
        {
            SubtypeName = PAINT_MAG_ID,
            ProjectilesCount = 1
        };

        public const ushort PACKET = 9319;
        public const char SEPARATOR = ' ';

        public const int TOOLSTATUS_TIMEOUT = 200;

        public const int COLOR_PALETTE_SIZE = 14;

        public static Color CROSSHAIR_NO_TARGET = new Color(255, 0, 0);
        public static Color CROSSHAIR_BAD_TARGET = new Color(255, 200, 0);
        public static Color CROSSHAIR_TARGET = new Color(0, 255, 0);
        public static Color CROSSHAIR_PAINTING = new Color(0, 255, 155);
        public static readonly MyStringId CROSSHAIR_SPRITEID = MyStringId.GetOrCompute("Default");

        private readonly StringBuilder assigned = new StringBuilder();
        public static HashSet<IMyEntity> ents = new HashSet<IMyEntity>();
        private readonly List<MyCubeGrid> gridsInSystemCache = new List<MyCubeGrid>();
        private readonly List<IMyPlayer> playersCache = new List<IMyPlayer>(0); // always empty

        public void Init()
        {
            init = true;
            instance = this;
            isThisHostDedicated = (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer);

            Log.Init();

            InputHandler.Init();

            MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET, ReceivedPacket);

            if(!isThisHostDedicated) // stuff that shouldn't happen DS-side.
            {
                settings = new Settings();

                MyAPIGateway.Utilities.MessageEntered += MessageEntered;

                if(!MyAPIGateway.Multiplayer.IsServer)
                    SendToServer_RequestColorList(MyAPIGateway.Multiplayer.MyId);
            }
        }

        protected override void UnloadData()
        {
            try
            {
                if(init)
                {
                    init = false;

                    InputHandler.Close();

                    MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET, ReceivedPacket);

                    if(settings != null)
                    {
                        settings.Close();
                        settings = null;
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Log.Close();
        }

        private bool EnsureColorDataEntry(ulong steamId)
        {
            if(!playerColorData.ContainsKey(steamId))
            {
                var emptyColor = new Vector3(0, -1, -1);
                var colors = new List<Vector3>();

                for(int i = 0; i < COLOR_PALETTE_SIZE; i++)
                {
                    colors.Add(emptyColor);
                }

                playerColorData.Add(steamId, new PlayerColorData(steamId, colors));
                return false;
            }

            return true;
        }

        private void ReplaceColorInGrid(MyCubeGrid grid, ulong steamId, Vector3 oldColor, Vector3 newColor, bool useGridSystem)
        {
            gridsInSystemCache.Clear();

            if(useGridSystem)
                grid.GetLogicalGridSystem(gridsInSystemCache);
            else
                gridsInSystemCache.Add(grid);

            int affected = 0;

            foreach(var g in gridsInSystemCache)
            {
                foreach(IMySlimBlock slim in g.CubeBlocks)
                {
                    if(slim.GetColorMask().EqualsToHSV(oldColor))
                    {
                        // GetCubeBlock() is a workaround for MySlimBlock being prohibited
                        g.ChangeColor(g.GetCubeBlock(slim.Position), newColor);
                        affected++;
                    }
                }
            }

            if(MyAPIGateway.Multiplayer.MyId == steamId)
                MyAPIGateway.Utilities.ShowNotification("Painted " + affected + " blocks.", 3000, MyFontEnum.White);
        }

        private void PaintBlock(MyCubeGrid grid, Vector3I pos, Vector3 color)
        {
            grid.ChangeColor(grid.GetCubeBlock(pos), color);
        }

        private void PaintBlockSymmetry(MyCubeGrid grid, Vector3I pos, Vector3 color, Vector3I mirrorPlane, OddAxis odd)
        {
            grid.ChangeColor(grid.GetCubeBlock(pos), color);

            bool oddX = (odd & OddAxis.X) == OddAxis.X;
            bool oddY = (odd & OddAxis.Y) == OddAxis.Y;
            bool oddZ = (odd & OddAxis.Z) == OddAxis.Z;

            var mirrorX = MirrorPaint(grid, 0, mirrorPlane, oddX, pos, color); // X
            var mirrorY = MirrorPaint(grid, 1, mirrorPlane, oddY, pos, color); // Y
            var mirrorZ = MirrorPaint(grid, 2, mirrorPlane, oddZ, pos, color); // Z
            Vector3I? mirrorYZ = null;

            if(mirrorX.HasValue && mirrorPlane.Y > int.MinValue) // XY
                MirrorPaint(grid, 1, mirrorPlane, oddY, mirrorX.Value, color);

            if(mirrorX.HasValue && mirrorPlane.Z > int.MinValue) // XZ
                MirrorPaint(grid, 2, mirrorPlane, oddZ, mirrorX.Value, color);

            if(mirrorY.HasValue && mirrorPlane.Z > int.MinValue) // YZ
                mirrorYZ = MirrorPaint(grid, 2, mirrorPlane, oddZ, mirrorY.Value, color);

            if(mirrorPlane.X > int.MinValue && mirrorYZ.HasValue) // XYZ
                MirrorPaint(grid, 0, mirrorPlane, oddX, mirrorYZ.Value, color);
        }

        public void ReceivedPacket(byte[] bytes)
        {
            try
            {
                int index = 0;

                var type = (PacketAction)bytes[index];
                index += sizeof(byte);

                bool isServer = MyAPIGateway.Multiplayer.IsServer;
                ulong skipSteamId = 0;

                switch(type)
                {
                    case PacketAction.AMMO_REMOVE:
                    case PacketAction.AMMO_ADD:
                        {
                            if(!isServer)
                                return; // server-side only

                            long entId = BitConverter.ToInt64(bytes, index);
                            index += sizeof(long);

                            if(!MyAPIGateway.Entities.EntityExists(entId))
                                return;

                            var ent = MyAPIGateway.Entities.GetEntityById(entId) as MyEntity;
                            var inv = ent.GetInventory(0) as IMyInventory;

                            if(inv == null)
                                return;

                            if(type == PacketAction.AMMO_ADD)
                                inv.AddItems((MyFixedPoint)1, PAINT_MAG);
                            else
                                inv.RemoveItemsOfType((MyFixedPoint)1, PAINT_MAG, false);

                            return; // don't relay to clients
                        }
                    case PacketAction.COLOR_PICK_ON:
                    case PacketAction.COLOR_PICK_OFF:
                        {
                            ulong steamId = BitConverter.ToUInt64(bytes, index);
                            index += sizeof(ulong);

                            if(type == PacketAction.COLOR_PICK_ON)
                            {
                                if(!playersColorPickMode.Contains(steamId))
                                    playersColorPickMode.Add(steamId);
                            }
                            else
                            {
                                playersColorPickMode.Remove(steamId);

                                if(localHeldTool != null)
                                {
                                    localHeldTool.cooldown = DateTime.UtcNow.Ticks;
                                }
                            }

                            break; // relay to clients
                        }
                    case PacketAction.PAINT_BLOCK:
                        {
                            ulong steamId = BitConverter.ToUInt64(bytes, index);
                            index += sizeof(ulong);

                            if(!(MyAPIGateway.Multiplayer.IsServer && steamId == MyAPIGateway.Multiplayer.MyId)) // if sent by server, ignore action because it was already done
                            {
                                long entId = BitConverter.ToInt64(bytes, index);
                                index += sizeof(long);

                                if(!MyAPIGateway.Entities.EntityExists(entId))
                                    return;

                                var ent = MyAPIGateway.Entities.GetEntityById(entId) as MyEntity;
                                var grid = (ent as MyCubeGrid);

                                if(grid == null)
                                    return;

                                var pos = new Vector3I(BitConverter.ToInt32(bytes, index),
                                                       BitConverter.ToInt32(bytes, index + sizeof(int)),
                                                       BitConverter.ToInt32(bytes, index + sizeof(int) * 2));
                                index += sizeof(int) * 3;

                                if(!grid.CubeExists(pos))
                                    return;

                                var color = bytes.BytesToColor(ref index);

                                if(bytes.Length > index) // symmetry paint
                                {
                                    var mirrorPlane = new Vector3I(BitConverter.ToInt32(bytes, index),
                                                                   BitConverter.ToInt32(bytes, index + sizeof(int)),
                                                                   BitConverter.ToInt32(bytes, index + sizeof(int) * 2));
                                    index += sizeof(int) * 3;

                                    var odd = (OddAxis)bytes[index];
                                    index += sizeof(byte);

                                    PaintBlockSymmetry(grid, pos, color, mirrorPlane, odd);
                                }
                                else
                                {
                                    PaintBlock(grid, pos, color);
                                }
                            }

                            skipSteamId = steamId; // skip relaying to this id
                            break; // relay to clients
                        }
                    case PacketAction.BLOCK_REPLACE_COLOR: // replace color on all blocks
                        {
                            ulong steamId = BitConverter.ToUInt64(bytes, index);
                            index += sizeof(ulong);

                            if(!(MyAPIGateway.Multiplayer.IsServer && steamId == MyAPIGateway.Multiplayer.MyId)) // if sent by server, ignore action because it was already done
                            {
                                long entId = BitConverter.ToInt64(bytes, index);
                                index += sizeof(long);

                                if(!MyAPIGateway.Entities.EntityExists(entId))
                                    return;

                                var ent = MyAPIGateway.Entities.GetEntityById(entId) as MyEntity;
                                var grid = (ent as MyCubeGrid);

                                if(grid == null)
                                    return;

                                var oldColor = bytes.BytesToColor(ref index);
                                var newColor = bytes.BytesToColor(ref index);
                                bool useGridSystem = (bytes[index] == 1);
                                index += sizeof(byte);

                                ReplaceColorInGrid(grid, steamId, oldColor, newColor, useGridSystem);
                            }

                            skipSteamId = steamId; // skip relaying to this id
                            break; // relay to clients
                        }
                    case PacketAction.SET_COLOR:
                        {
                            if(!isServer)
                                return; // server-side only

                            ulong steamId = BitConverter.ToUInt64(bytes, index);
                            index += sizeof(ulong);

                            byte slot = bytes[index];
                            index += sizeof(byte);

                            var color = bytes.BytesToColor(ref index);

                            EnsureColorDataEntry(steamId);

                            playerColorData[steamId].colors[slot] = color;

                            foreach(var kv in MyCubeBuilder.AllPlayersColors)
                            {
                                var keyString = kv.Key.ToString();
                                var id = ulong.Parse(keyString.Substring(0, keyString.IndexOf(':')));

                                if(steamId == id)
                                {
                                    kv.Value[slot] = color;
                                    SendToAllPlayers_UpdateColor(steamId, slot, color);
                                    break;
                                }
                            }

                            return; // don't relay to clients
                        }
                    case PacketAction.SELECTED_COLOR_SLOT:
                        {
                            ulong steamId = BitConverter.ToUInt64(bytes, index);
                            index += sizeof(ulong);

                            byte slot = bytes[index];
                            index += sizeof(byte);

                            EnsureColorDataEntry(steamId);

                            playerColorData[steamId].selectedSlot = slot;

                            if(localHeldTool != null)
                            {
                                localHeldTool.SetToolColor(playerColorData[steamId].colors[slot]);
                            }

                            skipSteamId = steamId; // skip relaying to this id
                            break; // relay to clients
                        }
                    case PacketAction.REQUEST_COLOR_LIST:
                        {
                            if(!isServer)
                                return; // server-side only

                            ulong steamId = BitConverter.ToUInt64(bytes, index);
                            index += sizeof(ulong);

                            // send all online players' colors to the connected player
                            foreach(var kv in playerColorData)
                            {
                                if(IsPlayerOnline(kv.Key))
                                    SendToPlayer_SendColorList(steamId, kv.Key, kv.Value.selectedSlot, kv.Value.colors);
                            }

                            if(EnsureColorDataEntry(steamId)) // send this player's colors to everyone if available, otherwise they'll be sent automatically when they are available
                            {
                                var cd = playerColorData[steamId];
                                SendToPlayer_SendColorList(0, steamId, cd.selectedSlot, cd.colors);
                            }

                            return; // don't relay to clients
                        }
                    case PacketAction.UPDATE_COLOR:
                        {
                            if(isServer)
                                return; // client-side only

                            ulong steamId = BitConverter.ToUInt64(bytes, index);
                            index += sizeof(ulong);

                            byte slot = bytes[index];
                            index += sizeof(byte);

                            var color = bytes.BytesToColor(ref index);

                            EnsureColorDataEntry(steamId);

                            playerColorData[steamId].colors[slot] = color;

                            return; // don't relay (not that it even can since it's clientside only)
                        }
                    case PacketAction.UPDATE_COLOR_LIST:
                        {
                            if(isServer)
                                return; // client-side only

                            ulong steamId = BitConverter.ToUInt64(bytes, index);
                            index += sizeof(ulong);

                            byte slot = bytes[index];
                            index += sizeof(byte);

                            EnsureColorDataEntry(steamId);

                            var cd = playerColorData[steamId];

                            for(int i = 0; i < COLOR_PALETTE_SIZE; i++)
                            {
                                cd.colors[i] = bytes.BytesToColor(ref index);
                            }

                            return; // don't relay (not that it even can since it's clientside only)
                        }
                }

                // relay packet to clients if type allows it
                if(isServer && MyAPIGateway.Players.Count > 1)
                {
                    var myId = MyAPIGateway.Multiplayer.MyId;

                    MyAPIGateway.Players.GetPlayers(playersCache, delegate (IMyPlayer p)
                                                    {
                                                        if(myId == p.SteamUserId) // don't re-send to yourself
                                                            return false;

                                                        if(skipSteamId > 0 && skipSteamId == p.SteamUserId) // don't send to the skipped ID
                                                            return false;

                                                        MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, p.SteamUserId, true);
                                                        return false;
                                                    });
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_Ammo(long entId, bool addItem)
        {
            try
            {
                int len = sizeof(byte) + sizeof(long);

                var bytes = new byte[len];
                bytes[0] = (byte)(addItem ? PacketAction.AMMO_ADD : PacketAction.AMMO_REMOVE);
                len = sizeof(byte);

                var data = BitConverter.GetBytes(entId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_PaintBlock(long entId, MyCubeGrid grid, Vector3I pos, Vector3 color, bool useSymmetry = false, Vector3I mirrorPlane = default(Vector3I), OddAxis odd = OddAxis.NONE)
        {
            try
            {
                int len = sizeof(byte) + sizeof(ulong) + sizeof(long) + sizeof(int) * 3 + sizeof(short) + sizeof(sbyte) * 2;

                if(useSymmetry)
                    len += sizeof(int) * 3 + sizeof(byte);

                var bytes = new byte[len];
                bytes[0] = (byte)PacketAction.PAINT_BLOCK;
                len = sizeof(byte);

                var data = BitConverter.GetBytes(MyAPIGateway.Multiplayer.MyId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                data = BitConverter.GetBytes(entId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                data = BitConverter.GetBytes(pos.X);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                data = BitConverter.GetBytes(pos.Y);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                data = BitConverter.GetBytes(pos.Z);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                color.ColorToBytes(bytes, ref len);

                if(useSymmetry)
                {
                    data = BitConverter.GetBytes(mirrorPlane.X);
                    Array.Copy(data, 0, bytes, len, data.Length);
                    len += data.Length;

                    data = BitConverter.GetBytes(mirrorPlane.Y);
                    Array.Copy(data, 0, bytes, len, data.Length);
                    len += data.Length;

                    data = BitConverter.GetBytes(mirrorPlane.Z);
                    Array.Copy(data, 0, bytes, len, data.Length);
                    len += data.Length;

                    bytes[len] = (byte)odd;
                    len += sizeof(byte);
                }

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);

                // do the action locally as well
                if(useSymmetry)
                    PaintBlockSymmetry(grid, pos, color, mirrorPlane, odd);
                else
                    PaintBlock(grid, pos, color);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_ReplaceColor(long entId, MyCubeGrid grid, Vector3 oldColor, Vector3 newColor, bool useGridSystem)
        {
            try
            {
                int len = sizeof(byte) + sizeof(long) + sizeof(ulong) + sizeof(short) * 2 + sizeof(sbyte) * 4 + sizeof(bool);

                var bytes = new byte[len];
                bytes[0] = (byte)PacketAction.BLOCK_REPLACE_COLOR;
                len = sizeof(byte);

                var data = BitConverter.GetBytes(MyAPIGateway.Multiplayer.MyId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                data = BitConverter.GetBytes(entId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                oldColor.ColorToBytes(bytes, ref len);
                newColor.ColorToBytes(bytes, ref len);

                bytes[len] = (byte)(useGridSystem ? 1 : 0);
                len += sizeof(byte);

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);

                // do the action locally as well
                ReplaceColorInGrid(grid, MyAPIGateway.Multiplayer.MyId, oldColor, newColor, useGridSystem);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_SelectedColorSlot(int slot)
        {
            try
            {
                int len = sizeof(byte) + sizeof(ulong) + sizeof(byte);

                var bytes = new byte[len];
                bytes[0] = (byte)PacketAction.SELECTED_COLOR_SLOT;
                len = sizeof(byte);

                var data = BitConverter.GetBytes(MyAPIGateway.Multiplayer.MyId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                bytes[len] = (byte)slot;
                len += sizeof(byte);

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_ColorPickMode(bool mode)
        {
            try
            {
                int len = sizeof(byte) + sizeof(ulong);

                var bytes = new byte[len];
                bytes[0] = (byte)(mode ? PacketAction.COLOR_PICK_ON : PacketAction.COLOR_PICK_OFF);
                len = sizeof(byte);

                var data = BitConverter.GetBytes(MyAPIGateway.Multiplayer.MyId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);

                pickColorMode = mode;
                prevColorPreview = GetBuildColor();

                entObjCache.Clear();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public bool SendToServer_SetColor(int slot, Vector3 color, bool checkAndSelect)
        {
            try
            {
                var myId = MyAPIGateway.Multiplayer.MyId;
                PlayerColorData cd;

                if(checkAndSelect && playerColorData.TryGetValue(myId, out cd))
                {
                    for(int i = 0; i < cd.colors.Count; i++)
                    {
                        if(cd.colors[i].EqualsToHSV(color))
                        {
                            localColorData.selectedSlot = i;
                            SendToServer_SelectedColorSlot(i);

                            MyAPIGateway.Utilities.ShowNotification("Color already exists in slot " + i + ".", 3000, MyFontEnum.Red);

                            return false; // color exists in palette, stop sending.
                        }
                    }
                }

                int len = sizeof(byte) + sizeof(ulong) + sizeof(byte) + sizeof(short) + sizeof(sbyte) * 2;

                var bytes = new byte[len];
                bytes[0] = (byte)PacketAction.SET_COLOR;
                len = sizeof(byte);

                var data = BitConverter.GetBytes(myId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                bytes[len] = (byte)slot;
                len += sizeof(byte);

                color.ColorToBytes(bytes, ref len);

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);

                // TODO find a way to set client's color on the color picker menu

                return true;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
        }

        public void SendToServer_RequestColorList(ulong steamId)
        {
            try
            {
                int len = sizeof(byte) + sizeof(ulong) + sizeof(byte);

                var bytes = new byte[len];
                bytes[0] = (byte)PacketAction.REQUEST_COLOR_LIST;
                len = sizeof(byte);

                var data = BitConverter.GetBytes(steamId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToAllPlayers_UpdateColor(ulong colorOwner, int slot, Vector3 color)
        {
            try
            {
                if(!MyAPIGateway.Multiplayer.IsServer) // server side only
                    return;

                int len = sizeof(byte) + sizeof(ulong) + sizeof(byte) + sizeof(short) + sizeof(sbyte) * 2;

                var bytes = new byte[len];
                bytes[0] = (byte)PacketAction.UPDATE_COLOR;
                len = sizeof(byte);

                var data = BitConverter.GetBytes(colorOwner);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                bytes[len] = (byte)slot;
                len += sizeof(byte);

                color.ColorToBytes(bytes, ref len);

                var myId = MyAPIGateway.Multiplayer.MyId;

                MyAPIGateway.Players.GetPlayers(playersCache, delegate (IMyPlayer p)
                {
                    if(myId == p.SteamUserId) // don't re-send to yourself
                        return false;

                    MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, p.SteamUserId, true);
                    return false;
                });
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToPlayer_SendColorList(ulong sendTo, ulong colorOwner, int slot, List<Vector3> colorList)
        {
            try
            {
                if(!MyAPIGateway.Multiplayer.IsServer) // server side only
                    return;

                if(colorList.Count != COLOR_PALETTE_SIZE)
                {
                    Log.Error("Size of color palette does not match the defined palette: list=" + colorList.Count + ", COLOR_PALETTE_SIZE=" + COLOR_PALETTE_SIZE);
                    return;
                }

                int len = sizeof(byte) + sizeof(ulong) + sizeof(byte) + (sizeof(short) * COLOR_PALETTE_SIZE) + (sizeof(sbyte) * 2 * COLOR_PALETTE_SIZE);

                var bytes = new byte[len];
                bytes[0] = (byte)PacketAction.UPDATE_COLOR_LIST;
                len = sizeof(byte);

                var data = BitConverter.GetBytes(colorOwner);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;

                bytes[len] = (byte)slot;
                len += sizeof(byte);

                for(int i = 0; i < colorList.Count; i++)
                {
                    colorList[i].ColorToBytes(bytes, ref len);
                }

                if(sendTo == 0)
                {
                    var myId = MyAPIGateway.Multiplayer.MyId;

                    MyAPIGateway.Players.GetPlayers(playersCache, delegate (IMyPlayer p)
                    {
                        if(myId == p.SteamUserId) // don't re-send to yourself
                            return false;

                        MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, p.SteamUserId, true);
                        return false;
                    });
                }
                else
                {
                    MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, sendTo, true);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private Vector3I? MirrorPaint(MyCubeGrid g, int axis, Vector3I mirror, bool odd, Vector3I originalPosition, Vector3 color)
        {
            switch(axis)
            {
                case 0:
                    if(mirror.X > int.MinValue)
                    {
                        var mirrorX = originalPosition + new Vector3I(((mirror.X - originalPosition.X) * 2) - (odd ? 1 : 0), 0, 0);

                        if(g.CubeExists(mirrorX))
                            g.ChangeColor(g.GetCubeBlock(mirrorX), color);

                        return mirrorX;
                    }
                    break;

                case 1:
                    if(mirror.Y > int.MinValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((mirror.Y - originalPosition.Y) * 2) - (odd ? 1 : 0), 0);

                        if(g.CubeExists(mirrorY))
                            g.ChangeColor(g.GetCubeBlock(mirrorY), color);

                        return mirrorY;
                    }
                    break;

                case 2:
                    if(mirror.Z > int.MinValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((mirror.Z - originalPosition.Z) * 2) + (odd ? 1 : 0)); // reversed on odd

                        if(g.CubeExists(mirrorZ))
                            g.ChangeColor(g.GetCubeBlock(mirrorZ), color);

                        return mirrorZ;
                    }
                    break;
            }

            return null;
        }

        private bool MirrorCheckSameColor(MyCubeGrid g, int axis, Vector3I originalPosition, Vector3 color, out Vector3I? mirror)
        {
            mirror = null;

            switch(axis)
            {
                case 0:
                    if(g.XSymmetryPlane.HasValue)
                    {
                        var mirrorX = originalPosition + new Vector3I(((g.XSymmetryPlane.Value.X - originalPosition.X) * 2) - (g.XSymmetryOdd ? 1 : 0), 0, 0);
                        var slim = ((IMyCubeGrid)g).GetCubeBlock(mirrorX);
                        mirror = mirrorX;

                        if(slim != null)
                            return slim.GetColorMask().EqualsToHSV(color);
                    }
                    break;

                case 1:
                    if(g.YSymmetryPlane.HasValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((g.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (g.YSymmetryOdd ? 1 : 0), 0);
                        var slim = ((IMyCubeGrid)g).GetCubeBlock(mirrorY);
                        mirror = mirrorY;

                        if(slim != null)
                            return slim.GetColorMask().EqualsToHSV(color);
                    }
                    break;

                case 2:
                    if(g.ZSymmetryPlane.HasValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((g.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (g.ZSymmetryOdd ? 1 : 0)); // reversed on odd
                        var slim = ((IMyCubeGrid)g).GetCubeBlock(mirrorZ);
                        mirror = mirrorZ;

                        if(slim != null)
                            return slim.GetColorMask().EqualsToHSV(color);
                    }
                    break;
            }

            return true;
        }

        private Vector3I? MirrorHighlight(MyCubeGrid g, int axis, Vector3I originalPosition)
        {
            switch(axis)
            {
                case 0:
                    if(g.XSymmetryPlane.HasValue)
                    {
                        var mirrorX = originalPosition + new Vector3I(((g.XSymmetryPlane.Value.X - originalPosition.X) * 2) - (g.XSymmetryOdd ? 1 : 0), 0, 0);

                        if(g.CubeExists(mirrorX))
                            MyCubeBuilder.DrawSemiTransparentBox(g, g.GetCubeBlock(mirrorX), Color.White, true, selectedInvalid ? "GizmoDrawLineRed" : "GizmoDrawLine", null);

                        return mirrorX;
                    }
                    break;

                case 1:
                    if(g.YSymmetryPlane.HasValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((g.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (g.YSymmetryOdd ? 1 : 0), 0);

                        if(g.CubeExists(mirrorY))
                            MyCubeBuilder.DrawSemiTransparentBox(g, g.GetCubeBlock(mirrorY), Color.White, true, selectedInvalid ? "GizmoDrawLineRed" : "GizmoDrawLine", null);

                        return mirrorY;
                    }
                    break;

                case 2:
                    if(g.ZSymmetryPlane.HasValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((g.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (g.ZSymmetryOdd ? 1 : 0)); // reversed on odd

                        if(g.CubeExists(mirrorZ))
                            MyCubeBuilder.DrawSemiTransparentBox(g, g.GetCubeBlock(mirrorZ), Color.White, true, selectedInvalid ? "GizmoDrawLineRed" : "GizmoDrawLine", null);

                        return mirrorZ;
                    }
                    break;
            }

            return null;
        }

        public override void Draw()
        {
            try
            {
                if(!init)
                    return;

                if(settings.hidePaletteWithHud && MyAPIGateway.Session.Config.MinimalHud)
                    return;

                if(localHeldTool != null && localColorData != null)
                {
                    var cam = MyAPIGateway.Session.Camera;
                    var camMatrix = cam.WorldMatrix;

                    var viewProjectionMatrixInv = MatrixD.Invert(cam.ViewMatrix * cam.ProjectionMatrix);
                    var pos = Vector3D.Transform(settings.paletteScreenPos, viewProjectionMatrixInv);

                    const int MIN_FOV = 40;
                    var FOV = MathHelper.ToDegrees(cam.FovWithZoom);
                    float scaleFOV = 1;

                    if(FOV > 90)
                    {
                        var FOVRatio = (FOV - 90) / (140 - 90);
                        scaleFOV = MathHelper.Lerp(1.1f, 3f, FOVRatio);
                    }
                    else
                    {
                        var FOVRatio = (FOV - MIN_FOV) / (90 - MIN_FOV);
                        scaleFOV = MathHelper.Lerp(0.4f, 1.1f, FOVRatio);
                    }

                    scaleFOV *= settings.paletteScale;

                    float SQUARE_WIDTH = 0.0014f * scaleFOV;
                    float SQUARE_HEIGHT = 0.0011f * scaleFOV;
                    float SQUARE_SELECTED_WIDTH = (SQUARE_WIDTH + 0.0002f) * scaleFOV;
                    float SQUARE_SELECTED_HEIGHT = (SQUARE_HEIGHT + 0.0002f) * scaleFOV;
                    const double SPACING_ADD = 0.0006;
                    double SPACING_WIDTH = (SQUARE_WIDTH * 2) + SPACING_ADD;
                    double SPACING_HEIGHT = (SQUARE_HEIGHT * 2) + SPACING_ADD;
                    const int MIDDLE_INDEX = 7;
                    MyQuadD quad;

                    pos += camMatrix.Left * (SPACING_WIDTH * (MIDDLE_INDEX / 2)) + camMatrix.Up * (SPACING_HEIGHT / 2);

                    for(int i = 0; i < localColorData.colors.Count; i++)
                    {
                        var v = localColorData.colors[i];
                        var c = HSVtoRGB(v) * 0.5f;

                        if(i == MIDDLE_INDEX)
                            pos += camMatrix.Left * (SPACING_WIDTH * MIDDLE_INDEX) + camMatrix.Down * SPACING_HEIGHT;

                        MyUtils.GenerateQuad(out quad, ref pos, SQUARE_WIDTH, SQUARE_HEIGHT, ref camMatrix);
                        MyTransparentGeometry.AddQuad("Square", ref quad, c, ref pos);

                        if(i == localColorData.selectedSlot)
                        {
                            MyUtils.GenerateQuad(out quad, ref pos, SQUARE_SELECTED_WIDTH, SQUARE_SELECTED_HEIGHT, ref camMatrix);
                            MyTransparentGeometry.AddQuad("PaintGunSelectedColor", ref quad, Color.White, ref pos, 0, -1);
                        }

                        pos += camMatrix.Right * SPACING_WIDTH;
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void SetToolColor(Vector3 color)
        {
            if(localHeldTool != null)
            {
                localHeldTool.SetToolColor(color);
            }
        }

        private bool CheckColorList(ulong steamId, List<Vector3> oldList, List<Vector3> newList)
        {
            bool changed = false;

            for(byte i = 0; i < oldList.Count; i++)
            {
                if(!oldList[i].EqualsToHSV(newList[i]))
                {
                    SendToAllPlayers_UpdateColor(steamId, i, newList[i]);
                    changed = true;
                }
            }

            return changed;
        }

        public string GetPlayerName(ulong steamId)
        {
            var list = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(list, (p) => p.SteamUserId == steamId);
            return (list.Count > 0 ? list[0].DisplayName : "(not found)");
        }

        public bool IsPlayerOnline(ulong steamId)
        {
            var list = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(list, (p) => p.SteamUserId == steamId);
            return list.Count > 0;
        }

        private ulong GetSteamId(string playerIdstring)
        {
            return ulong.Parse(playerIdstring.Substring(0, playerIdstring.IndexOf(':')));
        }

        public override void HandleInput()
        {
            try
            {
                if(!init)
                    return;

                InputHandler.Update();

                if(localHeldTool != null && InputHandler.IsInputReadable())
                {
                    if(symmetryInput && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
                    {
                        MyAPIGateway.CubeBuilder.UseSymmetry = !MyAPIGateway.CubeBuilder.UseSymmetry;
                    }

                    if(replaceAllMode && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
                    {
                        replaceGridSystem = !replaceGridSystem;
                    }

                    if(!MyAPIGateway.Input.IsKeyPress(MyKeys.Alt))
                    {
                        int change = 0;

                        if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_LEFT))
                            change = 1;
                        else if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_RIGHT))
                            change = -1;
                        else
                            change = MyAPIGateway.Input.DeltaMouseScrollWheelValue();

                        if(change != 0 && localColorData != null)
                        {
                            if(settings.extraSounds)
                                PlaySound("HudClick", 0.1f);

                            if(change < 0)
                            {
                                if(settings.selectColorZigZag)
                                {
                                    if(localColorData.selectedSlot >= 13)
                                        localColorData.selectedSlot = 0;
                                    else if(localColorData.selectedSlot >= 7)
                                        localColorData.selectedSlot -= 6;
                                    else
                                        localColorData.selectedSlot += 7;
                                }
                                else
                                {
                                    if(++localColorData.selectedSlot >= localColorData.colors.Count)
                                        localColorData.selectedSlot = 0;
                                }
                            }
                            else
                            {
                                if(settings.selectColorZigZag)
                                {
                                    if(localColorData.selectedSlot >= 7)
                                        localColorData.selectedSlot -= 7;
                                    else
                                        localColorData.selectedSlot += 6;
                                }
                                else
                                {
                                    if(--localColorData.selectedSlot < 0)
                                        localColorData.selectedSlot = (localColorData.colors.Count - 1);
                                }
                            }

                            SetToolColor(localColorData.colors[localColorData.selectedSlot]);
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if(!init)
                {
                    if(MyAPIGateway.Session == null || MyAPIGateway.Multiplayer == null)
                        return;

                    Init();
                }

                if(++skipColorUpdate > 10)
                {
                    skipColorUpdate = 0;

                    if(MyAPIGateway.Multiplayer.IsServer)
                    {
                        foreach(var kv in MyCubeBuilder.AllPlayersColors)
                        {
                            var steamId = GetSteamId(kv.Key.ToString());

                            if(playerColorData.ContainsKey(steamId))
                            {
                                var cd = playerColorData[steamId];

                                // send only changed colors
                                if(CheckColorList(steamId, cd.colors, kv.Value))
                                {
                                    cd.colors.Clear();
                                    cd.colors.AddList(kv.Value); // add them separately to avoid the reference being automatically updated and not detecting changes
                                }
                            }
                            else
                            {
                                playerColorData.Add(steamId, new PlayerColorData(steamId, new List<Vector3>(kv.Value))); // new list to not use the same reference, reason noted before

                                // send all colors if player is online
                                if(IsPlayerOnline(steamId))
                                {
                                    var myId = MyAPIGateway.Multiplayer.MyId;

                                    MyAPIGateway.Players.GetPlayers(playersCache, delegate (IMyPlayer p)
                                    {
                                        if(myId == p.SteamUserId) // don't re-send to yourself
                                            return false;

                                        SendToPlayer_SendColorList(p.SteamUserId, steamId, 0, kv.Value);
                                        return false;
                                    });
                                }
                            }
                        }
                    }

                    if(localColorData == null && !playerColorData.TryGetValue(MyAPIGateway.Multiplayer.MyId, out localColorData))
                        localColorData = null;

                    if(localColorData != null && localColorData.selectedSlot != prevSelectedColorSlot)
                    {
                        prevSelectedColorSlot = localColorData.selectedSlot;
                        SendToServer_SelectedColorSlot(localColorData.selectedSlot);
                    }
                }

                if(localHeldTool != null)
                {
                    var grid = selectedGrid;

                    if(symmetryInput)
                    {
                        if(MyAPIGateway.CubeBuilder.UseSymmetry && grid != null && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue))
                        {
                            var matrix = grid.WorldMatrix;
                            var quad = new MyQuadD();
                            Vector3D gridSize = (Vector3I.One + (grid.Max - grid.Min)) * grid.GridSizeHalf;
                            const float alpha = 0.4f;
                            const string material = "SquareIgnoreDepth";

                            if(grid.XSymmetryPlane.HasValue)
                            {
                                var center = matrix.Translation + matrix.Right * ((grid.XSymmetryPlane.Value.X * grid.GridSize) - (grid.XSymmetryOdd ? grid.GridSizeHalf : 0));

                                var minY = matrix.Up * ((grid.Min.Y - 1.5f) * grid.GridSize);
                                var maxY = matrix.Up * ((grid.Max.Y + 1.5f) * grid.GridSize);
                                var minZ = matrix.Backward * ((grid.Min.Z - 1.5f) * grid.GridSize);
                                var maxZ = matrix.Backward * ((grid.Max.Z + 1.5f) * grid.GridSize);

                                quad.Point0 = center + maxY + maxZ;
                                quad.Point1 = center + maxY + minZ;
                                quad.Point2 = center + minY + minZ;
                                quad.Point3 = center + minY + maxZ;

                                MyTransparentGeometry.AddQuad(material, ref quad, Color.Red * alpha, ref center, 0, -1);
                            }

                            if(grid.YSymmetryPlane.HasValue)
                            {
                                var center = matrix.Translation + matrix.Up * ((grid.YSymmetryPlane.Value.Y * grid.GridSize) - (grid.YSymmetryOdd ? grid.GridSizeHalf : 0));

                                var minZ = matrix.Backward * ((grid.Min.Z - 1.5f) * grid.GridSize);
                                var maxZ = matrix.Backward * ((grid.Max.Z + 1.5f) * grid.GridSize);
                                var minX = matrix.Right * ((grid.Min.X - 1.5f) * grid.GridSize);
                                var maxX = matrix.Right * ((grid.Max.X + 1.5f) * grid.GridSize);

                                quad.Point0 = center + maxZ + maxX;
                                quad.Point1 = center + maxZ + minX;
                                quad.Point2 = center + minZ + minX;
                                quad.Point3 = center + minZ + maxX;

                                MyTransparentGeometry.AddQuad(material, ref quad, Color.Green * alpha, ref center, 0, -1);
                            }

                            if(grid.ZSymmetryPlane.HasValue)
                            {
                                var center = matrix.Translation + matrix.Backward * ((grid.ZSymmetryPlane.Value.Z * grid.GridSize) + (grid.ZSymmetryOdd ? grid.GridSizeHalf : 0));

                                var minY = matrix.Up * ((grid.Min.Y - 1.5f) * grid.GridSize);
                                var maxY = matrix.Up * ((grid.Max.Y + 1.5f) * grid.GridSize);
                                var minX = matrix.Right * ((grid.Min.X - 1.5f) * grid.GridSize);
                                var maxX = matrix.Right * ((grid.Max.X + 1.5f) * grid.GridSize);

                                quad.Point0 = center + maxY + maxX;
                                quad.Point1 = center + maxY + minX;
                                quad.Point2 = center + minY + minX;
                                quad.Point3 = center + minY + maxX;

                                MyTransparentGeometry.AddQuad(material, ref quad, Color.Blue * alpha, ref center, 0, -1);
                            }
                        }
                    }

                    if(selectedSlimBlock != null)
                    {
                        if(selectedSlimBlock.IsDestroyed || selectedSlimBlock.IsFullyDismounted)
                        {
                            selectedSlimBlock = null;
                        }
                        else
                        {
                            // workaround not being able to cast to MySlimBlock with GetCubeBlock()
                            var internalGrid = selectedSlimBlock.CubeGrid as MyCubeGrid;
                            MyCubeBuilder.DrawSemiTransparentBox(internalGrid, internalGrid.GetCubeBlock(selectedSlimBlock.Position), Color.White, true, selectedInvalid ? "GizmoDrawLineRed" : "GizmoDrawLine", null);

                            // symmetry highlight
                            if(MyAPIGateway.Session.CreativeMode && MyCubeBuilder.Static.UseSymmetry && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue))
                            {
                                var mirrorX = MirrorHighlight(grid, 0, selectedSlimBlock.Position); // X
                                var mirrorY = MirrorHighlight(grid, 1, selectedSlimBlock.Position); // Y
                                var mirrorZ = MirrorHighlight(grid, 2, selectedSlimBlock.Position); // Z
                                Vector3I? mirrorYZ = null;

                                if(mirrorX.HasValue && grid.YSymmetryPlane.HasValue) // XY
                                    MirrorHighlight(grid, 1, mirrorX.Value);

                                if(mirrorX.HasValue && grid.ZSymmetryPlane.HasValue) // XZ
                                    MirrorHighlight(grid, 2, mirrorX.Value);

                                if(mirrorY.HasValue && grid.ZSymmetryPlane.HasValue) // YZ
                                    mirrorYZ = MirrorHighlight(grid, 2, mirrorY.Value);

                                if(grid.XSymmetryPlane.HasValue && mirrorYZ.HasValue) // XYZ
                                    MirrorHighlight(grid, 0, mirrorYZ.Value);
                            }
                        }
                    }

                    if(selectedCharacter != null)
                    {
                        if(selectedCharacter.MarkedForClose || selectedCharacter.Closed)
                        {
                            selectedCharacter = null;
                        }
                        else if(selectedCharacter.Visible)
                        {
                            var matrix = selectedCharacter.WorldMatrix;
                            var box = (BoundingBoxD)selectedCharacter.LocalAABB;
                            var color = Color.Green;
                            var worldToLocal = selectedCharacter.WorldMatrixInvScaled;

                            MySimpleObjectDraw.DrawAttachedTransparentBox(ref matrix, ref box, ref color, selectedCharacter.Render.GetRenderObjectID(), ref worldToLocal, MySimpleObjectRasterizer.Wireframe, 1, 0.05f, null, "GizmoDrawLine", false, 0);
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SetToolStatus(int line, string text, MyFontEnum font = MyFontEnum.White, int aliveTime = TOOLSTATUS_TIMEOUT)
        {
            if(text == null)
            {
                if(toolStatus[line] != null)
                    toolStatus[line].Hide();

                return;
            }

            if(toolStatus[line] == null)
                toolStatus[line] = MyAPIGateway.Utilities.CreateNotification(text, aliveTime, font);

            toolStatus[line].Font = font;
            toolStatus[line].Text = text;
            toolStatus[line].AliveTime = aliveTime;
            toolStatus[line].Show();
        }

        public static string ColorToString(Vector3 hsv)
        {
            return "Hue: " + Math.Round(hsv.X * 360) + "°, saturation: " + Math.Round(hsv.Y * 100) + ", value: " + Math.Round(hsv.Z * 100);
        }

        public static string ColorToStringShort(Vector3 hsv)
        {
            return "HSV: " + Math.Round(hsv.X * 360) + "°, " + Math.Round(hsv.Y * 100) + ", " + Math.Round(hsv.Z * 100);
        }

        public static void SetCrosshairColor(Color? color)
        {
            // FIXME whitelist broke this
            //if(color.HasValue)
            //    MyHud.Crosshair.AddTemporarySprite(MyHudTexturesEnum.crosshair, CROSSHAIR_SPRITEID, 1000, 500, color.Value, 0.02f);
            //else
            //    MyHud.Crosshair.ResetToDefault(true);
        }

        public Vector3 GetBuildColor()
        {
            return (localColorData != null ? localColorData.colors[localColorData.selectedSlot] : DEFAULT_COLOR);
        }

        public static void PlaySound(string name, float volume)
        {
            var emitter = new MyEntity3DSoundEmitter(MyAPIGateway.Session.ControlledObject.Entity as MyEntity);
            emitter.CustomVolume = volume;
            emitter.PlaySingleSound(new MySoundPair(name));
        }

        private bool IsBlockValid(IMySlimBlock block, Vector3 color, bool trigger, out string blockName, out Vector3 blockColor)
        {
            if(block == null)
            {
                if(pickColorMode)
                {
                    SetToolStatus(0, "Aim at a block or player and click to pick its color.", MyFontEnum.Blue);
                    SetToolStatus(1, null);
                    SetToolStatus(2, null);
                    SetToolStatus(3, null);
                }
                else if(replaceAllMode)
                {
                    SetToolStatus(0, "Aim at a block to replace its color from the entire grid.", MyFontEnum.Blue);
                    SetToolStatus(1, null);
                    SetToolStatus(2, null);
                    SetToolStatus(3, null);
                }
                else if(trigger)
                {
                    SetToolStatus(0, "Aim at a block to paint it.", MyFontEnum.Red);
                    SetToolStatus(1, null);
                    SetToolStatus(2, symmetryStatus, MyFontEnum.DarkBlue);
                    SetToolStatus(3, null);
                }

                blockName = null;
                blockColor = DEFAULT_COLOR;
                return false;
            }

            selectedSlimBlock = block;
            selectedInvalid = false;
            blockColor = block.GetColorMask();
            blockName = (block.FatBlock == null ? block.ToString() : block.FatBlock.DefinitionDisplayNameText);

            if(pickColorMode)
            {
                if(trigger)
                {
                    SendToServer_ColorPickMode(false);

                    if(SendToServer_SetColor((byte)localColorData.selectedSlot, blockColor, true))
                    {
                        PlaySound("HudMouseClick", 0.25f);
                        MyAPIGateway.Utilities.ShowNotification("Color in slot " + localColorData.selectedSlot + " set to " + ColorToStringShort(blockColor), 2000, MyFontEnum.White);
                    }
                    else
                    {
                        PlaySound("HudUnable", 0.25f);
                    }

                    SetToolStatus(0, null);
                    SetToolStatus(1, null);
                    SetToolStatus(2, null);
                    SetToolStatus(3, null);
                }
                else
                {
                    SetCrosshairColor(HSVtoRGB(blockColor));

                    if(!blockColor.EqualsToHSV(prevColorPreview))
                    {
                        prevColorPreview = blockColor;
                        SetToolColor(blockColor);

                        if(settings.extraSounds)
                            PlaySound("HudItem", 0.75f);
                    }

                    SetToolStatus(0, "Click to pick the color.", MyFontEnum.Green);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, ColorToStringShort(blockColor), MyFontEnum.White);
                    SetToolStatus(3, null);
                }

                return false;
            }

            if(replaceAllMode)
            {
                if(blockColor.EqualsToHSV(color))
                {
                    SetCrosshairColor(CROSSHAIR_BAD_TARGET);
                    selectedInvalid = true;

                    SetToolStatus(0, "Already painted this color.", MyFontEnum.Red);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, symmetryStatus, MyFontEnum.DarkBlue);
                    SetToolStatus(3, null);

                    return false;
                }

                SetCrosshairColor(CROSSHAIR_TARGET);

                var control = MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY);

                SetToolStatus(0, "Click to replace this color on all blocks.", MyFontEnum.Green);
                SetToolStatus(1, ColorToStringShort(blockColor), MyFontEnum.White);
                SetToolStatus(2, (replaceGridSystem ? "Replace on all connected grids" : "Replaces only on the selected grid") + ", press " + InputHandler.GetFriendlyStringForControl(control) + " to toggle.", (replaceGridSystem ? MyFontEnum.Red : MyFontEnum.DarkBlue));
                SetToolStatus(3, null);

                return true;
            }

            if(!MyAPIGateway.Session.CreativeMode && block.CurrentDamage > (block.MaxIntegrity / 10.0f) || (block.FatBlock != null && !block.FatBlock.IsFunctional))
            {
                if(trigger)
                    PlaySound("HudUnable", 0.5f);

                SetCrosshairColor(CROSSHAIR_BAD_TARGET);
                selectedInvalid = true;

                SetToolStatus(0, (block.FatBlock != null && !block.FatBlock.IsFunctional ? "Block not fully built!" : "Block is damaged!"), MyFontEnum.Red);
                SetToolStatus(1, blockName, MyFontEnum.White);
                SetToolStatus(2, null);
                SetToolStatus(3, null);

                return false;
            }

            var grid = block.CubeGrid as MyCubeGrid;
            bool symmetry = MyAPIGateway.Session.CreativeMode && MyCubeBuilder.Static.UseSymmetry && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue);
            bool symmetrySameColor = true;

            if(blockColor.EqualsToHSV(color))
            {
                if(symmetry)
                {
                    Vector3I? mirrorX = null;
                    Vector3I? mirrorY = null;
                    Vector3I? mirrorZ = null;
                    Vector3I? mirrorYZ = null;

                    // NOTE: do not optimize, all methods must be called
                    if(!MirrorCheckSameColor(grid, 0, block.Position, color, out mirrorX))
                        symmetrySameColor = false;

                    if(!MirrorCheckSameColor(grid, 1, block.Position, color, out mirrorY))
                        symmetrySameColor = false;

                    if(!MirrorCheckSameColor(grid, 2, block.Position, color, out mirrorZ))
                        symmetrySameColor = false;

                    if(mirrorX.HasValue && grid.YSymmetryPlane.HasValue) // XY
                    {
                        if(!MirrorCheckSameColor(grid, 1, mirrorX.Value, color, out mirrorX))
                            symmetrySameColor = false;
                    }

                    if(mirrorX.HasValue && grid.ZSymmetryPlane.HasValue) // XZ
                    {
                        if(!MirrorCheckSameColor(grid, 2, mirrorX.Value, color, out mirrorX))
                            symmetrySameColor = false;
                    }

                    if(mirrorY.HasValue && grid.ZSymmetryPlane.HasValue) // YZ
                    {
                        if(!MirrorCheckSameColor(grid, 2, mirrorY.Value, color, out mirrorYZ))
                            symmetrySameColor = false;
                    }

                    if(grid.XSymmetryPlane.HasValue && mirrorYZ.HasValue) // XYZ
                    {
                        if(!MirrorCheckSameColor(grid, 0, mirrorYZ.Value, color, out mirrorX))
                            symmetrySameColor = false;
                    }
                }

                if(!symmetry || symmetrySameColor)
                {
                    SetCrosshairColor(CROSSHAIR_BAD_TARGET);
                    selectedInvalid = true;

                    if(symmetry)
                        SetToolStatus(0, "All symmetry blocks are this color.", MyFontEnum.Red);
                    else
                        SetToolStatus(0, "Already painted this color.", MyFontEnum.Red);

                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, symmetryStatus, MyFontEnum.DarkBlue);
                    SetToolStatus(3, null);

                    return false;
                }
            }

            SetCrosshairColor(CROSSHAIR_TARGET);

            if(!trigger)
            {
                if(symmetry && !symmetrySameColor)
                    SetToolStatus(0, "Selection is already painted but symmetry blocks are not - click to paint.", MyFontEnum.Green);
                else if(MyAPIGateway.Session.CreativeMode)
                    SetToolStatus(0, "Click to paint.", MyFontEnum.Green);
                else
                    SetToolStatus(0, "Hold click to paint.", MyFontEnum.Green);

                SetToolStatus(1, blockName, MyFontEnum.White);
                SetToolStatus(2, symmetryStatus, MyFontEnum.DarkBlue);
                SetToolStatus(3, null);
            }

            return true;
        }

        public void PaintProcess(ref Vector3 blockColor, Vector3 color, float paintSpeed, string blockName)
        {
            if(replaceAllMode)
            {
                // notification for this is done in the ReceivedPacket method to avoid re-iterating blocks

                blockColor = color;
                return;
            }

            if(MyAPIGateway.Session.CreativeMode)
            {
                blockColor = color;
                SetToolStatus(0, "Painted!", MyFontEnum.Blue);
                SetToolStatus(1, blockName, MyFontEnum.White);
                SetToolStatus(2, symmetryStatus, MyFontEnum.DarkBlue);
                SetToolStatus(3, null);
                return;
            }

            if(blockColor.X.EqualsIntMul(color.X, 360))
            {
                paintSpeed *= PAINT_SPEED;
                paintSpeed *= MyAPIGateway.Session.WelderSpeedMultiplier;

                for(int i = 0; i < 3; i++)
                {
                    if(blockColor.GetDim(i) > color.GetDim(i))
                        blockColor.SetDim(i, Math.Max(blockColor.GetDim(i) - paintSpeed, color.GetDim(i)));
                    else
                        blockColor.SetDim(i, Math.Min(blockColor.GetDim(i) + paintSpeed, color.GetDim(i)));
                }

                if(blockColor.EqualsToHSV(color))
                {
                    blockColor = color;

                    SetToolStatus(0, "Painting done!", MyFontEnum.Blue);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, null);
                    SetToolStatus(3, null);

                    if(settings.extraSounds)
                        PlaySound("HudColorBlock", 0.8f);
                }
                else
                {
                    SetToolStatus(0, "Painting " + ColorPercent(blockColor, color) + "%...", MyFontEnum.Blue);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, null);
                    SetToolStatus(3, null);
                }
            }
            else
            {
                paintSpeed *= DEPAINT_SPEED;
                paintSpeed *= MyAPIGateway.Session.GrinderSpeedMultiplier;

                blockColor.Y = Math.Max(blockColor.Y - paintSpeed, DEFAULT_COLOR.Y);
                blockColor.Z = (blockColor.Z > 0 ? Math.Max(blockColor.Z - paintSpeed, DEFAULT_COLOR.Z) : Math.Min(blockColor.Z + paintSpeed, DEFAULT_COLOR.Z));

                if(blockColor.Y.EqualsIntMul(DEFAULT_COLOR.Y) && blockColor.Z.EqualsIntMul(DEFAULT_COLOR.Z))
                {
                    blockColor.X = color.X;
                }

                if(blockColor.EqualsToHSV(DEFAULT_COLOR))
                {
                    blockColor = DEFAULT_COLOR;
                    blockColor.X = color.X;

                    if(color == DEFAULT_COLOR)
                        SetToolStatus(0, "Removing paint done!", MyFontEnum.Blue);
                    else
                        SetToolStatus(0, "Removing paint 100%...", MyFontEnum.Blue);

                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, null);
                    SetToolStatus(3, null);
                }
                else
                {
                    SetToolStatus(0, "Removing paint " + ColorPercent(blockColor, DEFAULT_COLOR) + "%...", MyFontEnum.Blue);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, null);
                    SetToolStatus(3, null);
                }
            }
        }

        private byte ColorPercent(Vector3 blockColor, Vector3 targetColor)
        {
            float percentScale = (Math.Abs(targetColor.X - blockColor.X) + Math.Abs(targetColor.Y - blockColor.Y) + Math.Abs(targetColor.Z - blockColor.Z)) / 3f;
            return (byte)MathHelper.Clamp((1 - percentScale) * 99, 0, 99);
        }

        private float GetBlockSurface(IMySlimBlock block)
        {
            Vector3 blockSize;
            block.ComputeScaledHalfExtents(out blockSize);
            blockSize = (blockSize * 2);
            return (blockSize.X * blockSize.Y) + (blockSize.Y * blockSize.Z) + (blockSize.Z * blockSize.X) / 6;
        }

        private IMySlimBlock GetTargetBlock(IMyCubeGrid g, IMyEntity player)
        {
            var view = MyAPIGateway.Session.ControlledObject.GetHeadMatrix(false, true);
            var rayFrom = view.Translation + view.Forward * 1.6;
            var rayTo = view.Translation + view.Forward * 5;
            var blockPos = g.RayCastBlocks(rayFrom, rayTo);
            return (blockPos.HasValue ? g.GetCubeBlock(blockPos.Value) : null);
        }

        public bool HoldingTool(bool trigger)
        {
            try
            {
                var character = MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity;

                if(pickColorMode && MyAPIGateway.Players.Count > 1)
                {
                    var view = MyAPIGateway.Session.ControlledObject.GetHeadMatrix(false, true);
                    var ray = new RayD(view.Translation, view.Forward);
                    IMyCharacter targetCharacter = null;
                    ulong targetSteamId = 0;
                    double targetDist = 3;
                    Vector3 targetColor = DEFAULT_COLOR;
                    string targetName = null;
                    long myEntId = character.EntityId;

                    MyAPIGateway.Entities.GetEntities(ents, delegate (IMyEntity e)
                                                      {
                                                          var c = e as IMyCharacter;

                                                          if(c != null && c.IsPlayer && c.EntityId != myEntId && c.Visible && c.Physics != null)
                                                          {
                                                              var check = ray.Intersects(c.WorldAABB);

                                                              if(check.HasValue && check.Value <= targetDist)
                                                              {
                                                                  MyObjectBuilder_Character obj = null;

                                                                  if(!entObjCache.ContainsKey(c.EntityId))
                                                                  {
                                                                      obj = (MyObjectBuilder_Character)c.GetObjectBuilder(false); // FIXME use a better method once one surfaces
                                                                      entObjCache.Add(c.EntityId, obj); // cache the object to avoid regenerating it
                                                                  }
                                                                  else
                                                                  {
                                                                      obj = entObjCache[c.EntityId];
                                                                  }

                                                                  if(playerColorData.ContainsKey(obj.PlayerSteamId))
                                                                  {
                                                                      var cd = playerColorData[obj.PlayerSteamId];
                                                                      targetCharacter = c;
                                                                      targetSteamId = obj.PlayerSteamId;
                                                                      targetDist = check.Value;
                                                                      targetColor = cd.colors[cd.selectedSlot];
                                                                      targetName = obj.Name;
                                                                  }
                                                              }
                                                          }

                                                          return false;
                                                      });

                    if(targetCharacter != null)
                    {
                        selectedCharacter = targetCharacter;

                        if(trigger)
                        {
                            SendToServer_ColorPickMode(false);

                            if(SendToServer_SetColor((byte)localColorData.selectedSlot, targetColor, true))
                            {
                                PlaySound("HudMouseClick", 0.25f);
                                MyAPIGateway.Utilities.ShowNotification("Color in slot " + localColorData.selectedSlot + " set to " + ColorToStringShort(targetColor), 2000, MyFontEnum.White);
                            }
                            else
                            {
                                PlaySound("HudUnable", 0.25f);
                            }

                            SetToolStatus(0, null);
                            SetToolStatus(1, null);
                            SetToolStatus(2, null);
                            SetToolStatus(3, null);
                        }
                        else
                        {
                            SetCrosshairColor(HSVtoRGB(targetColor));

                            if(!targetColor.EqualsToHSV(prevColorPreview))
                            {
                                prevColorPreview = targetColor;

                                SetToolColor(targetColor);

                                if(settings.extraSounds)
                                    PlaySound("HudItem", 0.75f);
                            }

                            SetToolStatus(0, "Click to pick this player's selected color.", MyFontEnum.Green);
                            SetToolStatus(1, targetName, MyFontEnum.White);
                            SetToolStatus(2, ColorToStringShort(targetColor), MyFontEnum.White);
                            SetToolStatus(3, null);
                        }

                        return false;
                    }
                }

                // finds the closest grid you're aiming at by doing a physical ray cast
                var grid = MyAPIGateway.CubeBuilder.FindClosestGrid() as MyCubeGrid;
                selectedGrid = grid;

                SetCrosshairColor(CROSSHAIR_NO_TARGET);

                if(grid == null)
                {
                    if(pickColorMode)
                    {
                        SetToolStatus(0, "Aim at a block or player and click to pick its color.", MyFontEnum.Blue);
                        SetToolStatus(1, null);
                        SetToolStatus(2, null);
                        SetToolStatus(3, null);
                    }
                    else if(trigger)
                    {
                        SetToolStatus(0, "Aim at a block to paint it.", MyFontEnum.Red);
                        SetToolStatus(1, null);
                        SetToolStatus(2, null);
                        SetToolStatus(3, null);
                    }

                    return false;
                }
                else
                {
                    var block = GetTargetBlock(grid, character);
                    var color = GetBuildColor();
                    Vector3 blockColor;
                    string blockName;
                    symmetryStatus = null;

                    if(!replaceAllMode && MyAPIGateway.Session.CreativeMode)
                    {
                        if(grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue)
                        {
                            var controlSymmetry = MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY);
                            assigned.Clear();

                            if(controlSymmetry.GetKeyboardControl() != MyKeys.None)
                                assigned.Append(MyAPIGateway.Input.GetKeyName(controlSymmetry.GetKeyboardControl()));

                            if(controlSymmetry.GetSecondKeyboardControl() != MyKeys.None)
                            {
                                if(assigned.Length > 0)
                                    assigned.Append(" or ");

                                assigned.Append(MyAPIGateway.Input.GetKeyName(controlSymmetry.GetSecondKeyboardControl()));
                            }

                            if(InputHandler.IsInputReadable())
                            {
                                symmetryInput = true;

                                if(MyAPIGateway.CubeBuilder.UseSymmetry)
                                    symmetryStatus = "Symmetry enabled. Press " + assigned + " to turn off.";
                                else
                                    symmetryStatus = "Symmetry is off. Press " + assigned + " to enable.";
                            }
                            else
                            {
                                if(MyAPIGateway.CubeBuilder.UseSymmetry)
                                    symmetryStatus = "Symmetry enabled.";
                                else
                                    symmetryStatus = "Symmetry is off.";
                            }
                        }
                        else
                        {
                            symmetryStatus = "No symmetry on this ship.";
                        }
                    }

                    if(!IsBlockValid(block, color, trigger, out blockName, out blockColor))
                        return false;

                    if(block != null)
                    {
                        if(trigger)
                        {
                            float paintSpeed = (1.0f / GetBlockSurface(block));
                            PaintProcess(ref blockColor, color, paintSpeed, blockName);
                            SetCrosshairColor(CROSSHAIR_PAINTING);

                            if(replaceAllMode && MyAPIGateway.Session.CreativeMode)
                            {
                                SendToServer_ReplaceColor(grid.EntityId, grid, block.GetColorMask(), blockColor, replaceGridSystem);
                            }
                            else
                            {
                                if(MyAPIGateway.Session.CreativeMode && MyAPIGateway.CubeBuilder.UseSymmetry && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue))
                                {
                                    var mirrorPlane = new Vector3I(
                                        (grid.XSymmetryPlane.HasValue ? grid.XSymmetryPlane.Value.X : int.MinValue),
                                        (grid.YSymmetryPlane.HasValue ? grid.YSymmetryPlane.Value.Y : int.MinValue),
                                        (grid.ZSymmetryPlane.HasValue ? grid.ZSymmetryPlane.Value.Z : int.MinValue));

                                    OddAxis odd = OddAxis.NONE;

                                    if(grid.XSymmetryOdd)
                                        odd |= OddAxis.X;

                                    if(grid.YSymmetryOdd)
                                        odd |= OddAxis.Y;

                                    if(grid.ZSymmetryOdd)
                                        odd |= OddAxis.Z;

                                    SendToServer_PaintBlock(grid.EntityId, grid, block.Position, blockColor, true, mirrorPlane, odd);
                                }
                                else
                                {
                                    SendToServer_PaintBlock(grid.EntityId, grid, block.Position, blockColor, false);
                                }
                            }

                            return true;
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
        }

        public void MessageEntered(string msg, ref bool send)
        {
            try
            {
                if(msg.StartsWith("/pg", StringComparison.OrdinalIgnoreCase))
                {
                    send = false;
                    msg = msg.Substring("/pg".Length).Trim().ToLower();

                    if(msg.StartsWith("reload", StringComparison.Ordinal))
                    {
                        if(settings.Load())
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Reloaded and re-saved config.");
                        else
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Config created with the current settings.");

                        settings.Save();
                        return;
                    }

                    if(msg.StartsWith("pick", StringComparison.Ordinal))
                    {
                        if(localHeldTool == null)
                        {
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "You need to hold the tool for this to work.");
                        }
                        else
                        {
                            prevColorPreview = GetBuildColor();
                            SendToServer_ColorPickMode(true);
                        }

                        return;
                    }

                    if(msg.StartsWith("rgb", StringComparison.Ordinal) || msg.StartsWith("hsv", StringComparison.Ordinal))
                    {
                        bool hsv = msg.StartsWith("hsv", StringComparison.Ordinal);
                        msg = msg.Substring("rgb".Length).Trim();
                        var values = new int[3];

                        if(!hsv && msg.StartsWith("#", StringComparison.Ordinal))
                        {
                            msg = msg.Substring(1).Trim();

                            if(msg.Length < 6)
                            {
                                MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Invalid HEX color, needs 6 characters after #.");
                                return;
                            }

                            int c = 0;

                            for(int i = 1; i < 6; i += 2)
                            {
                                values[c++] = Convert.ToInt32("" + msg[i - 1] + msg[i], 16);
                            }
                        }
                        else
                        {
                            string[] split = msg.Split(' ');

                            if(split.Length != 3)
                            {
                                MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Need to specify 3 numbers from 0 to 255 to create a RGB color.");
                                return;
                            }

                            for(int i = 0; i < 3; i++)
                            {
                                if(!int.TryParse(split[i], out values[i]))
                                {
                                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Color argument " + (i + 1) + " is not a valid number!");
                                    return;
                                }
                            }
                        }

                        Vector3 color;

                        if(hsv)
                            color = new Vector3(MathHelper.Clamp(values[0], 0, 360) / 360.0f, MathHelper.Clamp(values[1], -100, 100) / 100.0f, MathHelper.Clamp(values[2], -100, 100) / 100.0f);
                        else
                            color = new Color(MathHelper.Clamp(values[0], 0, 255), MathHelper.Clamp(values[1], 0, 255), MathHelper.Clamp(values[2], 0, 255)).ColorToHSVDX11();

                        if(SendToServer_SetColor((byte)localColorData.selectedSlot, color, true))
                        {
                            PlaySound("HudMouseClick", 0.25f);
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Color in slot " + localColorData.selectedSlot + " set to " + ColorToString(color));
                        }
                        else
                        {
                            PlaySound("HudUnable", 0.25f);
                        }

                        return;
                    }

                    MyAPIGateway.Utilities.ShowMissionScreen("Paint Gun commands", null, null, "/pg pick\n" +
                                                             "  get color from a block (alias: Shift+[LandingGears])\n" +
                                                             "\n" +
                                                             "/pg rgb <0~255> <0~255> <0~255>\n" +
                                                             "  set the color using RGB format\n" +
                                                             "\n" +
                                                             "/pg rgb #<00~FF><00~FF><00~FF>\n" +
                                                             "  set the color using hex RGB format\n" +
                                                             "\n" +
                                                             "/pg hsv <0-360> <-100~100> <-100~100>\n" +
                                                             "  set the color using HSV format\n" +
                                                             "\n" +
                                                             "/pg reload\n" +
                                                             "  reloads the config file.\n", null, "Close");
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public static Color HSVtoRGB(Vector3 hsv)
        {
            // from the game code... weird values.
            return new Vector3(hsv.X, MathHelper.Clamp(hsv.Y + 0.8f, 0f, 1f), MathHelper.Clamp(hsv.Z + 0.55f, 0f, 1f)).HSVtoColor();
        }
    }

    public static class Extensions
    {
        public static Vector3I ToHSVI(this Vector3 vec)
        {
            return new Vector3I(vec.GetDim(0) * 360, vec.GetDim(1) * 100, vec.GetDim(2) * 100);
        }

        public static void ColorToBytes(this Vector3 color, byte[] bytes, ref int index)
        {
            var h = (short)(color.X * 360);
            var s = (sbyte)(color.Y * 100);
            var v = (sbyte)(color.Z * 100);

            var data = BitConverter.GetBytes(h);
            Array.Copy(data, 0, bytes, index, data.Length);
            index += data.Length;

            bytes[index] = (byte)s;
            index += sizeof(sbyte);

            bytes[index] = (byte)v;
            index += sizeof(sbyte);
        }

        public static Vector3 BytesToColor(this byte[] bytes, ref int index)
        {
            var h = BitConverter.ToInt16(bytes, index);
            index += sizeof(short);

            var s = (sbyte)bytes[index];
            index += sizeof(sbyte);

            var v = (sbyte)bytes[index];
            index += sizeof(sbyte);

            return new Vector3(h / 360f, s / 100f, v / 100f);
        }

        public static bool EqualsToHSV(this Vector3 hsv1, Vector3 hsv2)
        {
            return ((int)(hsv1.X * 360) == (int)(hsv2.X * 360) && (int)(hsv1.Y * 100) == (int)(hsv2.Y * 100) && (int)(hsv1.Z * 100) == (int)(hsv2.Z * 100));
        }

        public static bool EqualsIntMul(this float f1, float f2, int mul = 100)
        {
            return (int)(f1 * mul) == (int)(f2 * mul);
        }

        public static void GetLogicalGridSystem(this MyCubeGrid grid, List<MyCubeGrid> grids)
        {
            grids.Add(grid);
            var addedGrids = new HashSet<long>();
            addedGrids.Add(grid.EntityId);

            foreach(var block in grid.GetFatBlocks())
            {
                var g = GetGridFromBlock(block) as MyCubeGrid;

                if(g != null && !addedGrids.Contains(g.EntityId))
                {
                    addedGrids.Add(g.EntityId);
                    grids.Add(g);
                }
            }
        }

        private static IMyCubeGrid GetGridFromBlock(MyCubeBlock block)
        {
            var motorStator = block as IMyMotorBase;

            if(motorStator != null)
                return motorStator.RotorGrid;

            var motorRotor = block as IMyMotorRotor;

            if(motorRotor != null)
                return (motorRotor.Stator == null ? null : motorRotor.Stator.CubeGrid);

            var pistonBase = block as IMyPistonBase;

            if(pistonBase != null)
                return pistonBase.TopGrid;

            var pistonTop = block as IMyPistonTop;

            if(pistonTop != null)
                return (pistonTop.Piston == null ? null : pistonTop.Piston.CubeGrid);

            var connector = block as IMyShipConnector;

            if(connector != null)
                return (connector.OtherConnector == null ? null : connector.OtherConnector.CubeGrid);

            return null;
        }
    }
}
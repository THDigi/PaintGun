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

// HACK allows the use of these whitelisted enums without triggering prohibited issues with accessing their parent classes
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace Digi.PaintGun
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class PaintGunMod : MySessionComponentBase
    {
        public override void LoadData()
        {
            Log.SetUp("Paint Gun", 500818376, "PaintGun");
        }

        public static PaintGunMod instance = null;

        public bool init = false;
        public bool isThisHostDedicated = false;
        public Settings settings = null;
        public bool gameHUD = true;
        public float gameHUDBkOpacity = 1f;

        public bool pickColorMode = false;
        public bool replaceAllMode = false;
        public bool replaceAllModeInputPressed = false;
        public bool replaceGridSystem = false;
        public long replaceGridSystemTimeout = 0;
        public bool symmetryInput = false;
        public string symmetryStatus = null;
        public MyCubeGrid selectedGrid = null;
        public IMySlimBlock selectedSlimBlock = null;
        public IMyCharacter selectedCharacter = null;
        public bool selectedInvalid = false;
        public Vector3 prevColorPreview;
        public Vector3 prevCustomColor;
        public MyEntity3DSoundEmitter emitter;
        public IMyHudNotification[] toolStatus = new IMyHudNotification[4];

        private int prevSelectedColorSlot = 0;
        private byte skipColorUpdate = 0;

        public PaintGunItem localHeldTool = null;
        public PlayerColorData localColorData = null;
        public readonly Dictionary<ulong, PlayerColorData> playerColorData = new Dictionary<ulong, PlayerColorData>();
        public readonly HashSet<ulong> playersColorPickMode = new HashSet<ulong>();
        public readonly List<IMyPlayer> players = new List<IMyPlayer>(0);
        private readonly Dictionary<long, MyObjectBuilder_Character> entObjCache = new Dictionary<long, MyObjectBuilder_Character>();

        public const string MOD_NAME = "PaintGun";
        public const string PAINT_GUN_ID = "PaintGun";
        public const string PAINT_MAG_ID = "PaintGunMag";
        public const float PAINT_SPEED = 4f;
        public const float DEPAINT_SPEED = 6f;
        public const int SKIP_UPDATES = 10;
        public const float SAME_COLOR_RANGE = 0.001f;
        public const ushort PACKET = 9319; // network packet ID used for this mod; must be unique from other mods

        public readonly Vector3 DEFAULT_COLOR = new Vector3(0, -1, 0);
        public readonly Color BACKGROUND_COLOR = new Vector4(0.20784314f, 0.266666681f, 0.298039228f, 1f);

        public readonly MySoundPair SOUND_HUD_UNABLE = new MySoundPair("HudUnable");
        public readonly MySoundPair SOUND_HUD_CLICK = new MySoundPair("HudClick");
        public readonly MySoundPair SOUND_HUD_MOUSE_CLICK = new MySoundPair("HudMouseClick");
        public readonly MySoundPair SOUND_HUD_COLOR = new MySoundPair("HudColorBlock");
        public readonly MySoundPair SOUND_HUD_ITEM = new MySoundPair("HudItem");

        public readonly MyStringId MATERIAL_GIZMIDRAWLINE = MyStringId.GetOrCompute("GizmoDrawLine");
        public readonly MyStringId MATERIAL_GIZMIDRAWLINERED = MyStringId.GetOrCompute("GizmoDrawLineRed");
        public readonly MyStringId MATERIAL_SQUARE = MyStringId.GetOrCompute("Square");
        public readonly MyStringId MATERIAL_PALETTE_COLOR = MyStringId.GetOrCompute("PaintGunPalette_Color");
        public readonly MyStringId MATERIAL_PALETTE_SELECTED = MyStringId.GetOrCompute("PaintGunPalette_Selected");
        public readonly MyStringId MATERIAL_PALETTE_BACKGROUND = MyStringId.GetOrCompute("PaintGunPalette_Background");

        public readonly MySoundPair SPRAY_SOUND = new MySoundPair("PaintGunSpray");
        public readonly MyStringId MATERIAL_SPRAY = MyStringId.GetOrCompute("Smoke");

        public readonly MyObjectBuilder_AmmoMagazine PAINT_MAG = new MyObjectBuilder_AmmoMagazine()
        {
            SubtypeName = PAINT_MAG_ID,
            ProjectilesCount = 1
        };

        public const int TOOLSTATUS_TIMEOUT = 200;

        public const int COLOR_PALETTE_SIZE = 14;

        public readonly Color CROSSHAIR_NO_TARGET = new Color(255, 0, 0);
        public readonly Color CROSSHAIR_BAD_TARGET = new Color(255, 200, 0);
        public readonly Color CROSSHAIR_TARGET = new Color(0, 255, 0);
        public readonly Color CROSSHAIR_PAINTING = new Color(0, 255, 155);
        public readonly MyStringId CROSSHAIR_SPRITEID = MyStringId.GetOrCompute("Default");

        private readonly List<IHitInfo> hits = new List<IHitInfo>();
        private readonly Dictionary<long, DetectionInfo> entitiesInRange = new Dictionary<long, DetectionInfo>();
        private readonly List<MyLineSegmentOverlapResult<MyEntity>> raycastResults = new List<MyLineSegmentOverlapResult<MyEntity>>();
        private readonly HashSet<IMyEntity> ents = new HashSet<IMyEntity>();
        private readonly StringBuilder assigned = new StringBuilder();
        private readonly HashSet<MyCubeGrid> gridsInSystemCache = new HashSet<MyCubeGrid>();
        private readonly List<IMyPlayer> playersCache = new List<IMyPlayer>(0); // always empty

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

        public void Init()
        {
            init = true;
            instance = this;
            isThisHostDedicated = (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer);

            prevCustomColor = DEFAULT_COLOR;

            Log.Init();

            MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET, ReceivedPacket);

            if(!isThisHostDedicated) // stuff that shouldn't happen DS-side.
            {
                settings = new Settings();

                UpdateConfigValues();

                MyAPIGateway.Utilities.MessageEntered += MessageEntered;
                MyAPIGateway.Gui.GuiControlRemoved += GuiControlRemoved;

                if(!MyAPIGateway.Multiplayer.IsServer)
                    SendToServer_RequestColorList(MyAPIGateway.Multiplayer.MyId);
            }
        }

        protected override void UnloadData()
        {
            instance = null;
            try
            {
                if(init)
                {
                    init = false;

                    MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET, ReceivedPacket);
                    MyAPIGateway.Gui.GuiControlRemoved -= GuiControlRemoved;

                    if(settings != null)
                    {
                        settings.Close();
                        settings = null;
                    }
                }

                emitter?.Cleanup();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Log.Close();
        }

        private void GuiControlRemoved(object obj)
        {
            try
            {
                if(obj.ToString().EndsWith("ScreenOptionsSpace")) // closing options menu just assumes you changed something so it'll re-check config settings
                {
                    UpdateConfigValues();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        /// <summary>
        /// The config calls are slow so we're caching the ones we use when world loads or player exits the optins menu.
        /// </summary>
        private void UpdateConfigValues()
        {
            var cfg = MyAPIGateway.Session.Config;

            gameHUD = !cfg.MinimalHud;
            gameHUDBkOpacity = cfg.HUDBkOpacity;
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
                grid.GetShipSubgrids(gridsInSystemCache);
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
            grid.ChangeColor(grid.GetCubeBlock(pos), color); // HACK getting a MySlimBlock and sending it straight to arguments avoids getting prohibited errors.
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

        #region Network sync
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
                                    localHeldTool.cooldown = DateTime.UtcNow.Ticks;
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

                                var identity = MyAPIGateway.Players.TryGetIdentityId(steamId);

                                if(!grid.ColorGridOrBlockRequestValidation(identity))
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

                                var identity = MyAPIGateway.Players.TryGetIdentityId(steamId);

                                if(!grid.ColorGridOrBlockRequestValidation(identity))
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
                MyAPIGateway.Session.Player.ChangeOrSwitchToColor(color);
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
        #endregion

        #region Symmetry mirror-paint
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
                            MyCubeBuilder.DrawSemiTransparentBox(g, g.GetCubeBlock(mirrorX), Color.White, true, selectedInvalid ? MATERIAL_GIZMIDRAWLINERED : MATERIAL_GIZMIDRAWLINE, null);

                        return mirrorX;
                    }
                    break;

                case 1:
                    if(g.YSymmetryPlane.HasValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((g.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (g.YSymmetryOdd ? 1 : 0), 0);

                        if(g.CubeExists(mirrorY))
                            MyCubeBuilder.DrawSemiTransparentBox(g, g.GetCubeBlock(mirrorY), Color.White, true, selectedInvalid ? MATERIAL_GIZMIDRAWLINERED : MATERIAL_GIZMIDRAWLINE, null);

                        return mirrorY;
                    }
                    break;

                case 2:
                    if(g.ZSymmetryPlane.HasValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((g.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (g.ZSymmetryOdd ? 1 : 0)); // reversed on odd

                        if(g.CubeExists(mirrorZ))
                            MyCubeBuilder.DrawSemiTransparentBox(g, g.GetCubeBlock(mirrorZ), Color.White, true, selectedInvalid ? MATERIAL_GIZMIDRAWLINERED : MATERIAL_GIZMIDRAWLINE, null);

                        return mirrorZ;
                    }
                    break;
            }

            return null;
        }
        #endregion

        public override void HandleInput()
        {
            try
            {
                if(!init || localHeldTool == null)
                    return;

                if(MyAPIGateway.Gui.IsCursorVisible && MyAPIGateway.Gui.ActiveGamePlayScreen == "ColorPick")
                {
                    localColorData.selectedSlot = MyAPIGateway.Session.Player.SelectedBuildColorSlot;
                    SetToolColor(localColorData.colors[localColorData.selectedSlot]);
                }
                else if(InputHandler.IsInputReadable())
                {
                    if(symmetryInput && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
                    {
                        MyAPIGateway.CubeBuilder.UseSymmetry = !MyAPIGateway.CubeBuilder.UseSymmetry;
                    }

                    if(replaceAllMode && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
                    {
                        replaceGridSystem = !replaceGridSystem;
                    }

                    if(!MyAPIGateway.Input.IsAnyAltKeyPressed())
                    {
                        var change = 0;

                        if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_LEFT))
                            change = 1;
                        else if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_RIGHT))
                            change = -1;
                        else
                            change = MyAPIGateway.Input.DeltaMouseScrollWheelValue();

                        if(change != 0 && localColorData != null)
                        {
                            if(settings.extraSounds)
                                PlaySound(SOUND_HUD_CLICK, 0.1f);

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

                            MyAPIGateway.Session.Player.SelectedBuildColorSlot = localColorData.selectedSlot;
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

                // HUD toggle monitor; required here because it gets the previous value if used in HandleInput()
                if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.TOGGLE_HUD))
                    gameHUD = !MyAPIGateway.Session.Config.MinimalHud;

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

                                MyTransparentGeometry.AddQuad(MATERIAL_SQUARE, ref quad, Color.Red * alpha, ref center);
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

                                MyTransparentGeometry.AddQuad(MATERIAL_SQUARE, ref quad, Color.Green * alpha, ref center);
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

                                MyTransparentGeometry.AddQuad(MATERIAL_SQUARE, ref quad, Color.Blue * alpha, ref center);
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
                            MyCubeBuilder.DrawSemiTransparentBox(internalGrid, internalGrid.GetCubeBlock(selectedSlimBlock.Position), Color.White, true, selectedInvalid ? MATERIAL_GIZMIDRAWLINERED : MATERIAL_GIZMIDRAWLINE, null);

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

                            MySimpleObjectDraw.DrawAttachedTransparentBox(ref matrix, ref box, ref color, selectedCharacter.Render.GetRenderObjectID(), ref worldToLocal, MySimpleObjectRasterizer.Wireframe, 1, 0.05f, null, MATERIAL_GIZMIDRAWLINE, false);
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void Draw()
        {
            try
            {
                if(!init)
                    return;

                if(localHeldTool != null && localColorData != null)
                {
                    if(MyAPIGateway.Gui.IsCursorVisible)
                        return;

                    if(settings.hidePaletteWithHUD && !gameHUD)
                        return;

                    var cam = MyAPIGateway.Session.Camera;
                    var camMatrix = cam.WorldMatrix;
                    var viewProjectionMatrixInv = MatrixD.Invert(cam.ViewMatrix * cam.ProjectionMatrix);
                    var localPos = new Vector3D(settings.paletteScreenPos.X, settings.paletteScreenPos.Y, 0);
                    var hudPos = Vector3D.Transform(localPos, viewProjectionMatrixInv);
                    var scaleFOV = (float)Math.Tan(MyAPIGateway.Session.Camera.FovWithZoom / 2);
                    scaleFOV *= settings.paletteScale;

                    float squareWidth = 0.0014f * scaleFOV;
                    float squareHeight = 0.0011f * scaleFOV;
                    float selectedWidth = (squareWidth + (squareWidth / 7f));
                    float selectedHeight = (squareHeight + (squareHeight / 7f));
                    double spacingAdd = 0.0006 * scaleFOV;
                    double spacingWidth = (squareWidth * 2) + spacingAdd;
                    double spacingHeight = (squareHeight * 2) + spacingAdd;
                    const int MIDDLE_INDEX = 7;
                    const float BG_WIDTH_MUL = 3.85f;
                    const float BG_HEIGHT_MUL = 1.3f;

                    var pos = hudPos + camMatrix.Left * (spacingWidth * (MIDDLE_INDEX / 2)) + camMatrix.Up * (spacingHeight / 2);

                    for(int i = 0; i < localColorData.colors.Count; i++)
                    {
                        var v = localColorData.colors[i];
                        var c = HSVtoRGB(v);

                        if(i == MIDDLE_INDEX)
                            pos += camMatrix.Left * (spacingWidth * MIDDLE_INDEX) + camMatrix.Down * spacingHeight;

                        if(i == localColorData.selectedSlot)
                            MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_SELECTED, Color.White, pos, camMatrix.Left, camMatrix.Up, selectedWidth, selectedHeight, Vector2.Zero, BlendTypeEnum.SDR);

                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, c, pos, camMatrix.Left, camMatrix.Up, squareWidth, squareHeight, Vector2.Zero, BlendTypeEnum.SDR);

                        pos += camMatrix.Right * spacingWidth;
                    }

                    var color = BACKGROUND_COLOR * (settings.paletteBackgroundOpacity < 0 ? gameHUDBkOpacity : settings.paletteBackgroundOpacity);
                    MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, color, hudPos, camMatrix.Left, camMatrix.Up, (float)(spacingWidth * BG_WIDTH_MUL), (float)(spacingHeight * BG_HEIGHT_MUL), Vector2.Zero, BlendTypeEnum.SDR);
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

        public void SetToolStatus(int line, string text, string font = MyFontEnum.White, int aliveTime = TOOLSTATUS_TIMEOUT)
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

        public static string ColorMaskToString(Vector3 colorMask)
        {
            var hsv = ColorMaskToHSV(colorMask);

            return $"Hue: {hsv.X}°, saturation: {hsv.Y}%, value: {hsv.Z}%";
        }

        public static string ColorMaskToStringShort(Vector3 colorMask)
        {
            var hsv = ColorMaskToHSV(colorMask);

            return $"HSV: {hsv.X}°, {hsv.Y}%, {hsv.Z}%";
        }

        // Thanks to Whiplash141 for this conversion method
        public static Vector3I ColorMaskToHSV(Vector3 colorMask)
        {
            float saturationProportion = colorMask.Y + 0.8f;
            float valueProportion = colorMask.Z + 0.55f - 0.1f;

            float hue = colorMask.X * 360f;
            float saturation = saturationProportion * 100f;
            float value = valueProportion * 100f;

            return new Vector3I(MathHelper.Clamp(hue, 0f, 360), MathHelper.Clamp(saturation, 0f, 100), MathHelper.Clamp(value, 0f, 100));
        }

        public static Color HSVtoRGB(Vector3 hsv)
        {
            // HACK copied and inverted with data from MyGuiScreenColorPicker.OnValueChange()
            return new Vector3(
                    hsv.X,
                    MathHelper.Clamp(hsv.Y + MyColorPickerConstants.SATURATION_DELTA, 0f, 1f),
                    MathHelper.Clamp(hsv.Z + MyColorPickerConstants.VALUE_DELTA - MyColorPickerConstants.VALUE_COLORIZE_DELTA, 0f, 1f)
                ).HSVtoColor();
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

        public void PlaySound(MySoundPair soundPair, float volume)
        {
            if(emitter == null)
                emitter = new MyEntity3DSoundEmitter(null);

            emitter.SetPosition(MyAPIGateway.Session.Camera.WorldMatrix.Translation);
            emitter.CustomVolume = volume;
            emitter.PlaySingleSound(soundPair);
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
                }
                else if(replaceAllMode)
                {
                    SetToolStatus(0, "Aim at a block to replace its color from the entire grid.", MyFontEnum.Blue);
                    SetToolStatus(1, null);
                    SetToolStatus(2, null);
                }
                else if(trigger)
                {
                    SetToolStatus(0, "Aim at a block to paint it.", MyFontEnum.Red);
                    SetToolStatus(1, null);
                    SetToolStatus(2, symmetryStatus, MyFontEnum.DarkBlue);
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
                        PlaySound(SOUND_HUD_MOUSE_CLICK, 0.25f);
                        MyAPIGateway.Utilities.ShowNotification("Color in slot " + localColorData.selectedSlot + " set to " + ColorMaskToStringShort(blockColor), 2000, MyFontEnum.White);
                    }
                    else
                    {
                        PlaySound(SOUND_HUD_UNABLE, 0.25f);
                    }

                    SetToolStatus(0, null);
                    SetToolStatus(1, null);
                    SetToolStatus(2, null);
                }
                else
                {
                    SetCrosshairColor(HSVtoRGB(blockColor));

                    if(!blockColor.EqualsToHSV(prevColorPreview))
                    {
                        prevColorPreview = blockColor;
                        SetToolColor(blockColor);

                        if(settings.extraSounds)
                            PlaySound(SOUND_HUD_ITEM, 0.75f);
                    }

                    SetToolStatus(0, "Click to pick the color.", MyFontEnum.Green);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, ColorMaskToStringShort(blockColor), MyFontEnum.White);
                }

                return false;
            }

            if(!block.CubeGrid.ColorGridOrBlockRequestValidation(MyAPIGateway.Session.Player.IdentityId))
            {
                SetToolStatus(0, "You can only paint owned or allied ships.", MyFontEnum.Red);
                SetToolStatus(1, null);
                SetToolStatus(2, null);

                if(trigger)
                    PlaySound(SOUND_HUD_UNABLE, 0.25f);

                return false;
            }

            if(replaceAllMode)
            {
                selectedInvalid = blockColor.EqualsToHSV(color);

                if(selectedInvalid)
                    SetCrosshairColor(CROSSHAIR_BAD_TARGET);
                else
                    SetCrosshairColor(CROSSHAIR_TARGET);

                var control = MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY);

                if(selectedInvalid)
                    SetToolStatus(0, "Already painted this color.", MyFontEnum.Red);
                else
                    SetToolStatus(0, "Click to replace this color on all blocks.", MyFontEnum.Green);

                SetToolStatus(1, ColorMaskToStringShort(blockColor), MyFontEnum.White);
                SetToolStatus(2, (replaceGridSystem ? "Replace on all attached grids (except connectors)" : "Replaces only on the selected grid") + ", press " + InputHandler.GetFriendlyStringForControl(control) + " to toggle.", (replaceGridSystem ? MyFontEnum.Red : MyFontEnum.DarkBlue));

                return (selectedInvalid ? false : true);
            }

            if(!MyAPIGateway.Session.CreativeMode && block.CurrentDamage > (block.MaxIntegrity / 10.0f) || (block.FatBlock != null && !block.FatBlock.IsFunctional))
            {
                if(trigger)
                    PlaySound(SOUND_HUD_UNABLE, 0.5f);

                SetCrosshairColor(CROSSHAIR_BAD_TARGET);
                selectedInvalid = true;

                SetToolStatus(0, (block.FatBlock != null && !block.FatBlock.IsFunctional ? "Block not fully built!" : "Block is damaged!"), MyFontEnum.Red);
                SetToolStatus(1, blockName, MyFontEnum.White);
                SetToolStatus(2, null);

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

                    if(settings.extraSounds)
                        PlaySound(SOUND_HUD_COLOR, 0.8f);
                }
                else
                {
                    SetToolStatus(0, "Painting " + ColorPercent(blockColor, color) + "%...", MyFontEnum.Blue);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, null);
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
                }
                else
                {
                    SetToolStatus(0, "Removing paint " + ColorPercent(blockColor, DEFAULT_COLOR) + "%...", MyFontEnum.Blue);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, null);
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

        private void GetTarget(IMyCharacter character, out IMyCubeGrid targetGrid, out IMySlimBlock targetBlock, out IMyCharacter targetCharacter)
        {
            // HACK copied functionality from MyCasterComponent (what welders/grinders use)
            // TODO optimize?

            targetGrid = null;
            targetBlock = null;
            targetCharacter = null;

            hits.Clear();
            entitiesInRange.Clear();
            raycastResults.Clear();

            const float MAX_DISTANCE = 5;
            var view = character.GetHeadMatrix(false, true);
            var rayDir = view.Forward;
            var rayFrom = view.Translation;
            var rayTo = view.Translation + rayDir * MAX_DISTANCE;

            MyAPIGateway.Physics.CastRay(rayFrom, rayTo, hits, CollisionLayers.ObjectDetectionCollisionLayer);

            foreach(var hit in hits)
            {
                var ent = hit.HitEntity;

                if(ent == null)
                    continue;

                IMyEntity parent = ent.GetTopMostParent(null);
                MyCubeGrid grid = parent as MyCubeGrid;
                Vector3D hitPos = hit.Position;

                if(grid != null)
                {
                    if(grid.GridSizeEnum == MyCubeSize.Large)
                        hitPos += hit.Normal * -0.08f;
                    else
                        hitPos += hit.Normal * -0.02f;
                }

                DetectionInfo info;
                if(entitiesInRange.TryGetValue(parent.EntityId, out info))
                {
                    float num = Vector3.DistanceSquared(info.DetectionPoint, rayFrom);
                    float num2 = Vector3.DistanceSquared(hitPos, rayFrom);
                    if(num > num2)
                    {
                        entitiesInRange[parent.EntityId] = new DetectionInfo(parent, hitPos);
                    }
                }
                else
                {
                    entitiesInRange[parent.EntityId] = new DetectionInfo(parent, hitPos);
                }
            }

            // also look for blocks that have physics disabled... which currently are only lights
            LineD lineD = new LineD(rayFrom, rayTo);
            using(raycastResults.GetClearToken<MyLineSegmentOverlapResult<MyEntity>>())
            {
                MyGamePruningStructure.GetAllEntitiesInRay(ref lineD, raycastResults, MyEntityQueryType.Both);

                foreach(MyLineSegmentOverlapResult<MyEntity> res in raycastResults)
                {
                    if(res.Element == null)
                        continue;

                    var parent = res.Element.GetTopMostParent(null);
                    var block = res.Element as IMyCubeBlock;

                    if(block != null)
                    {
                        // HACK shortcut for !block.SlimBlock.HasPhysics
                        var lightBlockDef = block.SlimBlock.BlockDefinition as MyLightingBlockDefinition;

                        if(lightBlockDef == null)
                            continue;

                        MatrixD wmInv = block.PositionComp.WorldMatrixNormalizedInv;
                        Vector3D localFrom = Vector3D.Transform(rayFrom, ref wmInv);
                        Vector3D localTo = Vector3D.Transform(rayTo, ref wmInv);

                        Ray ray = new Ray(localFrom, Vector3.Normalize(localTo - localFrom));
                        float? num3 = ray.Intersects(block.PositionComp.LocalAABB);
                        float? num4 = num3;
                        num3 = (num4.HasValue ? new float?(num4.GetValueOrDefault() + 0.01f) : null);

                        if(num3.HasValue)
                        {
                            if(num3.GetValueOrDefault() <= MAX_DISTANCE && num3.HasValue)
                            {
                                var detectionPoint = rayFrom + rayDir * num3.Value;

                                DetectionInfo info;
                                if(entitiesInRange.TryGetValue(parent.EntityId, out info))
                                {
                                    if(Vector3.DistanceSquared(info.DetectionPoint, rayFrom) > Vector3.DistanceSquared(detectionPoint, rayFrom))
                                    {
                                        entitiesInRange[parent.EntityId] = new DetectionInfo(parent, detectionPoint);
                                    }
                                }
                                else
                                {
                                    entitiesInRange[parent.EntityId] = new DetectionInfo(parent, detectionPoint);
                                }
                            }
                        }
                    }
                }
            }

            if(entitiesInRange.Count > 0)
            {
                float prevDistSq = 3.40282347E+38f;
                IMyEntity lastDetectedEntity = null;
                Vector3D hitPosition = Vector3D.Zero;

                foreach(var info in entitiesInRange.Values)
                {
                    float distSq = (float)Vector3D.DistanceSquared(info.DetectionPoint, rayFrom);

                    if(info.Entity.Physics != null && info.Entity.Physics.Enabled && distSq < prevDistSq)
                    {
                        lastDetectedEntity = info.Entity;
                        hitPosition = info.DetectionPoint;
                        prevDistSq = distSq;
                    }
                }

                targetGrid = lastDetectedEntity as MyCubeGrid;

                if(targetGrid != null)
                {
                    MatrixD gridWMInv = targetGrid.PositionComp.WorldMatrixNormalizedInv;
                    Vector3D value = Vector3D.Transform(hitPosition, gridWMInv);
                    Vector3I pos;
                    targetGrid.FixTargetCube(out pos, value / (double)targetGrid.GridSize);
                    targetBlock = targetGrid.GetCubeBlock(pos);
                }
                else
                {
                    targetCharacter = lastDetectedEntity as IMyCharacter;
                }
            }

            hits.Clear();
            entitiesInRange.Clear();
            raycastResults.Clear();
        }

        public bool HoldingTool(bool trigger)
        {
            try
            {
                var character = MyAPIGateway.Session.Player.Character;

                selectedGrid = null;
                selectedCharacter = null;
                selectedSlimBlock = null;
                selectedInvalid = false;
                symmetryInput = false;

                IMyCharacter targetCharacter;
                IMyCubeGrid targetGrid;
                IMySlimBlock targetBlock;

                GetTarget(character, out targetGrid, out targetBlock, out targetCharacter);

                if(pickColorMode && targetCharacter != null)
                {
                    selectedCharacter = targetCharacter;

                    playersCache.Clear();
                    MyAPIGateway.Players.GetPlayers(playersCache, p => p.Character == selectedCharacter);

                    if(playersCache.Count == 0)
                        return false;

                    var targetPlayer = playersCache[0];

                    if(!playerColorData.ContainsKey(targetPlayer.SteamUserId))
                        return false;

                    var cd = playerColorData[targetPlayer.SteamUserId];
                    var targetColor = cd.colors[cd.selectedSlot];

                    if(trigger)
                    {
                        SendToServer_ColorPickMode(false);

                        if(SendToServer_SetColor((byte)localColorData.selectedSlot, targetColor, true))
                        {
                            PlaySound(SOUND_HUD_MOUSE_CLICK, 0.25f);
                            MyAPIGateway.Utilities.ShowNotification("Color in slot " + localColorData.selectedSlot + " set to " + ColorMaskToStringShort(targetColor), 2000, MyFontEnum.White);
                        }
                        else
                        {
                            PlaySound(SOUND_HUD_UNABLE, 0.25f);
                        }

                        SetToolStatus(0, null);
                        SetToolStatus(1, null);
                        SetToolStatus(2, null);
                    }
                    else
                    {
                        SetCrosshairColor(HSVtoRGB(targetColor));

                        if(!targetColor.EqualsToHSV(prevColorPreview))
                        {
                            prevColorPreview = targetColor;

                            SetToolColor(targetColor);

                            if(settings.extraSounds)
                                PlaySound(SOUND_HUD_ITEM, 0.75f);
                        }

                        SetToolStatus(0, "Click to pick this player's selected color.", MyFontEnum.Green);
                        SetToolStatus(1, targetPlayer.DisplayName, MyFontEnum.White);
                        SetToolStatus(2, ColorMaskToStringShort(targetColor), MyFontEnum.White);
                    }

                    return false;
                }

                SetCrosshairColor(CROSSHAIR_NO_TARGET);

                var color = GetBuildColor();
                Vector3 blockColor;
                string blockName;

                if(!IsBlockValid(targetBlock, color, trigger, out blockName, out blockColor))
                    return false;

                selectedSlimBlock = targetBlock;
                selectedGrid = (MyCubeGrid)targetGrid;

                #region Symmetry toggle input
                symmetryStatus = null;

                if(!replaceAllMode && MyAPIGateway.Session.CreativeMode)
                {
                    if(selectedGrid.XSymmetryPlane.HasValue || selectedGrid.YSymmetryPlane.HasValue || selectedGrid.ZSymmetryPlane.HasValue)
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
                #endregion

                if(selectedSlimBlock != null)
                {
                    var grid = selectedGrid;

                    if(trigger)
                    {
                        float paintSpeed = (1.0f / GetBlockSurface(selectedSlimBlock));
                        PaintProcess(ref blockColor, color, paintSpeed, blockName);
                        SetCrosshairColor(CROSSHAIR_PAINTING);

                        if(replaceAllMode && MyAPIGateway.Session.CreativeMode)
                        {
                            SendToServer_ReplaceColor(grid.EntityId, grid, selectedSlimBlock.GetColorMask(), blockColor, replaceGridSystem);
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

                                SendToServer_PaintBlock(grid.EntityId, grid, selectedSlimBlock.Position, blockColor, true, mirrorPlane, odd);
                            }
                            else
                            {
                                SendToServer_PaintBlock(grid.EntityId, grid, selectedSlimBlock.Position, blockColor, false);
                            }
                        }

                        return true;
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
                            color = new Vector3(MathHelper.Clamp(values[0], 0, 360) / 360.0f, MathHelper.Clamp(values[1], 0, 100) / 100.0f, MathHelper.Clamp(values[2], 0, 100) / 100.0f);
                        else
                            color = new Color(MathHelper.Clamp(values[0], 0, 255), MathHelper.Clamp(values[1], 0, 255), MathHelper.Clamp(values[2], 0, 255)).ColorToHSVDX11();

                        if(SendToServer_SetColor((byte)localColorData.selectedSlot, color, true))
                        {
                            PlaySound(SOUND_HUD_MOUSE_CLICK, 0.25f);
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Color in slot " + localColorData.selectedSlot + " set to " + ColorMaskToString(color));
                        }
                        else
                        {
                            PlaySound(SOUND_HUD_UNABLE, 0.25f);
                        }

                        return;
                    }

                    var help = new StringBuilder();

                    var assignedLG = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.LANDING_GEAR));
                    var assignedCubeSize = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE));

                    help.Append("##### Commands #####").Append('\n');
                    help.Append('\n');
                    help.Append("/pg pick").Append('\n');
                    help.Append("  Activate color picker mode (hotkey: Shift+").Append(assignedLG).Append(")").Append('\n');
                    help.Append('\n');
                    help.Append("/pg rgb <0~255> <0~255> <0~255>").Append('\n');
                    help.Append("/pg rgb #<00~FF><00~FF><00~FF>").Append('\n');
                    help.Append("/pg hsv <0~360> <0~100> <0~100>").Append('\n');
                    help.Append("  Set the currently selected slot's color.").Append('\n');
                    help.Append('\n');
                    help.Append("/pg reload").Append('\n');
                    help.Append("  Reloads the config file.").Append('\n');
                    help.Append('\n');
                    help.Append("##### Hotkeys #####").Append('\n');
                    help.Append('\n');
                    help.Append("Shift+").Append(assignedLG).Append('\n');
                    help.Append("  Activate color picker mode.").Append('\n');
                    help.Append('\n');
                    help.Append("Shift+").Append(assignedCubeSize).Append('\n');
                    help.Append("  (Creative only) Toggle replace color mode.").Append('\n');
                    help.Append('\n');
                    help.Append("##### Config path #####").Append('\n');
                    help.Append('\n');
                    help.Append("%appdata%/SpaceEngineers/Storage").Append('\n');
                    help.Append("    /").Append(Log.workshopId).Append(".sbm_PaintGun/paintgun.cfg").Append('\n');

                    MyAPIGateway.Utilities.ShowMissionScreen("Paint Gun help", null, null, help.ToString(), null, "Close");
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
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

        public static void GetShipSubgrids(this MyCubeGrid grid, HashSet<MyCubeGrid> grids)
        {
            grids.Add(grid);
            GetSubgridsRecursive(grid, grids);
        }

        private static void GetSubgridsRecursive(MyCubeGrid grid, HashSet<MyCubeGrid> grids)
        {
            foreach(var block in grid.GetFatBlocks())
            {
                var g = GetGridFromBlock(block) as MyCubeGrid;

                if(g != null && !grids.Contains(g))
                {
                    grids.Add(g);
                    GetSubgridsRecursive(g, grids);
                }
            }
        }

        private static IMyCubeGrid GetGridFromBlock(MyCubeBlock block)
        {
            var motorStator = block as IMyMotorBase;

            if(motorStator != null)
                return motorStator.TopGrid;

            var motorRotor = block as IMyMotorRotor;

            if(motorRotor != null)
                return motorRotor.Base?.CubeGrid;

            var pistonBase = block as IMyPistonBase;

            if(pistonBase != null)
                return pistonBase.TopGrid;

            var pistonTop = block as IMyPistonTop;

            if(pistonTop != null)
                return pistonTop.Base?.CubeGrid;

            //var connector = block as IMyShipConnector;
            //
            //if(connector != null)
            //    return connector.OtherConnector?.CubeGrid;

            return null;
        }

        // copied from Sandbox.Game.Entities.MyCubeGrid because it's private
        public static bool ColorGridOrBlockRequestValidation(this IMyCubeGrid grid, long player)
        {
            if(player == 0L || grid.BigOwners.Count == 0)
                return true;

            foreach(long owner in grid.BigOwners)
            {
                var relation = GetRelationsBetweenPlayers(owner, player);

                if(relation == MyRelationsBetweenPlayers.Allies || relation == MyRelationsBetweenPlayers.Self)
                    return true;
            }

            return false;
        }

        // copied from Sandbox.Game.World.MyPlayer because it's not exposed
        private static MyRelationsBetweenPlayers GetRelationsBetweenPlayers(long id1, long id2)
        {
            if(id1 == id2)
                return MyRelationsBetweenPlayers.Self;

            if(id1 == 0L || id2 == 0L)
                return MyRelationsBetweenPlayers.Neutral;

            IMyFaction f1 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(id1);
            IMyFaction f2 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(id2);

            if(f1 == f2)
                return MyRelationsBetweenPlayers.Allies;

            if(f1 == null || f2 == null)
                return MyRelationsBetweenPlayers.Enemies;

            if(MyAPIGateway.Session.Factions.GetRelationBetweenFactions(f1.FactionId, f2.FactionId) == MyRelationsBetweenFactions.Neutral)
                return MyRelationsBetweenPlayers.Neutral;

            return MyRelationsBetweenPlayers.Enemies;
        }
    }
}
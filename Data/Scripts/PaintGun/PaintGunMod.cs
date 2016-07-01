using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Gui;
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
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class PaintGunMod : MySessionComponentBase
    {
        public static PaintGunMod instance = null;
        
        public bool init = false;
        public bool isThisHost = false;
        public bool isThisHostDedicated = false;
        public Settings settings = null;
        
        public bool pickColor = false;
        public bool symmetryInput = false;
        public string symmetryStatus = null;
        public MyCubeGrid grid = null; // currently selected grid by the local paint gun
        public IMySlimBlock selectedSlimBlock = null;
        public bool selectedInvalid = false;
        public Vector3 prevColorPreview;
        public Vector3 prevCustomColor = DEFAULT_COLOR;
        public IMyHudNotification[] toolStatus = new IMyHudNotification[4];
        public Vector3[] defaultColors = new Vector3[14];
        
        public Dictionary<ulong, Vector3> playerColors = new Dictionary<ulong, Vector3>();
        public Dictionary<ulong, IMyEntity> holdingTools = new Dictionary<ulong, IMyEntity>();
        public HashSet<ulong> playersColorPickMode = new HashSet<ulong>();
        
        public const string MOD_NAME = "PaintGun";
        public const string PAINT_GUN_ID = "PaintGun";
        public const string PAINT_MAG_ID = "PaintGunMag";
        public const float PAINT_SPEED = 1.0f;
        public const float DEPAINT_SPEED = 1.5f;
        public const int SKIP_UPDATES = 10;
        public static Vector3 DEFAULT_COLOR = new Vector3(0, -1, 0);
        public const float SAME_COLOR_RANGE = 0.001f;
        public static MyObjectBuilder_AmmoMagazine PAINT_MAG = new MyObjectBuilder_AmmoMagazine() { SubtypeName = PAINT_MAG_ID, ProjectilesCount = 1 };
        
        public const ushort PACKET = 9318;
        public static readonly Encoding encode = Encoding.Unicode;
        public const char SEPARATOR = ' ';
        
        public const int TOOLSTATUS_TIMEOUT = 200;
        public static readonly MyStringId CUSTOM_TEXT = MyStringId.GetOrCompute("CustomText");
        
        public static Color CROSSHAIR_NO_TARGET = new Color(255, 0, 0);
        public static Color CROSSHAIR_BAD_TARGET = new Color(255, 200, 0);
        public static Color CROSSHAIR_TARGET = new Color(0, 255, 0);
        public static Color CROSSHAIR_PAINTING = new Color(0, 255, 155);
        public static readonly MyStringId CROSSHAIR_SPRITEID = MyStringId.GetOrCompute("Default");
        
        private readonly StringBuilder assigned = new StringBuilder();
        
        public static HashSet<IMyEntity> ents = new HashSet<IMyEntity>();
        
        private byte skipColorUpdate = 0;
        
        public void Init()
        {
            init = true;
            instance = this;
            isThisHost = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer;
            isThisHostDedicated = (MyAPIGateway.Utilities.IsDedicated && isThisHost);
            
            Log.Init();
            Log.Info("Initialized");
            
            MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET, ReceivedPacket);
            
            if(!isThisHostDedicated) // stuff that shouldn't happen DS-side.
            {
                settings = new Settings();
                
                MyAPIGateway.Utilities.MessageEntered += MessageEntered;
                
                // snatched from MyPlayer.InitDefaultColors()
                defaultColors[0] = MyRenderComponentBase.OldGrayToHSV;
                defaultColors[1] = MyRenderComponentBase.OldRedToHSV;
                defaultColors[2] = MyRenderComponentBase.OldGreenToHSV;
                defaultColors[3] = MyRenderComponentBase.OldBlueToHSV;
                defaultColors[4] = MyRenderComponentBase.OldYellowToHSV;
                defaultColors[5] = MyRenderComponentBase.OldWhiteToHSV;
                defaultColors[6] = MyRenderComponentBase.OldBlackToHSV;
                
                for (int i = 7; i < defaultColors.Length; ++i)
                {
                    defaultColors[i] = (defaultColors[i - 7] + new Vector3(0, 0.15f, 0.2f));
                }
            }
        }
        
        protected override void UnloadData()
        {
            try
            {
                if(init)
                {
                    init = false;
                    
                    MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET, ReceivedPacket);
                    
                    if(settings != null)
                    {
                        settings.Close();
                        settings = null;
                    }
                    
                    Log.Info("Mod unloaded");
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
            
            Log.Close();
        }
        
        public void ReceivedPacket(byte[] bytes)
        {
            try
            {
                int index = 0;
                
                byte type = bytes[index];
                index += sizeof(byte);
                
                if(type >= 2 && MyAPIGateway.Multiplayer.IsServer) // relay to clients if it's any type except inventory because those get synchronized by the game
                {
                    var myId = MyAPIGateway.Multiplayer.MyId;
                    MyAPIGateway.Players.GetPlayers(new List<IMyPlayer>(), delegate(IMyPlayer p)
                                                    {
                                                        if(myId != p.SteamUserId)
                                                            MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, p.SteamUserId, true);
                                                        
                                                        return false;
                                                    });
                }
                
                switch(type)
                {
                    case 0: // inventory remove
                    case 1: // inventory add
                        {
                            long entId = BitConverter.ToInt64(bytes, index);
                            index += sizeof(long);
                            
                            if(!MyAPIGateway.Entities.EntityExists(entId))
                                return;
                            
                            var ent = MyAPIGateway.Entities.GetEntityById(entId) as MyEntity;
                            var inv = ent.GetInventory(0) as IMyInventory;
                            
                            if(inv == null)
                                return;
                            
                            if(type == 1)
                                inv.AddItems((MyFixedPoint)1, PAINT_MAG);
                            else
                                inv.RemoveItemsOfType((MyFixedPoint)1, PAINT_MAG, false);
                            break;
                        }
                    case 2: // block painted
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
                            
                            var slim = grid.GetCubeBlock(pos);
                            
                            if(slim == null)
                                return;
                            
                            var color = new Vector3(BitConverter.ToSingle(bytes, index),
                                                    BitConverter.ToSingle(bytes, index + sizeof(float)),
                                                    BitConverter.ToSingle(bytes, index + sizeof(float) * 2));
                            index += sizeof(float) * 3;
                            
                            grid.ChangeColor(slim, color);
                            
                            if(bytes.Length > index) // symmetry paint
                            {
                                var mirrorPlane = new Vector3I(BitConverter.ToInt32(bytes, index),
                                                               BitConverter.ToInt32(bytes, index + sizeof(int)),
                                                               BitConverter.ToInt32(bytes, index + sizeof(int) * 2));
                                index += sizeof(int) * 3;
                                
                                bool[] odd =
                                {
                                    BitConverter.ToBoolean(bytes, index),
                                    BitConverter.ToBoolean(bytes, index + sizeof(bool)),
                                    BitConverter.ToBoolean(bytes, index + sizeof(bool) * 2)
                                };
                                index += sizeof(bool) * 3;
                                
                                var mirrorX = MirrorPaint(grid, 0, mirrorPlane, odd[0], pos, color); // X
                                var mirrorY = MirrorPaint(grid, 1, mirrorPlane, odd[1], pos, color); // Y
                                var mirrorZ = MirrorPaint(grid, 2, mirrorPlane, odd[2], pos, color); // Z
                                Vector3I? mirrorYZ = null;
                                
                                if(mirrorX.HasValue && mirrorPlane.Y > int.MinValue) // XY
                                    MirrorPaint(grid, 1, mirrorPlane, odd[1], mirrorX.Value, color);
                                
                                if(mirrorX.HasValue && mirrorPlane.Z > int.MinValue) // XZ
                                    MirrorPaint(grid, 2, mirrorPlane, odd[2], mirrorX.Value, color);
                                
                                if(mirrorY.HasValue && mirrorPlane.Z > int.MinValue) // YZ
                                    mirrorYZ = MirrorPaint(grid, 2, mirrorPlane, odd[2], mirrorY.Value, color);
                                
                                if(mirrorPlane.X > int.MinValue && mirrorYZ.HasValue) // XYZ
                                    MirrorPaint(grid, 0, mirrorPlane, odd[0], mirrorYZ.Value, color);
                            }
                            
                            break;
                        }
                    case 3: // set tool color
                        {
                            ulong steamId = BitConverter.ToUInt64(bytes, index);
                            index += sizeof(ulong);
                            
                            var color = new Vector3(BitConverter.ToSingle(bytes, index),
                                                    BitConverter.ToSingle(bytes, index + sizeof(float)),
                                                    BitConverter.ToSingle(bytes, index + sizeof(float) * 2));
                            index += sizeof(float) * 3;
                            
                            if(!playerColors.ContainsKey(steamId))
                                playerColors.Add(steamId, color);
                            else
                                playerColors[steamId] = color;
                            
                            if(!holdingTools.ContainsKey(steamId))
                                return;
                            
                            var logic = holdingTools[steamId].GameLogic.GetAs<PaintGun>();
                            
                            if(logic == null)
                                return;
                            
                            logic.SetToolColor(color);
                            break;
                        }
                    case 4: // set color pick mode
                    case 5:
                        {
                            ulong steamId = BitConverter.ToUInt64(bytes, index);
                            index += sizeof(ulong);
                            
                            if(type == 4)
                            {
                                if(!playersColorPickMode.Contains(steamId))
                                    playersColorPickMode.Add(steamId);
                            }
                            else
                            {
                                playersColorPickMode.Remove(steamId);
                                
                                if(holdingTools.ContainsKey(steamId))
                                {
                                    var logic = holdingTools[steamId].GameLogic.GetAs<PaintGun>();
                                    
                                    if(logic == null)
                                        return;
                                    
                                    logic.cooldown = DateTime.UtcNow.Ticks;
                                }
                            }
                            break;
                        }
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
                        var slimX = g.GetCubeBlock(mirrorX);
                        
                        if(slimX != null)
                        {
                            g.ChangeColor(slimX, color);
                        }
                        
                        return mirrorX;
                    }
                    break;
                    
                case 1:
                    if(mirror.Y > int.MinValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((mirror.Y - originalPosition.Y) * 2) - (odd ? 1 : 0), 0);
                        var slimY = g.GetCubeBlock(mirrorY);
                        
                        if(slimY != null)
                        {
                            g.ChangeColor(slimY, color);
                        }
                        
                        return mirrorY;
                    }
                    break;
                    
                case 2:
                    if(mirror.Z > int.MinValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((mirror.Z - originalPosition.Z) * 2) + (odd ? 1 : 0)); // reversed on odd
                        var slimZ = g.GetCubeBlock(mirrorZ);
                        
                        if(slimZ != null)
                        {
                            g.ChangeColor(slimZ, color);
                        }
                        
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
                        var slimX = g.GetCubeBlock(mirrorX) as IMySlimBlock;
                        mirror = mirrorX;
                        
                        if(slimX != null)
                            return NearEqual(slimX.GetColorMask(), color, SAME_COLOR_RANGE);
                    }
                    break;
                    
                case 1:
                    if(g.YSymmetryPlane.HasValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((g.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (g.YSymmetryOdd ? 1 : 0), 0);
                        var slimY = g.GetCubeBlock(mirrorY) as IMySlimBlock;
                        mirror = mirrorY;
                        
                        if(slimY != null)
                            return NearEqual(slimY.GetColorMask(), color, SAME_COLOR_RANGE);
                    }
                    break;
                    
                case 2:
                    if(g.ZSymmetryPlane.HasValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((g.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (g.ZSymmetryOdd ? 1 : 0)); // reversed on odd
                        var slimZ = g.GetCubeBlock(mirrorZ) as IMySlimBlock;
                        mirror = mirrorZ;
                        
                        if(slimZ != null)
                            return NearEqual(slimZ.GetColorMask(), color, SAME_COLOR_RANGE);
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
                        var slimX = g.GetCubeBlock(mirrorX);
                        
                        if(slimX != null)
                        {
                            MyCubeBuilder.DrawSemiTransparentBox(g, slimX, Color.White, true, selectedInvalid ? "GizmoDrawLineRed" : "GizmoDrawLine", null);
                        }
                        
                        return mirrorX;
                    }
                    break;
                    
                case 1:
                    if(g.YSymmetryPlane.HasValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((g.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (g.YSymmetryOdd ? 1 : 0), 0);
                        var slimY = g.GetCubeBlock(mirrorY);
                        
                        if(slimY != null)
                        {
                            MyCubeBuilder.DrawSemiTransparentBox(g, slimY, Color.White, true, selectedInvalid ? "GizmoDrawLineRed" : "GizmoDrawLine", null);
                        }
                        
                        return mirrorY;
                    }
                    break;
                    
                case 2:
                    if(g.ZSymmetryPlane.HasValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((g.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (g.ZSymmetryOdd ? 1 : 0)); // reversed on odd
                        var slimZ = g.GetCubeBlock(mirrorZ);
                        
                        if(slimZ != null)
                        {
                            MyCubeBuilder.DrawSemiTransparentBox(g, slimZ, Color.White, true, selectedInvalid ? "GizmoDrawLineRed" : "GizmoDrawLine", null);
                        }
                        
                        return mirrorZ;
                    }
                    break;
            }
            
            return null;
        }
        
        public void SendAmmoPacket(long entId, int type)
        {
            try
            {
                int len = sizeof(byte) + sizeof(long);
                
                var bytes = new byte[len];
                bytes[0] = (byte)(type == 1 ? 1 : 0);
                len = 1;
                
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
        
        public void SendPaintPacket(long entId, Vector3I pos, Vector3 color, Vector3I? mirrorPlane = null, bool[] odd = null)
        {
            try
            {
                int len = sizeof(byte) + sizeof(long) + sizeof(int) * 3 + sizeof(float) * 3;
                
                if(mirrorPlane.HasValue && odd != null)
                    len += sizeof(int) * 3 + sizeof(bool) * 3;
                
                var bytes = new byte[len];
                bytes[0] = 2;
                len = 1;
                
                var data = BitConverter.GetBytes(entId);
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
                
                data = BitConverter.GetBytes(color.X);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;
                
                data = BitConverter.GetBytes(color.Y);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;
                
                data = BitConverter.GetBytes(color.Z);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;
                
                if(mirrorPlane.HasValue && odd != null)
                {
                    data = BitConverter.GetBytes(mirrorPlane.Value.X);
                    Array.Copy(data, 0, bytes, len, data.Length);
                    len += data.Length;
                    
                    data = BitConverter.GetBytes(mirrorPlane.Value.Y);
                    Array.Copy(data, 0, bytes, len, data.Length);
                    len += data.Length;
                    
                    data = BitConverter.GetBytes(mirrorPlane.Value.Z);
                    Array.Copy(data, 0, bytes, len, data.Length);
                    len += data.Length;
                    
                    data = BitConverter.GetBytes(odd[0]);
                    Array.Copy(data, 0, bytes, len, data.Length);
                    len += data.Length;
                    
                    data = BitConverter.GetBytes(odd[1]);
                    Array.Copy(data, 0, bytes, len, data.Length);
                    len += data.Length;
                    
                    data = BitConverter.GetBytes(odd[2]);
                    Array.Copy(data, 0, bytes, len, data.Length);
                    len += data.Length;
                }
                
                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public void SendToolColor(ulong myId, Vector3 color)
        {
            try
            {
                int len = sizeof(byte) + sizeof(ulong) + sizeof(float) * 3;
                
                var bytes = new byte[len];
                bytes[0] = 3;
                len = 1;
                
                var data = BitConverter.GetBytes(myId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;
                
                data = BitConverter.GetBytes(color.X);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;
                
                data = BitConverter.GetBytes(color.Y);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;
                
                data = BitConverter.GetBytes(color.Z);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;
                
                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public void SendColorPickMode(ulong myId, bool pickMode)
        {
            try
            {
                int len = sizeof(byte) + sizeof(ulong);
                
                var bytes = new byte[len];
                bytes[0] = (byte)(pickMode ? 4 : 5);
                len = 1;
                
                var data = BitConverter.GetBytes(myId);
                Array.Copy(data, 0, bytes, len, data.Length);
                len += data.Length;
                
                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
                
                pickColor = pickMode;
                prevColorPreview = GetBuildColor();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        private bool IsInColorPickerMenu()
        {
            string activeScreen = (MyGuiScreenGamePlay.ActiveGameplayScreen == null ? null : MyGuiScreenGamePlay.ActiveGameplayScreen.ToString());
            return activeScreen != null && activeScreen.EndsWith("ColorPicker", StringComparison.Ordinal);
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
                
                bool holdingPaintGun = holdingTools.ContainsKey(MyAPIGateway.Multiplayer.MyId);
                
                if(holdingPaintGun)
                {
                    if(++skipColorUpdate > 10)
                    {
                        skipColorUpdate = 0;
                        var player = MyAPIGateway.Session.Player as Sandbox.Game.World.MyPlayer;
                        SetBuildColor(player.SelectedBuildColor); // only updates if color is different
                    }
                    
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
                        
                        if(InputHandler.IsInputReadable() && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
                        {
                            MyAPIGateway.CubeBuilder.UseSymmetry = !MyAPIGateway.CubeBuilder.UseSymmetry;
                        }
                    }
                    
                    if(selectedSlimBlock != null)
                    {
                        if(selectedSlimBlock.IsDestroyed || selectedSlimBlock.IsFullyDismounted)
                        {
                            selectedSlimBlock = null;
                            return;
                        }
                        
                        MyCubeBuilder.DrawSemiTransparentBox(selectedSlimBlock.CubeGrid as MyCubeGrid, selectedSlimBlock as Sandbox.Game.Entities.Cube.MySlimBlock, Color.White, true, selectedInvalid ? "GizmoDrawLineRed" : "GizmoDrawLine", null);
                        
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
                toolStatus[line] = new MyHudNotification(CUSTOM_TEXT, aliveTime, font, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, 5, MyNotificationLevel.Normal);
            
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
        
        public static bool NearEqual(float val1, float val2, float epsilon = 0.01f)
        {
            return Math.Abs(val1 - val2) < epsilon;
        }
        
        public static bool NearEqual(Vector3 val1, Vector3 val2, float epsilon = 0.01f)
        {
            return (NearEqual(val1.X, val2.X, epsilon) && NearEqual(val1.Y, val2.Y, epsilon) && NearEqual(val1.Z, val2.Z, epsilon));
        }
        
        public static void SetCrosshairColor(Color? color)
        {
            if(color.HasValue)
                MyHud.Crosshair.AddTemporarySprite(MyHudTexturesEnum.crosshair, CROSSHAIR_SPRITEID, 1000, 500, color.Value, 0.02f);
            else
                MyHud.Crosshair.ResetToDefault(true);
        }
        
        public Vector3 GetBuildColor()
        {
            var player = MyAPIGateway.Session.Player as Sandbox.Game.World.MyPlayer;
            
            return (player == null ? DEFAULT_COLOR : player.SelectedBuildColor);
        }
        
        public void SetBuildColor(Vector3 color, bool save = true)
        {
            var player = MyAPIGateway.Session.Player as Sandbox.Game.World.MyPlayer;
            
            if(player == null)
                return;
            
            if(Vector3.DistanceSquared(color, prevCustomColor) <= 0.00001f)
                return;
            
            prevCustomColor = color;
            player.ChangeOrSwitchToColor(color);
            
            SendToolColor(MyAPIGateway.Multiplayer.MyId, color);
            
            if(save)
                settings.Save();
        }
        
        public static void PlaySound(string name, float volume)
        {
            var emitter = new MyEntity3DSoundEmitter(MyAPIGateway.Session.ControlledObject.Entity as MyEntity);
            emitter.CustomVolume = volume;
            emitter.PlaySingleSound(new MySoundPair(name));
        }
        
        private bool IsBlockValid(IMySlimBlock block, Vector3 color, bool trigger, out string blockName, out Vector3 blockColor)
        {
            if(block != null)
            {
                selectedSlimBlock = block;
                selectedInvalid = false;
                blockColor = block.GetColorMask();
                blockName = (block.FatBlock == null ? block.ToString() : block.FatBlock.DefinitionDisplayNameText);
                
                if(pickColor)
                {
                    if(trigger)
                    {
                        SendColorPickMode(MyAPIGateway.Multiplayer.MyId, false);
                        
                        SetBuildColor(blockColor);
                        
                        PlaySound("HudMouseClick", 0.25f);
                        
                        MyAPIGateway.Utilities.ShowNotification("Color set to "+ColorToStringShort(blockColor), 2000, MyFontEnum.White);
                        
                        SetToolStatus(0, null);
                        SetToolStatus(1, null);
                        SetToolStatus(2, null);
                        SetToolStatus(3, null);
                    }
                    else
                    {
                        SetCrosshairColor(HSVtoRGB(blockColor));
                        
                        if(Vector3.DistanceSquared(blockColor, prevColorPreview) > 0)
                        {
                            prevColorPreview = blockColor;
                            SendToolColor(MyAPIGateway.Multiplayer.MyId, blockColor);
                            
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
                
                if(block.CurrentDamage > (block.MaxIntegrity / 10.0f) || (block.FatBlock != null && !block.FatBlock.IsFunctional))
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
                
                if(NearEqual(blockColor, color, SAME_COLOR_RANGE))
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
            else
            {
                if(pickColor)
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
                    SetToolStatus(2, symmetryStatus, MyFontEnum.DarkBlue);
                    SetToolStatus(3, null);
                }
                
                blockName = null;
                blockColor = DEFAULT_COLOR;
                return false;
            }
        }
        
        public void PaintProcess(ref Vector3 blockColor, Vector3 color, float paintSpeed, string blockName)
        {
            if(MyAPIGateway.Session.CreativeMode)
            {
                blockColor = color;
                SetToolStatus(0, "Painted!", MyFontEnum.Blue);
                SetToolStatus(1, blockName, MyFontEnum.White);
                SetToolStatus(2, symmetryStatus, MyFontEnum.DarkBlue);
                SetToolStatus(3, null);
                return;
            }
            
            if(NearEqual(blockColor.X, color.X, 0.05f))
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
                
                if(NearEqual(blockColor, color, 0.001f))
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
                    byte percent = (byte)Math.Round(99 - ((MathHelper.Clamp(Vector3.Distance(blockColor, color), 0, 2.236f) / 2.236f) * 99), 0);
                    
                    SetToolStatus(0, "Painting " + percent + "%...", MyFontEnum.Blue);
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
                
                if(NearEqual(blockColor.Y, DEFAULT_COLOR.Y) && NearEqual(blockColor.Z, DEFAULT_COLOR.Z))
                {
                    blockColor.X = color.X;
                }
                
                if(NearEqual(blockColor, DEFAULT_COLOR))
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
                    byte percent = (byte)Math.Round(99 - ((MathHelper.Clamp(Vector3.Distance(blockColor, DEFAULT_COLOR), 0, 2.236f) / 2.236f) * 99), 0);
                    
                    SetToolStatus(0, "Removing paint " + percent + "%...", MyFontEnum.Blue);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, null);
                    SetToolStatus(3, null);
                }
            }
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
                var player = MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity;
                
                if(pickColor)
                {
                    var view = MyAPIGateway.Session.ControlledObject.GetHeadMatrix(false, true);
                    var ray = new RayD(view.Translation, view.Forward);
                    IMyEntity target = null;
                    double targetDistSq = 3;
                    Vector3 targetColor = DEFAULT_COLOR;
                    string targetName = null;
                    long myEntId = player.EntityId;
                    
                    MyAPIGateway.Entities.GetEntities(ents, delegate(IMyEntity e)
                                                      {
                                                          if(e is IMyCharacter && e.EntityId != myEntId && (e as IMyCharacter).IsPlayer)
                                                          {
                                                              var check = ray.Intersects(e.WorldAABB);
                                                              
                                                              if(check.HasValue && check.Value <= targetDistSq)
                                                              {
                                                                  var obj = e.GetObjectBuilder(false) as MyObjectBuilder_Character;
                                                                  
                                                                  if(playerColors.ContainsKey(obj.PlayerSteamId))
                                                                  {
                                                                      target = e;
                                                                      targetDistSq = check.Value;
                                                                      targetColor = playerColors[obj.PlayerSteamId];
                                                                      targetName = obj.Name;
                                                                  }
                                                              }
                                                          }
                                                          
                                                          return false;
                                                      });
                    
                    if(target != null)
                    {
                        if(trigger)
                        {
                            SendColorPickMode(MyAPIGateway.Multiplayer.MyId, false);
                            
                            SetBuildColor(targetColor);
                            
                            PlaySound("HudMouseClick", 0.25f);
                            
                            MyAPIGateway.Utilities.ShowNotification("Color set to "+ColorToStringShort(targetColor), 2000, MyFontEnum.White);
                            
                            SetToolStatus(0, null);
                            SetToolStatus(1, null);
                            SetToolStatus(2, null);
                            SetToolStatus(3, null);
                        }
                        else
                        {
                            SetCrosshairColor(HSVtoRGB(targetColor));
                            
                            if(Vector3.DistanceSquared(targetColor, prevColorPreview) > 0)
                            {
                                prevColorPreview = targetColor;
                                SendToolColor(MyAPIGateway.Multiplayer.MyId, targetColor);
                                
                                if(settings.extraSounds)
                                    PlaySound("HudItem", 0.75f);
                            }
                            
                            SetToolStatus(0, "Click to pick the color from this player.", MyFontEnum.Green);
                            SetToolStatus(1, targetName, MyFontEnum.White);
                            SetToolStatus(2, ColorToStringShort(targetColor), MyFontEnum.White);
                            SetToolStatus(3, null);
                        }
                        
                        return false;
                    }
                }
                
                grid = MyAPIGateway.CubeBuilder.FindClosestGrid() as MyCubeGrid;
                
                SetCrosshairColor(CROSSHAIR_NO_TARGET);
                
                if(grid == null)
                {
                    if(pickColor)
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
                    var block = GetTargetBlock(grid, player);
                    var color = GetBuildColor();
                    Vector3 blockColor;
                    string blockName;
                    symmetryStatus = null;
                    
                    if(MyAPIGateway.Session.CreativeMode)
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
                                    symmetryStatus = "Symmetry enabled. Press "+assigned+" to turn off.";
                                else
                                    symmetryStatus = "Symmetry is off. Press "+assigned+" to enable.";
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
                            
                            Vector3I? mirrorPlane = null;
                            bool[] odd = null;
                            
                            if(MyAPIGateway.Session.CreativeMode && MyAPIGateway.CubeBuilder.UseSymmetry && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue))
                            {
                                mirrorPlane = new Vector3I(
                                    (grid.XSymmetryPlane.HasValue ? grid.XSymmetryPlane.Value.X : int.MinValue),
                                    (grid.YSymmetryPlane.HasValue ? grid.YSymmetryPlane.Value.Y : int.MinValue),
                                    (grid.ZSymmetryPlane.HasValue ? grid.ZSymmetryPlane.Value.Z : int.MinValue));
                                
                                odd = new [] { grid.XSymmetryOdd, grid.YSymmetryOdd, grid.ZSymmetryOdd };
                            }
                            
                            SendPaintPacket(grid.EntityId, block.Position, blockColor, mirrorPlane, odd);
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
                        if(!holdingTools.ContainsKey(MyAPIGateway.Multiplayer.MyId))
                        {
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "You need to hold the tool for this to work.");
                        }
                        else
                        {
                            prevColorPreview = GetBuildColor();
                            SendColorPickMode(MyAPIGateway.Multiplayer.MyId, true);
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
                                values[c++] = Convert.ToInt32("" + msg[i-1] + msg[i], 16);
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
                                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Color argument "+(i+1)+" is not a valid number!");
                                    return;
                                }
                            }
                        }
                        
                        Vector3 color;
                        
                        if(hsv)
                            color = new Vector3(MathHelper.Clamp(values[0], 0, 360) / 360.0f, MathHelper.Clamp(values[1], -100, 100) / 100.0f, MathHelper.Clamp(values[2], -100, 100) / 100.0f);
                        else
                            color = new Color(MathHelper.Clamp(values[0], 0, 255), MathHelper.Clamp(values[1], 0, 255), MathHelper.Clamp(values[2], 0, 255)).ColorToHSVDX11();
                        
                        SetBuildColor(color);
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Color set to " + ColorToString(color));
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
    }
}
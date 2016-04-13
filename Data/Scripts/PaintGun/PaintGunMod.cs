using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Multiplayer;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using VRage.Common.Utils;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Gui;
using VRage.Game.ModAPI;
using VRage.Input;
using VRageMath;
using VRage;
using VRage.ObjectBuilders;
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
        public Vector3 prevColorPreview;
        public Vector3 customColor = DEFAULT_COLOR;
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
        
        public static HashSet<IMyEntity> ents = new HashSet<IMyEntity>();
        
        public void Init()
        {
            instance = this;
            init = true;
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
            init = false;
            
            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET, ReceivedPacket);
            
            if(settings != null)
            {
                settings.Close();
                settings = null;
            }
            
            Log.Info("Mod unloaded");
            Log.Close();
        }
        
        public void ReceivedPacket(byte[] bytes)
        {
            try
            {
                string[] data = encode.GetString(bytes).Split(SEPARATOR);
                int type = int.Parse(data[0]);
                int i = 1;
                
                switch(type)
                {
                    case 0: // inventory remove
                    case 1: // inventory add
                        {
                            long entId = long.Parse(data[i++]);
                            
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
                            if(MyAPIGateway.Multiplayer.IsServer)
                            {
                                var myId = MyAPIGateway.Multiplayer.MyId;
                                MyAPIGateway.Players.GetPlayers(new List<IMyPlayer>(), delegate(IMyPlayer p)
                                                                {
                                                                    if(myId != p.SteamUserId)
                                                                        MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, p.SteamUserId, true);
                                                                    
                                                                    return false;
                                                                });
                            }
                            
                            long entId = long.Parse(data[i++]);
                            
                            if(!MyAPIGateway.Entities.EntityExists(entId))
                                return;
                            
                            var ent = MyAPIGateway.Entities.GetEntityById(entId) as MyEntity;
                            var grid = (ent as MyCubeGrid);
                            
                            if(grid == null)
                                return;
                            
                            var pos = new Vector3I(int.Parse(data[i++]), int.Parse(data[i++]), int.Parse(data[i++]));
                            var slim = grid.GetCubeBlock(pos);
                            
                            if(slim == null)
                                return;
                            
                            var color = new Vector3(float.Parse(data[i++]), float.Parse(data[i++]), float.Parse(data[i++]));
                            grid.ChangeColor(slim, color);
                            
                            if(MyAPIGateway.Session.CreativeMode && data.Length > i) // symmetry paint
                            {
                                var mirrorX = MirrorPaint(grid, 0, pos, color);
                                var mirrorY = MirrorPaint(grid, 1, pos, color);
                                var mirrorZ = MirrorPaint(grid, 2, pos, color);
                                Vector3I? mirrorYZ = null;
                                
                                if(mirrorX.HasValue && grid.YSymmetryPlane.HasValue) // XY
                                    MirrorPaint(grid, 1, mirrorX.Value, color);
                                
                                if(mirrorX.HasValue && grid.ZSymmetryPlane.HasValue) // XZ
                                    MirrorPaint(grid, 2, mirrorX.Value, color);
                                
                                if(mirrorY.HasValue && grid.ZSymmetryPlane.HasValue) // YZ
                                    mirrorYZ = MirrorPaint(grid, 2, mirrorY.Value, color);
                                
                                if(grid.XSymmetryPlane.HasValue && mirrorYZ.HasValue) // XYZ
                                    MirrorPaint(grid, 0, mirrorYZ.Value, color);
                            }
                            
                            break;
                        }
                    case 3: // set tool color
                        {
                            if(MyAPIGateway.Multiplayer.IsServer)
                            {
                                var myId = MyAPIGateway.Multiplayer.MyId;
                                MyAPIGateway.Players.GetPlayers(new List<IMyPlayer>(), delegate(IMyPlayer p)
                                                                {
                                                                    if(myId != p.SteamUserId)
                                                                        MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, p.SteamUserId, true);
                                                                    
                                                                    return false;
                                                                });
                            }
                            
                            ulong steamId = ulong.Parse(data[i++]);
                            var color = new Vector3(float.Parse(data[i++]), float.Parse(data[i++]), float.Parse(data[i++]));
                            
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
                            if(MyAPIGateway.Multiplayer.IsServer)
                            {
                                var myId = MyAPIGateway.Multiplayer.MyId;
                                MyAPIGateway.Players.GetPlayers(new List<IMyPlayer>(), delegate(IMyPlayer p)
                                                                {
                                                                    if(myId != p.SteamUserId)
                                                                        MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, p.SteamUserId, true);
                                                                    
                                                                    return false;
                                                                });
                            }
                            
                            ulong steamId = ulong.Parse(data[i++]);
                            
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
                                    
                                    logic.lastPickTime = DateTime.UtcNow.Ticks;
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
        
        private Vector3I? MirrorPaint(MyCubeGrid grid, int axis, Vector3I originalPosition, Vector3 color)
        {
            switch(axis)
            {
                case 0:
                    if(grid.XSymmetryPlane.HasValue)
                    {
                        var mirrorX = originalPosition + new Vector3I(((grid.XSymmetryPlane.Value.X - originalPosition.X) * 2) - (grid.XSymmetryOdd ? 1 : 0), 0, 0);
                        var slimX = grid.GetCubeBlock(mirrorX);
                        
                        if(slimX != null)
                        {
                            grid.ChangeColor(slimX, color);
                        }
                        
                        return mirrorX;
                    }
                    break;
                    
                case 1:
                    if(grid.YSymmetryPlane.HasValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((grid.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (grid.YSymmetryOdd ? 1 : 0), 0);
                        var slimY = grid.GetCubeBlock(mirrorY);
                        
                        if(slimY != null)
                        {
                            grid.ChangeColor(slimY, color);
                        }
                        
                        return mirrorY;
                    }
                    break;
                    
                case 2:
                    if(grid.ZSymmetryPlane.HasValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((grid.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (grid.ZSymmetryOdd ? 1 : 0)); // reversed on odd
                        var slimZ = grid.GetCubeBlock(mirrorZ);
                        
                        if(slimZ != null)
                        {
                            grid.ChangeColor(slimZ, color);
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
                var data = new StringBuilder();
                data.Append(type == 1 ? 1 : 0);
                data.Append(SEPARATOR);
                data.Append(entId);
                var bytes = encode.GetBytes(data.ToString());
                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public void SendPaintPacket(long entId, Vector3I pos, Vector3 color, bool useSymmetry = false)
        {
            try
            {
                var data = new StringBuilder();
                data.Append(2);
                data.Append(SEPARATOR);
                data.Append(entId);
                data.Append(SEPARATOR);
                data.Append(pos.X);
                data.Append(SEPARATOR);
                data.Append(pos.Y);
                data.Append(SEPARATOR);
                data.Append(pos.Z);
                data.Append(SEPARATOR);
                data.Append(color.X);
                data.Append(SEPARATOR);
                data.Append(color.Y);
                data.Append(SEPARATOR);
                data.Append(color.Z);
                
                if(useSymmetry)
                {
                    data.Append(SEPARATOR);
                    data.Append(0);
                }
                
                var bytes = encode.GetBytes(data.ToString());
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
                var data = new StringBuilder();
                data.Append(3);
                data.Append(SEPARATOR);
                data.Append(myId);
                data.Append(SEPARATOR);
                data.Append(color.X);
                data.Append(SEPARATOR);
                data.Append(color.Y);
                data.Append(SEPARATOR);
                data.Append(color.Z);
                var bytes = encode.GetBytes(data.ToString());
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
                var data = new StringBuilder();
                data.Append(pickMode ? 4 : 5);
                data.Append(SEPARATOR);
                data.Append(myId);
                var bytes = encode.GetBytes(data.ToString());
                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
                
                pickColor = pickMode;
                prevColorPreview = GetBuildColor();
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
                
                if(holdingTools.ContainsKey(MyAPIGateway.Multiplayer.MyId))
                {
                    if(selectedSlimBlock != null)
                    {
                        if(selectedSlimBlock.IsDestroyed || selectedSlimBlock.IsFullyDismounted)
                        {
                            selectedSlimBlock = null;
                            return;
                        }
                        
                        MyCubeBuilder.DrawSemiTransparentBox(selectedSlimBlock.CubeGrid as MyCubeGrid, selectedSlimBlock as Sandbox.Game.Entities.Cube.MySlimBlock, Color.White, true, "GizmoDrawLine", null);
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
                                
                                var color = Color.Red * alpha;
                                
                                MyTransparentGeometry.AddQuad(material, ref quad, ref color, ref center, 0, -1);
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
                                
                                var color = Color.Green * alpha;
                                
                                MyTransparentGeometry.AddQuad(material, ref quad, ref color, ref center, 0, -1);
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
                                
                                var color = Color.Blue * alpha;
                                
                                MyTransparentGeometry.AddQuad(material, ref quad, ref color, ref center, 0, -1);
                            }
                        }
                        
                        if(MyGuiScreenGamePlay.ActiveGameplayScreen == null && MyGuiScreenTerminal.GetCurrentScreen() == MyTerminalPageEnum.None)
                        {
                            var controlSymmetry = MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY);
                            
                            if(controlSymmetry.IsNewPressed())
                                MyAPIGateway.CubeBuilder.UseSymmetry = !MyAPIGateway.CubeBuilder.UseSymmetry;
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
        
        public string ColorToString(Vector3 hsv)
        {
            return "Hue: " + Math.Round(hsv.X * 360) + "°, saturation: " + Math.Round(hsv.Y * 100) + ", value: " + Math.Round(hsv.Z * 100);
        }
        
        public string ColorToStringShort(Vector3 hsv)
        {
            return "HSV: " + Math.Round(hsv.X * 360) + "°, " + Math.Round(hsv.Y * 100) + ", " + Math.Round(hsv.Z * 100);
        }
        
        public bool NearEqual(float val1, float val2, float epsilon = 0.01f)
        {
            return Math.Abs(val1 - val2) < epsilon;
        }
        
        public bool NearEqual(Vector3 val1, Vector3 val2, float epsilon = 0.01f)
        {
            return (NearEqual(val1.X, val2.X, epsilon) && NearEqual(val1.Y, val2.Y, epsilon) && NearEqual(val1.Z, val2.Z, epsilon));
        }
        
        public void SetCrosshairColor(Color? color)
        {
            if(color.HasValue)
                MyHud.Crosshair.AddTemporarySprite(MyHudTexturesEnum.crosshair, CROSSHAIR_SPRITEID, 1000, 500, color.Value, 0.02f);
            else
                MyHud.Crosshair.ResetToDefault(true);
        }
        
        public Vector3 GetBuildColor()
        {
            return customColor;
        }
        
        public void SetBuildColor(Vector3 color, bool save = true)
        {
            if(Vector3.DistanceSquared(color, prevCustomColor) > 0)
            {
                prevCustomColor = customColor = color;
                SendToolColor(MyAPIGateway.Multiplayer.MyId, color);
                
                if(save)
                    settings.Save();
            }
        }
        
        public void PlaySound(string name, float volume)
        {
            var emitter = new MyEntity3DSoundEmitter(MyAPIGateway.Session.ControlledObject.Entity as MyEntity);
            emitter.CustomVolume = volume;
            emitter.PlaySingleSound(new MySoundPair(name));
        }
        
        private bool IsBlockValid(IMySlimBlock block, Vector3 color, bool trigger, out string blockName, out Vector3 blockColor)
        {
            if(block != null)
            {
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
                    
                    SetToolStatus(0, (block.FatBlock != null && !block.FatBlock.IsFunctional ? "Block not fully built!" : "Block is damaged!"), MyFontEnum.Red);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, null);
                    SetToolStatus(3, null);
                    
                    return false;
                }
                
                if(NearEqual(blockColor, color, 0.001f))
                {
                    SetCrosshairColor(CROSSHAIR_BAD_TARGET);
                    
                    SetToolStatus(0, "Already painted this color.", MyFontEnum.Red);
                    SetToolStatus(1, blockName, MyFontEnum.White);
                    SetToolStatus(2, symmetryStatus, MyFontEnum.DarkBlue);
                    SetToolStatus(3, null);
                    
                    return false;
                }
                
                SetCrosshairColor(CROSSHAIR_TARGET);
                
                if(!trigger)
                {
                    if(MyAPIGateway.Session.CreativeMode)
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
                
                if(blockColor.Z > 0)
                    blockColor.Z = Math.Max(blockColor.Z - paintSpeed, DEFAULT_COLOR.Z);
                else
                    blockColor.Z = Math.Min(blockColor.Z + paintSpeed, DEFAULT_COLOR.Z);
                
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
        
        private IMySlimBlock GetTargetBlock(IMyCubeGrid grid, IMyEntity player)
        {
            var view = MyAPIGateway.Session.ControlledObject.GetHeadMatrix(false, true);
            var rayFrom = view.Translation + view.Forward * 1.6;
            var rayTo = view.Translation + view.Forward * 5;
            var blockPos = grid.RayCastBlocks(rayFrom, rayTo);
            return (blockPos.HasValue ? grid.GetCubeBlock(blockPos.Value) : null);
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
                            StringBuilder assigned = new StringBuilder();
                            
                            if(controlSymmetry.GetKeyboardControl() != MyKeys.None)
                                assigned.Append(MyAPIGateway.Input.GetKeyName(controlSymmetry.GetKeyboardControl()));
                            
                            if(controlSymmetry.GetSecondKeyboardControl() != MyKeys.None)
                            {
                                if(assigned.Length > 0)
                                    assigned.Append(" or ");
                                
                                assigned.Append(MyAPIGateway.Input.GetKeyName(controlSymmetry.GetSecondKeyboardControl()));
                            }
                            
                            if(MyGuiScreenGamePlay.ActiveGameplayScreen == null && MyGuiScreenTerminal.GetCurrentScreen() == MyTerminalPageEnum.None)
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
                        selectedSlimBlock = block;
                        
                        if(trigger)
                        {
                            float paintSpeed = (1.0f / GetBlockSurface(block));
                            PaintProcess(ref blockColor, color, paintSpeed, blockName);
                            SetCrosshairColor(CROSSHAIR_PAINTING);
                            SendPaintPacket(grid.EntityId, block.Position, blockColor, MyAPIGateway.CubeBuilder.UseSymmetry);
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
                if(msg.StartsWith("/pg", StringComparison.InvariantCultureIgnoreCase))
                {
                    send = false;
                    msg = msg.Substring("/pg".Length).Trim().ToLower();
                    
                    if(msg.Equals("pick"))
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
                    else if(msg.Equals("reload"))
                    {
                        if(settings.Load())
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Reloaded and re-saved config.");
                        else
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Config created with the current settings.");
                        
                        settings.Save();
                        return;
                    }
                    else if(msg.StartsWith("default"))
                    {
                        msg = msg.Substring("default".Length).Trim();
                        
                        int num;
                        
                        if(!int.TryParse(msg, out num))
                        {
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Argument is not a number.");
                        }
                        else
                        {
                            num = MathHelper.Clamp(num, 1, 14);
                            SetBuildColor(defaultColors[num-1]);
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Got color from default " + num + " with color " + ColorToString(GetBuildColor()));
                        }
                        
                        return;
                    }
                    else if(msg.StartsWith("rgb") || msg.StartsWith("hsv"))
                    {
                        bool hsv = msg.StartsWith("hsv");
                        msg = msg.Substring("rgb".Length).Trim();
                        
                        string[] split = msg.Split(' ');
                        
                        if(split.Length != 3)
                        {
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Need to specify 3 numbers from 0 to 255 to create a RGB color.");
                        }
                        else
                        {
                            int[] values = new int[3];
                            
                            for(int i = 0; i < 3; i++)
                            {
                                if(!int.TryParse(split[i], out values[i]))
                                {
                                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Color argument "+(i+1)+" is not a valid number!");
                                    return;
                                }
                            }
                            
                            Vector3 color;
                            
                            if(hsv)
                            {
                                color = new Vector3(MathHelper.Clamp(values[0], 0, 360) / 360.0f, MathHelper.Clamp(values[1], -100, 100) / 100.0f, MathHelper.Clamp(values[2], -100, 100) / 100.0f);
                            }
                            else
                            {
                                color = new Color((int)MathHelper.Clamp(values[0], 0, 255), (int)MathHelper.Clamp(values[1], 0, 255), (int)MathHelper.Clamp(values[2], 0, 255)).ColorToHSVDX11();
                            }
                            
                            SetBuildColor(color);
                            MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Color set to " + ColorToString(color));
                        }
                        
                        return;
                    }
                    
                    MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Available commands:");
                    MyAPIGateway.Utilities.ShowMessage("/pg pick ", "pick a color from an existing block (alias: Shift+ColorMenu)");
                    MyAPIGateway.Utilities.ShowMessage("/pg default <1~15> ", "picks one of the default colors");
                    MyAPIGateway.Utilities.ShowMessage("/pg rgb <0~255> <0~255> <0~255> ", "set the color using RGB format");
                    MyAPIGateway.Utilities.ShowMessage("/pg hsv <0-360> <-100~100> <-100~100>", "set the color using HSV format");
                    MyAPIGateway.Utilities.ShowMessage("/pg reload ", "reloads the config file.");
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
    
    public class RenderWorkaround : MyCubeBlock
    {
        public static void SetEmissiveParts(uint renderObjectId, float emissivity, Color emissivePartColor, Color displayPartColor)
        {
            UpdateEmissiveParts(renderObjectId, emissivity, emissivePartColor, displayPartColor);
        }
    }
}
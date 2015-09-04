using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Multiplayer;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Common.Utils;
using VRageMath;
using VRage;
using VRage.ObjectBuilders;
using VRage.Components;
using VRage.ModAPI;
using VRage.Utils;
using Digi.Utils;

namespace Digi.PaintGun
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class PaintGun : MySessionComponentBase
    {
        public static bool init { get; private set; }
        public static bool isThisHost { get; private set; }
        public static bool isThisHostDedicated { get; private set; }
        
        public Vector3 customColor = DEFAULT_COLOR;
        public bool holdingTool = false;
        public bool pickColor = false;
        private int skipUpdates;
        private long lastShotTime = 0;
        private IMyHudNotification toolStatus;
        
        public const string MOD_NAME = "PaintGun";
        public const string PAINT_GUN_ID = "PaintGun";
        public const string PAINT_MAG_ID = "PaintGunMag";
        public const float PAINT_SPEED = 1.0f;
        public const float DEPAINT_SPEED = 1.5f;
        public const int SKIP_UPDATES = 10;
        public static Vector3 DEFAULT_COLOR = new Vector3(0, -1, 0);
        private static MyObjectBuilder_AmmoMagazine PAINT_MAG = new MyObjectBuilder_AmmoMagazine() { SubtypeName = PAINT_MAG_ID, ProjectilesCount = 1 };
        
        private Color prevCrosshairColor;
        private static Color CROSSHAIR_NO_TARGET = new Color(255, 0, 0);
        private static Color CROSSHAIR_BAD_TARGET = new Color(255, 200, 0);
        private static Color CROSSHAIR_TARGET = new Color(0, 255, 0);
        private static Color CROSSHAIR_PAINTING = new Color(0, 255, 155);
        
        private const int TOOLSTATUS_TIMEOUT = 200;
        
        private Vector3[] defaultColors = new Vector3[14];
        
        public void Init()
        {
            init = true;
            isThisHost = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer;
            isThisHostDedicated = (MyAPIGateway.Utilities.IsDedicated && isThisHost);
            
            if(isThisHostDedicated)
                return;
            
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
        
        protected override void UnloadData()
        {
            init = false;
            
            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
        }
        
        public override void UpdateAfterSimulation()
        {
            try
            {
                if(!init)
                {
                    if(MyAPIGateway.Session == null)
                        return;
                    
                    Init();
                }
                
                if(!isThisHostDedicated && MyAPIGateway.Session.Player != null && MyAPIGateway.Session.Player.Controller != null && MyAPIGateway.Session.Player.Controller.ControlledEntity != null && MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity != null)
                {
                    var player = MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity;
                    
                    if(player is IMyCharacter)
                    {
                        var character = player.GetObjectBuilder(false) as MyObjectBuilder_Character;
                        var tool = character.HandWeapon as MyObjectBuilder_AutomaticRifle;
                        
                        if(tool != null && tool.SubtypeName == PAINT_GUN_ID)
                        {
                            if(!holdingTool)
                            {
                                DrawTool();
                                lastShotTime = tool.GunBase.LastShootTime;
                            }
                            
                            if(++skipUpdates >= SKIP_UPDATES)
                            {
                                skipUpdates = 0;
                                bool trigger = tool.GunBase.LastShootTime + (TimeSpan.TicksPerMillisecond * 200) > DateTime.UtcNow.Ticks;
                                bool painted = HoldingTool(trigger);
                                
                                // expend the ammo manually when painting
                                if(painted && !MyAPIGateway.Session.CreativeMode)
                                {
                                    var inv = ((IMyInventoryOwner)player).GetInventory(0) as Sandbox.ModAPI.IMyInventory;
                                    inv.RemoveItemsOfType((MyFixedPoint)1, PAINT_MAG, false);
                                }
                            }
                            
                            // always add the shot ammo back
                            if(tool.GunBase.LastShootTime > lastShotTime)
                            {
                                lastShotTime = tool.GunBase.LastShootTime;
                                
                                if(!MyAPIGateway.Session.CreativeMode)
                                {
                                    var inv = ((IMyInventoryOwner)player).GetInventory(0) as Sandbox.ModAPI.IMyInventory;
                                    inv.AddItems((MyFixedPoint)1, PAINT_MAG);
                                }
                            }
                            
                            return;
                        }
                    }
                }
                
                if(holdingTool)
                {
                    HolsterTool();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        private void SetToolStatus(string text, MyFontEnum font, int aliveTime = TOOLSTATUS_TIMEOUT)
        {
            if(toolStatus == null)
            {
                toolStatus = MyAPIGateway.Utilities.CreateNotification(text, aliveTime, font);
            }
            else
            {
                toolStatus.Font = font;
                toolStatus.Text = text;
                toolStatus.AliveTime = aliveTime;
                toolStatus.Show();
            }
        }
        
        private string ColorToString(Vector3 hsv)
        {
            return "Hue: " + (hsv.X * 360) + "*, saturation: " + (hsv.Y * 100) + ", value: " + (hsv.Z * 100);
        }
        
        private bool NearEqual(float val1, float val2, float epsilon = 0.01f)
        {
            return Math.Abs(val1 - val2) < epsilon;
        }
        
        private bool NearEqual(Vector3 val1, Vector3 val2, float epsilon = 0.01f)
        {
            return (NearEqual(val1.X, val2.X, epsilon) && NearEqual(val1.Y, val2.Y, epsilon) && NearEqual(val1.Z, val2.Z, epsilon));
        }
        
        public void DrawTool()
        {
            holdingTool = true;
            
            prevCrosshairColor = Sandbox.Game.Gui.MyHud.Crosshair.Color;
            
            SetToolStatus("Type /pg for Paint Gun options.", MyFontEnum.DarkBlue, 3000);
        }
        
        private void SetCrosshairColor(Color color)
        {
            if(Sandbox.Game.Gui.MyHud.Crosshair.Color != color)
                Sandbox.Game.Gui.MyHud.Crosshair.Color = color;
        }
        
        private Vector3 GetBuildColor()
        {
            return customColor;
        }
        
        private void SetBuildColor(Vector3 color)
        {
            customColor = color;
        }
        
        private bool IsBlockValid(IMySlimBlock block, Vector3 color, bool trigger, out string blockName, out Vector3 blockColor)
        {
            if(block != null)
            {
                blockColor = block.GetColorMask();
                
                if(block.FatBlock == null)
                {
                    blockName = block.ToString();
                }
                else
                {
                    blockName = block.FatBlock.DefinitionDisplayNameText;
                }
                
                if(pickColor)
                {
                    if(trigger)
                    {
                        pickColor = false;
                        SetBuildColor(block.GetColorMask());
                        SetToolStatus("COLOR PICK MODE:\nColor picked from " + blockName + ".", MyFontEnum.Green);
                    }
                    else
                    {
                        SetToolStatus("COLOR PICK MODE:\n" + blockName + "'s color is "+ColorToString(block.GetColorMask())+"\nClick to pick this color.", MyFontEnum.Blue);
                    }
                    
                    return false;
                }
                
                if(block.HasDeformation || block.CurrentDamage > 0 || (block.FatBlock != null && !block.FatBlock.IsFunctional))
                {
                    if(block.CurrentDamage == 0 && block.FatBlock != null)
                    {
                        block.FatBlock.SetDamageEffect(false);
                    }
                    
                    SetCrosshairColor(CROSSHAIR_BAD_TARGET);
                    SetToolStatus("Paint target: " + blockName + "\n" + (block.HasDeformation || block.CurrentDamage > 0 ? "Block is damaged or deformed and can't be painted!" : "Block is not fully built and can't be painted!"), MyFontEnum.Red);
                    return false;
                }
                
                if(NearEqual(blockColor, color, 0.001f))
                {
                    SetCrosshairColor(CROSSHAIR_BAD_TARGET);
                    SetToolStatus(blockName + " is painted your selected color.", MyFontEnum.Green);
                    return false;
                }
                
                SetCrosshairColor(CROSSHAIR_TARGET);
                
                if(!trigger)
                {
                    SetToolStatus("Paint target: " + blockName, MyFontEnum.DarkBlue);
                }
                
                return true;
            }
            else
            {
                if(pickColor)
                {
                    SetToolStatus("COLOR PICK MODE:\nNo block target!", MyFontEnum.Red);
                }
                else if(trigger)
                {
                    SetToolStatus("No block target for painting.", MyFontEnum.Red);
                }
                
                blockName = null;
                blockColor = DEFAULT_COLOR;
                return false;
            }
        }
        
        private IMySlimBlock GetTargetBlock(IMyCubeGrid grid, IMyEntity player)
        {
            var view = MyAPIGateway.Session.ControlledObject.GetHeadMatrix(true, true);
            var target = player.WorldAABB.Center + (view.Forward * (grid.GridSizeEnum == MyCubeSize.Small ? 1.5 : 2)) + (view.Up * 0.6);
            var pos = grid.WorldToGridInteger(target);
            return grid.GetCubeBlock(pos);
        }
        
        private void PaintProcess(ref Vector3 blockColor, Vector3 color, float paintSpeed, string blockName)
        {
            if(MyAPIGateway.Session.CreativeMode)
            {
                blockColor = color;
                SetToolStatus("Painting " + blockName + "... done!", MyFontEnum.Blue);
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
                    
                    SetToolStatus("Painting " + blockName + "... 100% done!", MyFontEnum.Blue);
                }
                else
                {
                    byte percent = (byte)Math.Round(99 - ((MathHelper.Clamp(Vector3.Distance(blockColor, color), 0, 2.236f) / 2.236f) * 99), 0);
                    
                    SetToolStatus("Painting " + blockName + "... " + percent + "%", MyFontEnum.Blue);
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
                    
                    if(color != DEFAULT_COLOR)
                        SetToolStatus("Removing paint from " + blockName + "... 100%", MyFontEnum.Blue);
                    else
                        SetToolStatus("Removing paint from " + blockName + "... 100% done!", MyFontEnum.Blue);
                }
                else
                {
                    byte percent = (byte)Math.Round(99 - ((MathHelper.Clamp(Vector3.Distance(blockColor, DEFAULT_COLOR), 0, 2.236f) / 2.236f) * 99), 0);
                    
                    SetToolStatus("Removing paint from " + blockName + "... " + percent + "%", MyFontEnum.Blue);
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
        
        public bool HoldingTool(bool trigger)
        {
            try
            {
                var player = MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity;
                var grid = MyAPIGateway.CubeBuilder.FindClosestGrid();
                
                SetCrosshairColor(CROSSHAIR_NO_TARGET);
                
                if(grid == null)
                {
                    if(pickColor)
                    {
                        SetToolStatus("COLOR PICK MODE:\nNo ship target!", MyFontEnum.Red);
                    }
                    else if(trigger)
                    {
                        SetToolStatus("No ship target for painting.", MyFontEnum.Red);
                    }
                }
                else
                {
                    var block = GetTargetBlock(grid, player);
                    var color = GetBuildColor();
                    Vector3 blockColor;
                    string blockName;
                    
                    if(!IsBlockValid(block, color, trigger, out blockName, out blockColor))
                    {
                        return false;
                    }
                    
                    if(trigger && block != null)
                    {
                        float paintSpeed = (1.0f / GetBlockSurface(block));
                        
                        PaintProcess(ref blockColor, color, paintSpeed, blockName);
                        
                        SetCrosshairColor(CROSSHAIR_PAINTING);
                        
                        grid.ColorBlocks(block.Position, block.Position, blockColor);
                        
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
        
        public void HolsterTool()
        {
            holdingTool = false;
            
            if(pickColor)
            {
                pickColor = false;
                SetToolStatus("Color picking cancelled.", MyFontEnum.DarkBlue, 1000);
            }
            else if(toolStatus != null)
            {
                toolStatus.Hide();
            }
            
            if(prevCrosshairColor != null)
            {
                SetCrosshairColor(prevCrosshairColor);
            }
        }
        
        public void MessageEntered(string msg, ref bool send)
        {
            if(msg.StartsWith("/pg", StringComparison.InvariantCultureIgnoreCase))
            {
                send = false;
                msg = msg.Substring("/pg".Length).Trim().ToLower();
                
                if(msg.Equals("pick"))
                {
                    if(!holdingTool)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "You need to hold the tool for this to work.");
                    }
                    else
                    {
                        pickColor = true;
                    }
                    
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
                        num = MathHelper.Clamp(num, 1, 14) - 1;
                        SetBuildColor(defaultColors[num]);
                        MyAPIGateway.Utilities.ShowMessage(MOD_NAME, "Got color from default " + (num + 1) + " with color " + ColorToString(GetBuildColor()));
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
                MyAPIGateway.Utilities.ShowMessage("/pg pick ", "pick a color from an existing block");
                MyAPIGateway.Utilities.ShowMessage("/pg default <1~14> ", "picks one of the default colors");
                MyAPIGateway.Utilities.ShowMessage("/pg rgb <0~255> <0~255> <0~255> ", "set the color using RGB format");
                MyAPIGateway.Utilities.ShowMessage("/pg hsv <0-360> <-100~100> <-100~100>", "set the color using HSV format");
            }
        }
    }
}
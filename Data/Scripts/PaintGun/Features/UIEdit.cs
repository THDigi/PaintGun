#if false
using System;
using System.Text;
using Digi.ComponentLib;
using Digi.PaintGun.Utilities;
using Draygo.API;
using Sandbox.ModAPI;
using VRage.Game;
using VRageMath;

namespace Digi.PaintGun.Features
{
    public class UIEdit : ModComponent
    {
        private HudAPIv2.HUDMessage uiTitle => Mod.uiTitle;
        private HudAPIv2.BillBoardHUDMessage uiTitleBg => Mod.uiTitleBg;
        private HudAPIv2.HUDMessage uiText => Mod.uiText;
        private HudAPIv2.BillBoardHUDMessage uiTextBg => Mod.uiTextBg;
        private HudAPIv2.BillBoardHUDMessage uiTargetColor => Mod.uiTargetColor;
        private HudAPIv2.BillBoardHUDMessage uiPaintColor => Mod.uiPaintColor;
        private HudAPIv2.BillBoardHUDMessage uiProgressBar => Mod.uiProgressBar;
        private HudAPIv2.BillBoardHUDMessage uiProgressBarBg => Mod.uiProgressBarBg;
        private HudAPIv2.MessageBase[] ui => Mod.ui;

        public int uiEditSelected = -1;
        public int uiEditResolution = 3;
        public Vector2D uiEditAllOrigin = new Vector2D(0.642f, -0.22f);
        public Vector2D uiEditSlectedOriginOffset;
        public Vector2D uiEditSelectedOffset;
        public Vector2 uiEditSelectedSize;
        public double uiEditSelectedScale = 1.0;

        static bool ENABLED => false;

        public UIEdit(PaintGunMod main) : base(main)
        {
            if(ENABLED && MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE)
            {
                foreach(var mod in MyAPIGateway.Session.Mods)
                {
                    if(mod.Name == "PaintGun.dev")
                    {
                        uiEdit = new UIEdit();
                        break;
                    }
                }
            }
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }

        public override void UpdateAfterSim()
        {
            UIEditUpdate();
            UIEditApply();
        }

        public void UIEditUpdate()
        {
            if(uiTitle == null)
                return;

            //MyAPIGateway.Utilities.ShowNotification($"UIEdit mode is on!", 16);

            bool resolutionChanged = false;

            if(MyAPIGateway.Input.IsNewKeyPressed(MyKeys.OemPlus))
            {
                uiEditResolution++;
                resolutionChanged = true;
            }

            if(MyAPIGateway.Input.IsNewKeyPressed(MyKeys.OemMinus))
            {
                uiEditResolution--;
                resolutionChanged = true;
            }

            double resolution = 1 / Math.Pow(10, uiEditResolution);

            if(resolutionChanged)
            {
                MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Resolution={resolution:0.###########}; rawVal={uiEditResolution}", 1000);
                return;
            }

            int scroll = MathHelper.Clamp(MyAPIGateway.Input.DeltaMouseScrollWheelValue(), -1, 1);

            if(MyAPIGateway.Input.IsKeyPress(MyKeys.NumPad0))
            {
                MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Selected #{uiEditSelected}", 16);

                if(scroll == 0)
                    return;

                if(scroll > 0)
                {
                    if(++uiEditSelected >= ui.Length)
                        uiEditSelected = 0;
                }
                else
                {
                    if(--uiEditSelected < 0)
                        uiEditSelected = ui.Length - 1;
                }

                var msgBase = ui[uiEditSelected];

                if(msgBase is HudAPIv2.HUDMessage)
                {
                    var obj = (HudAPIv2.HUDMessage)msgBase;

                    uiEditSlectedOriginOffset = obj.Origin;
                    uiEditSelectedOffset = obj.Offset;
                    uiEditSelectedScale = obj.Scale;
                    uiEditSelectedSize = Vector2.Zero;
                }
                else if(msgBase is HudAPIv2.BillBoardHUDMessage)
                {
                    var obj = (HudAPIv2.BillBoardHUDMessage)msgBase;

                    uiEditSlectedOriginOffset = obj.Origin;
                    uiEditSelectedScale = obj.Scale;
                    uiEditSelectedSize = new Vector2(obj.Width, obj.Height);

                    if(uiEditSelected == 1 || uiEditSelected == 3 || uiEditSelected == 6 || uiEditSelected == 7)
                    {
                        uiEditSelectedOffset = obj.Offset - new Vector2D(obj.Width * 0.5f, obj.Height * -0.5f);
                    }
                    else
                    {
                        uiEditSelectedOffset = obj.Offset;
                    }
                }

                return;
            }

            if(uiEditSelected == -1)
                return;

            if(MyAPIGateway.Input.IsKeyPress(MyKeys.NumPad1))
            {
                //if(MyAPIGateway.Input.IsAnyShiftKeyPressed())
                {
                    MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Move ALL X = {uiEditAllOrigin.X:0.######}", 16);
                    if(scroll != 0) uiEditAllOrigin += new Vector2D(scroll * resolution, 0);
                }
                //else
                //{
                //    MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Move X = {uiEditSlectedOriginOffset.X:0.######}", 16);
                //    if(scroll != 0) uiEditSlectedOriginOffset += new Vector2D(scroll * resolution, 0);
                //}
                return;
            }
            if(MyAPIGateway.Input.IsKeyPress(MyKeys.NumPad2))
            {
                //if(MyAPIGateway.Input.IsAnyShiftKeyPressed())
                {
                    MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Move ALL Y = {uiEditAllOrigin.Y:0.######}", 16);
                    if(scroll != 0) uiEditAllOrigin += new Vector2D(0, scroll * resolution);
                }
                //else
                //{
                //    MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Move Y = {uiEditSlectedOriginOffset.Y:0.######}", 16);
                //    if(scroll != 0) uiEditSlectedOriginOffset += new Vector2D(0, scroll * resolution);
                //}
                return;
            }

            if(MyAPIGateway.Input.IsKeyPress(MyKeys.NumPad3))
            {
                MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Scale = {uiEditSelectedScale:0.######}", 16);
                if(scroll != 0) uiEditSelectedScale += (scroll * resolution);
                return;
            }

            if(MyAPIGateway.Input.IsKeyPress(MyKeys.NumPad4))
            {
                MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Size X = {uiEditSelectedSize.X:0.######}", 16);
                if(scroll != 0) uiEditSelectedSize += new Vector2((float)(scroll * resolution), 0);
                return;
            }
            if(MyAPIGateway.Input.IsKeyPress(MyKeys.NumPad5))
            {
                MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Size Y = {uiEditSelectedSize.Y:0.######}", 16);
                if(scroll != 0) uiEditSelectedSize += new Vector2(0, (float)(scroll * resolution));
                return;
            }

            if(MyAPIGateway.Input.IsKeyPress(MyKeys.NumPad7))
            {
                MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Offset X = {uiEditSelectedOffset.X:0.######}", 16);
                if(scroll != 0) uiEditSelectedOffset += new Vector2D(scroll * resolution, 0);
                return;
            }
            if(MyAPIGateway.Input.IsKeyPress(MyKeys.NumPad8))
            {
                MyAPIGateway.Utilities.ShowNotification($"UIEdit :: Offset Y = {uiEditSelectedOffset.Y:0.######}", 16);
                if(scroll != 0) uiEditSelectedOffset += new Vector2D(0, scroll * resolution);
                return;
            }
        }

        private void UIEditApply()
        {
            if(uiTitle == null)
                return;

            //for(int i = 0; i < ui.Length; ++i)
            //{
            //    var msgBase = ui[i];

            //    if(msgBase is HudAPIv2.HUDMessage)
            //    {
            //        var obj = (HudAPIv2.HUDMessage)msgBase;

            //        obj.Origin = uiEditAllOrigin;
            //    }
            //    else if(msgBase is HudAPIv2.BillBoardHUDMessage)
            //    {
            //        var obj = (HudAPIv2.BillBoardHUDMessage)msgBase;

            //        obj.Origin = uiEditAllOrigin;
            //    }
            //}

            if(uiEditSelected != -1)
            {
                var msgBase = ui[uiEditSelected];

                if(msgBase is HudAPIv2.HUDMessage)
                {
                    var obj = (HudAPIv2.HUDMessage)msgBase;

                    //obj.Origin += uiEditOriginOffset;
                    obj.Offset = uiEditSelectedOffset;
                    obj.Scale = uiEditSelectedScale;
                }
                else if(msgBase is HudAPIv2.BillBoardHUDMessage)
                {
                    var obj = (HudAPIv2.BillBoardHUDMessage)msgBase;

                    //obj.Origin += uiEditOriginOffset;
                    obj.Scale = uiEditSelectedScale;
                    obj.Width = uiEditSelectedSize.X;
                    obj.Height = uiEditSelectedSize.Y;

                    if(uiEditSelected == 1 || uiEditSelected == 3 || uiEditSelected == 6 || uiEditSelected == 7)
                    {
                        obj.Offset = new Vector2D(obj.Width * 0.5f, obj.Height * -0.5f) + uiEditSelectedOffset;
                    }
                    else
                    {
                        obj.Offset = uiEditSelectedOffset;
                    }
                }
            }

            //Mod.UpdateUISettings();
        }
    }
}
#endif
using System;
using System.Text;
using Draygo.API;
using Sandbox.ModAPI;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Digi.PaintGun.Features
{
    public class ColorPickerGUIWarning : ModComponent
    {
        HudAPIv2.HUDMessage skinDesyncWarning;
        HudAPIv2.HUDMessage skinDesyncWarningShadow;

        public ColorPickerGUIWarning(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
            MyAPIGateway.Gui.GuiControlCreated += GUIScreenOpened;
            MyAPIGateway.Gui.GuiControlRemoved += GUIScreenClosed;
        }

        protected override void UnregisterComponent()
        {
            MyAPIGateway.Gui.GuiControlCreated -= GUIScreenOpened;
            MyAPIGateway.Gui.GuiControlRemoved -= GUIScreenClosed;
        }

        void GUIScreenOpened(object screen)
        {
            try
            {
                if(!TextAPIEnabled || !screen.ToString().EndsWith("ColorPicker"))
                    return;

                if(skinDesyncWarning == null)
                {
                    const string TEXT = "PaintGun NOTE:\nThe 'Use color', 'Use skin' and 'skin selection' from this menu are not also selected for the PaintGun.\nYou can select skins and toggle color/skin directly with the PaintGun equipped.";
                    const double SCALE = 1.25;
                    var position = new Vector2D(0, 0.75);

                    skinDesyncWarningShadow = new HudAPIv2.HUDMessage(new StringBuilder(TEXT.Length + 24).Append("<color=0,0,0>").Append(TEXT), position, Scale: SCALE, HideHud: true, Blend: BlendTypeEnum.PostPP);
                    skinDesyncWarning = new HudAPIv2.HUDMessage(new StringBuilder(TEXT.Length + 24).Append("<color=255,255,0>").Append(TEXT), position, Scale: SCALE, HideHud: true, Blend: BlendTypeEnum.PostPP);

                    var textLen = skinDesyncWarning.GetTextLength();
                    skinDesyncWarning.Offset = new Vector2D(textLen.X * -0.5, 0);
                    skinDesyncWarningShadow.Offset = skinDesyncWarning.Offset + new Vector2D(0.0015, -0.0015);
                }
                else
                {
                    skinDesyncWarning.Visible = true;
                    skinDesyncWarningShadow.Visible = true;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        void GUIScreenClosed(object screen)
        {
            try
            {
                if(!TextAPIEnabled || !screen.ToString().EndsWith("ColorPicker"))
                    return;

                if(skinDesyncWarning != null)
                {
                    skinDesyncWarning.Visible = false;
                    skinDesyncWarningShadow.Visible = false;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}
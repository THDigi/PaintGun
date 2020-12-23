using System;
using System.Text;
using Digi.ComponentLib;
using Draygo.API;
using Sandbox.ModAPI;
using VRage;
using VRage.Input;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Digi.PaintGun.Features
{
    public class ColorPickerGUIWarning : ModComponent
    {
        HudAPIv2.HUDMessage text;
        bool hidden = false; // TODO: save to config?
        bool colorMenuVisible = false;

        const double TextScale = 1;

        readonly Vector2D TextPosition = new Vector2D(0.4, 0.95); // note: top-right pivot; top-right coords=1,1

        const string Text = "<color=yellow>NOTE: <color=white>Certain controls here do not affect the PaintGun:" +
                    "\n - '<color=red>{LOCG:ApplyColor}<color=white>' checkbox" +
                    "\n - '<color=red>{LOCG:ApplySkin}<color=white>' checkbox" +
                    "\n - <color=red>Skins list for selecting skin<color=white>" +
                    "\n" +
                    "\nInstead, <color=lime>equip the PaintGun and use the hotkeys<color=white>." +
                    "\n" +
                    "\nHold O and K to hide this message.";

        const string ScreenEndsWith = "ColorPicker";

        public ColorPickerGUIWarning(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
            MyAPIGateway.Gui.GuiControlCreated += GUIScreenOpened;
            MyAPIGateway.Gui.GuiControlRemoved += GUIScreenClosed;

            TextAPI.Detected += TextAPI_Detected;
        }

        protected override void UnregisterComponent()
        {
            MyAPIGateway.Gui.GuiControlCreated -= GUIScreenOpened;
            MyAPIGateway.Gui.GuiControlRemoved -= GUIScreenClosed;

            TextAPI.Detected -= TextAPI_Detected;
        }

        protected override void UpdateInput(bool anyKeyOrMouse, bool inMenu, bool paused)
        {
            if(!hidden && anyKeyOrMouse && inMenu && colorMenuVisible && MyAPIGateway.Input.IsKeyPress(MyKeys.O) && MyAPIGateway.Input.IsKeyPress(MyKeys.K))
            {
                hidden = true;
                HideText(permanent: true);
            }
        }

        void TextAPI_Detected()
        {
            // if menu was already open when textAPI loaded then show text.
            if(colorMenuVisible)
            {
                DrawText();
            }
        }

        void GUIScreenOpened(object screen)
        {
            try
            {
                if(!screen.GetType().Name.EndsWith(ScreenEndsWith))
                    return;

                colorMenuVisible = true;
                DrawText();
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
                if(!screen.GetType().Name.EndsWith(ScreenEndsWith))
                    return;

                colorMenuVisible = false;
                HideText();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        void DrawText()
        {
            if(hidden || !TextAPIEnabled)
                return;

            if(text == null)
            {
                text = new HudAPIv2.HUDMessage(new StringBuilder(MyTexts.GetString(Text)), TextPosition, Scale: TextScale, Shadowing: true, Blend: BlendTypeEnum.PostPP);

                var textSize = text.GetTextLength();
                text.Offset = new Vector2D(-textSize.X, textSize.Y); // pivot top-right
            }

            text.Visible = true;

            // enable input reading for hiding the text
            SetUpdateMethods(UpdateFlags.UPDATE_INPUT, true);
        }

        void HideText(bool permanent = false)
        {
            if(text != null)
            {
                text.Visible = false;

                if(permanent)
                {
                    text.DeleteMessage();
                    text = null;
                }
            }

            SetUpdateMethods(UpdateFlags.UPDATE_INPUT, false);
        }
    }
}
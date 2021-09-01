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

        const string Text = "<color=yellow>NOTE: <reset>PaintGun's skin selection and apply color/skin toggles are independent from the game's." +
                            "\n" +
                            "\nThis means that using game's color picker will not select the same skin for PaintGun." +
                            "\nOr using paintgun's skin selection/hotkeys or color picker will also not set those things for the game." +
                            "\nThis is a problem for newly placed blocks as they use the game's skin selection." +
                            "\n" +
                            "\nThe cause is the lacking modding API, there's no way to get/set those things for the game." +
                            "\n" +
                            "\n<color=200,200,200>(Hold O and K to hide this message)";

        const string ScreenEndsWith = "ColorPicker";

        public ColorPickerGUIWarning(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
            MyAPIGateway.Gui.GuiControlCreated += GUIScreenOpened;
            MyAPIGateway.Gui.GuiControlRemoved += GUIScreenClosed;

            Main.TextAPI.Detected += TextAPI_Detected;
        }

        protected override void UnregisterComponent()
        {
            if(!IsRegistered)
                return;

            MyAPIGateway.Gui.GuiControlCreated -= GUIScreenOpened;
            MyAPIGateway.Gui.GuiControlRemoved -= GUIScreenClosed;

            Main.TextAPI.Detected -= TextAPI_Detected;
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
            if(hidden || !Main.TextAPI.IsEnabled)
                return;

            if(text == null)
            {
                text = new HudAPIv2.HUDMessage(new StringBuilder(MyTexts.GetString(Text)), TextPosition, Scale: TextScale, Shadowing: true, Blend: BlendTypeEnum.PostPP);

                Vector2D textSize = text.GetTextLength();
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
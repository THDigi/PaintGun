using System;
using System.Text;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRageMath;

namespace Digi.PaintGun
{
    public partial class PaintGunMod
    {
        void MessageEntered(string msg, ref bool send)
        {
            try
            {
                const StringComparison COMPARE_TYPE = StringComparison.InvariantCultureIgnoreCase;

                if(msg.StartsWith("/pg", COMPARE_TYPE))
                {
                    send = false;
                    msg = msg.Substring("/pg".Length).Trim();

                    if(msg.StartsWith("reload", COMPARE_TYPE))
                    {
                        if(settings.Load())
                            MyVisualScriptLogicProvider.SendChatMessage("Reloaded and re-saved config.", MOD_NAME, 0, MyFontEnum.Green);
                        else
                            MyVisualScriptLogicProvider.SendChatMessage("Config created with the current settings.", MOD_NAME, 0, MyFontEnum.Green);

                        UpdateUISettings();
                        settings.Save();
                        return;
                    }

                    if(msg.StartsWith("pick", COMPARE_TYPE))
                    {
                        if(localHeldTool == null)
                        {
                            MyVisualScriptLogicProvider.SendChatMessage("You need to hold the paint gun for this to work.", MOD_NAME, 0, MyFontEnum.Red);
                        }
                        else
                        {
                            prevColorMaskPreview = GetLocalBuildColorMask();
                            SetColorPickMode(true);
                        }

                        return;
                    }

                    bool hsv = msg.StartsWith("hsv ", COMPARE_TYPE);

                    if(hsv || msg.StartsWith("rgb ", COMPARE_TYPE))
                    {
                        msg = msg.Substring(3).Trim();
                        var values = new float[3];

                        if(!hsv && msg.StartsWith("#", COMPARE_TYPE))
                        {
                            msg = msg.Substring(1).Trim();

                            if(msg.Length < 6)
                            {
                                MyVisualScriptLogicProvider.SendChatMessage("Invalid HEX color, needs 6 characters after #.", MOD_NAME, 0, MyFontEnum.Red);
                                return;
                            }

                            int c = 0;

                            for(int i = 1; i < 6; i += 2)
                            {
                                values[c++] = Convert.ToInt32(msg[i - 1].ToString() + msg[i].ToString(), 16);
                            }
                        }
                        else
                        {
                            string[] split = msg.Split(' ');

                            if(split.Length != 3)
                            {
                                MyVisualScriptLogicProvider.SendChatMessage("Need to specify 3 numbers separated by spaces.", MOD_NAME, 0, MyFontEnum.Red);
                                return;
                            }

                            for(int i = 0; i < 3; i++)
                            {
                                if(!float.TryParse(split[i], out values[i]))
                                {
                                    MyVisualScriptLogicProvider.SendChatMessage($"'{split[i]}' is not a valid number!", MOD_NAME, 0, MyFontEnum.Red);
                                    return;
                                }
                            }
                        }

                        Vector3 colorMask;

                        if(hsv)
                        {
                            colorMask = HSVToColorMask(new Vector3(MathHelper.Clamp(values[0], 0f, 360f) / 360.0f, MathHelper.Clamp(values[1], 0f, 100f) / 100.0f, MathHelper.Clamp(values[2], 0f, 100f) / 100.0f));
                        }
                        else
                        {
                            colorMask = RGBToColorMask(new Color(MathHelper.Clamp((int)values[0], 0, 255), MathHelper.Clamp((int)values[1], 0, 255), MathHelper.Clamp((int)values[2], 0, 255)));
                        }

                        var material = new BlockMaterial(colorMask, BlockSkins[localColorData.SelectedSkinIndex].SubtypeId);

                        PickColorAndSkinFromBlock(localColorData.SelectedSlot, material);
                        return;
                    }

                    var help = new StringBuilder();

                    var assignedLG = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.LANDING_GEAR));
                    var assignedSecondaryClick = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.SECONDARY_TOOL_ACTION));
                    var assignedCubeSize = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE));
                    var assignedColorBlock = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE));

                    var assignedColorPrev = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.SWITCH_LEFT));
                    var assignedColorNext = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.SWITCH_RIGHT));

                    help.Append("##### Commands #####").Append('\n');
                    help.Append('\n');
                    help.Append("/pg pick").Append('\n');
                    help.Append("  Activate color picker mode (hotkey: Shift+").Append(assignedLG).Append(')').Append('\n');
                    help.Append('\n');
                    help.Append("/pg rgb <0~255> <0~255> <0~255>").Append('\n');
                    help.Append("/pg rgb #<00~FF><00~FF><00~FF>").Append('\n');
                    help.Append("/pg hsv <0.0~360.0> <0.0~100.0> <0.0~100.0>").Append('\n');
                    help.Append("  Set the currently selected slot's color.").Append('\n');
                    help.Append('\n');
                    help.Append("/pg reload").Append('\n');
                    help.Append("  Reloads the config file.").Append('\n');
                    help.Append('\n');
                    help.Append("##### Hotkeys #####").Append('\n');
                    help.Append('\n');
                    help.Append("MouseScroll or ").Append(assignedColorPrev).Append("/").Append(assignedColorNext).Append('\n');
                    help.Append("  Change selected color slot.").Append('\n');
                    help.Append('\n');
                    help.Append("Shift+MouseScroll or Shift+").Append(assignedColorPrev).Append("/Shift+").Append(assignedColorNext).Append('\n');
                    help.Append("  Change selected skin.").Append('\n');
                    help.Append('\n');
                    help.Append(assignedColorBlock).Append('\n');
                    help.Append("  Toggle if color is applied.").Append('\n');
                    help.Append('\n');
                    help.Append("Shift+").Append(assignedColorBlock).Append('\n');
                    help.Append("  Toggle if skin is applied.").Append('\n');
                    help.Append('\n');
                    help.Append(assignedSecondaryClick).Append('\n');
                    help.Append("  Deep paint mode, allows painting under blocks if you're close enough.").Append('\n');
                    help.Append('\n');
                    help.Append("Shift+").Append(assignedSecondaryClick).Append('\n');
                    help.Append("  Replaces selected color with aimed block/player's color.").Append('\n');
                    help.Append('\n');
                    help.Append("Shift+").Append(assignedLG).Append('\n');
                    help.Append("  Toggle color picker mode.").Append('\n');
                    help.Append('\n');
                    help.Append("Shift+").Append(assignedCubeSize).Append('\n');
                    help.Append("  (Creative or SM) Toggle replace color mode.").Append('\n');
                    help.Append('\n');
                    help.Append("##### Config path #####").Append('\n');
                    help.Append('\n');
                    help.Append("%appdata%/SpaceEngineers/Storage").Append('\n');
                    help.Append("    /").Append(Log.WorkshopId).Append(".sbm_PaintGun/paintgun.cfg").Append('\n');

                    MyAPIGateway.Utilities.ShowMissionScreen("Paint Gun help", null, null, help.ToString(), null, "Close");
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}
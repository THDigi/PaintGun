﻿using Digi.ComponentLib;
using Sandbox.Game;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Entity.UseObject;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun.Features.Palette
{
    public class PaletteInputHandler : ModComponent
    {
        PlayerInfo LocalInfo => Main.Palette.LocalInfo;

        public PaletteInputHandler(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
            Main.CheckPlayerField.PlayerReady += PlayerReady;
        }

        protected override void UnregisterComponent()
        {
        }

        void PlayerReady()
        {
            Main.CheckPlayerField.PlayerReady -= PlayerReady;
            UpdateMethods = UpdateFlags.UPDATE_INPUT;
        }

        protected override void UpdateInput(bool anyKeyOrMouse, bool inMenu, bool paused)
        {
            if(!Main.CheckPlayerField.Ready)
                return;

            IMyPlayer player = MyAPIGateway.Session.Player;

            // monitor selected color slot while in color picker GUI
            if(inMenu && MyAPIGateway.Gui.ActiveGamePlayScreen == "ColorPick")
            {
#if false // color swap feature, doesn't visually update right away and can be confusing.
                if(MyAPIGateway.Input.IsRightMousePressed())
                {
                    if(LocalInfo.SelectedColorIndex != player.SelectedBuildColorSlot)
                    {
                        MyAPIGateway.Utilities.ShowMessage("DEBUG", $"Swap {LocalInfo.SelectedColorIndex} with {player.SelectedBuildColorSlot}");

                        Vector3 oldColor = LocalInfo.ColorsMasks[LocalInfo.SelectedColorIndex];
                        player.BuildColorSlots[LocalInfo.SelectedColorIndex] = player.BuildColorSlots[player.SelectedBuildColorSlot];
                        player.BuildColorSlots[player.SelectedBuildColorSlot] = oldColor;
                    }
                }
#endif

                LocalInfo.SelectedColorSlot = player.SelectedBuildColorSlot;
                LocalInfo.SetColorAt(LocalInfo.SelectedColorSlot, player.BuildColorSlots[LocalInfo.SelectedColorSlot]);
                return;
            }

            if(paused)
                return;

            bool controllingLocalChar = (MyAPIGateway.Session.ControlledObject == player?.Character);
            if(controllingLocalChar)
            {
                bool inputReadable = (InputHandler.IsInputReadable() && !MyAPIGateway.Session.IsCameraUserControlledSpectator);
                if(inputReadable)
                {
                    HandleInputs_CubeBuilderColorChange();

                    if(Main.LocalToolHandler.LocalTool != null)
                    {
                        HandleInputs_ToggleApplyColorOrSkin();
                        HandleInputs_CyclePalette();
                        HandleInputs_Symmetry();
                        HandleInputs_ColorPickMode();
                        HandleInputs_InstantColorPick();
                        HandleInputs_ReplaceMode();
                    }
                }
            }
        }

        void HandleInputs_CubeBuilderColorChange()
        {
            if(MyAPIGateway.CubeBuilder.IsActivated)
            {
                if(InputWrapper.IsControlJustPressed(MyControlsSpace.SWITCH_LEFT) || InputWrapper.IsControlJustPressed(MyControlsSpace.SWITCH_RIGHT))
                {
                    LocalInfo.SelectedColorSlot = MyAPIGateway.Session.Player.SelectedBuildColorSlot;
                }
            }
        }

        void HandleInputs_ToggleApplyColorOrSkin()
        {
            if(LocalInfo == null)
                return;

            if(!MyAPIGateway.Input.IsAnyAltKeyPressed() && InputHandler.IsControlPressedIgnoreBlock(MyControlsSpace.CUBE_COLOR_CHANGE, newPress: true))
            {
                bool skin = MyAPIGateway.Input.IsAnyShiftKeyPressed();
                if(skin)
                    LocalInfo.ApplySkin = !LocalInfo.ApplySkin;
                else
                    LocalInfo.ApplyColor = !LocalInfo.ApplyColor;
            }
        }

        void HandleInputs_CyclePalette()
        {
            if(LocalInfo == null)
                return;

            if(!LocalInfo.ApplySkin && !LocalInfo.ApplyColor)
                return;

            int cycleDir = 0;
            bool cycleSkins = false;

            if(Main.GameConfig.UsingGamepad)
            {
                // x button is used for both, if it's not pressed then ignore
                if(!MyAPIGateway.Input.IsJoystickButtonNewPressed(Constants.GamepadBind_CyclePalette))
                    return;

                cycleDir = -1; // cycle right
                cycleSkins = MyAPIGateway.Input.IsJoystickButtonPressed(Constants.GamepadBind_CycleSkinsModifier);

                if(!cycleSkins)
                {
                    IMyUseObject useObject = MyAPIGateway.Session?.Player?.Character?.Components?.Get<MyCharacterDetectorComponent>()?.UseObject;
                    if(useObject != null)
                        return; // aiming at an interactive object while pressing only X, ignore
                }
            }
            else // keyboard & mouse
            {
                if(MyAPIGateway.Input.IsAnyAltKeyPressed())
                    return; // ignore combos with alt to allow other systems to use these controls with alt

                cycleDir = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
                if(cycleDir == 0)
                {
                    if(InputWrapper.IsControlJustPressed(MyControlsSpace.SWITCH_RIGHT))
                        cycleDir = -1;
                    else if(InputWrapper.IsControlJustPressed(MyControlsSpace.SWITCH_LEFT))
                        cycleDir = 1;
                }

                if(cycleDir == 0)
                    return;

                cycleSkins = MyAPIGateway.Input.IsAnyShiftKeyPressed();
                bool ctrl = MyAPIGateway.Input.IsAnyCtrlKeyPressed();

                // ignore ctrl+shift+scroll combo, leave it to other systems
                if(cycleSkins && ctrl)
                    return;

                // color cycling requires it pressed or explicitly requires it not pressed depending on user pref
                if(!cycleSkins && Main.Settings.requireCtrlForColorCycle != ctrl)
                    return;
            }

            if(cycleDir == 0)
                return;

            if(cycleSkins && !Main.Palette.HasAnySkin)
                return;

            // skin or color applying is off, can't cycle turned off palette
            if(cycleSkins ? !LocalInfo.ApplySkin : !LocalInfo.UseColor)
            {
                Main.HUDSounds.PlayUnable();

                // there's no gamepad equivalent to toggle palettes so it'll just show kb/m binds for both.
                string assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE));

                bool showBind = true;
                string message = cycleSkins ? "Skin applying is turned off." : "Color applying is turned off.";

                if(!cycleSkins && !LocalInfo.SkinAllowsColor)
                {
                    message = $"[{LocalInfo.SelectedSkinInfo.Name}] skin cannot be colored.";
                    showBind = false;
                }

                Main.Notifications.Show(0, message, MyFontEnum.Red, 1000);

                if(showBind)
                    Main.Notifications.Show(1, cycleSkins ? $"Press [Shift] + [{assigned}] to enable" : $"Press [{assigned}] to enable", MyFontEnum.Debug, 1000);
                return;
            }

            if(cycleDir < 0)
            {
                if(cycleSkins)
                {
                    for(int i = 0; i < Main.Palette.SkinsForHUD.Count; i++)
                    {
                        SkinInfo skin = Main.Palette.SkinsForHUD[i];
                        if(skin.SubtypeId == LocalInfo.SelectedSkin)
                        {
                            i++;
                            if(i < Main.Palette.SkinsForHUD.Count)
                                LocalInfo.SelectedSkin = Main.Palette.SkinsForHUD[i].SubtypeId;
                            else
                                LocalInfo.SelectedSkin = MyStringHash.NullOrEmpty;
                            break;
                        }
                    }
                }
                else
                {
                    int slot = LocalInfo.SelectedColorSlot;
                    if(Main.Settings.selectColorZigZag)
                    {
                        if(slot >= 13)
                            slot = 0;
                        else if(slot >= 7)
                            slot -= 6;
                        else
                            slot += 7;
                    }
                    else
                    {
                        if(++slot >= LocalInfo.ColorsMasks.Count)
                            slot = 0;
                    }

                    LocalInfo.SelectedColorSlot = slot;
                }
            }
            else
            {
                if(cycleSkins)
                {
                    for(int i = (Main.Palette.SkinsForHUD.Count - 1); i >= 0; i--)
                    {
                        SkinInfo skin = Main.Palette.SkinsForHUD[i];
                        if(skin.SubtypeId == LocalInfo.SelectedSkin)
                        {
                            i--;
                            if(i >= 0)
                                LocalInfo.SelectedSkin = Main.Palette.SkinsForHUD[i].SubtypeId;
                            else
                                LocalInfo.SelectedSkin = Main.Palette.SkinsForHUD[Main.Palette.SkinsForHUD.Count - 1].SubtypeId;
                            break;
                        }
                    }
                }
                else
                {
                    int slot = LocalInfo.SelectedColorSlot;
                    if(Main.Settings.selectColorZigZag)
                    {
                        if(slot >= 7)
                            slot -= 7;
                        else
                            slot += 6;
                    }
                    else
                    {
                        if(--slot < 0)
                            slot = (LocalInfo.ColorsMasks.Count - 1);
                    }

                    LocalInfo.SelectedColorSlot = slot;
                }
            }

            if(cycleSkins)
            {
                Main.HUDSounds.PlayItem();
            }
            else
            {
                MyAPIGateway.Session.Player.SelectedBuildColorSlot = LocalInfo.SelectedColorSlot;
                Main.HUDSounds.PlayClick();
            }
        }

        void HandleInputs_Symmetry()
        {
            if(Main.LocalToolHandler.SymmetryInputAvailable && InputWrapper.IsControlJustPressed(MyControlsSpace.USE_SYMMETRY))
            {
                MyAPIGateway.CubeBuilder.UseSymmetry = !MyAPIGateway.CubeBuilder.UseSymmetry;
            }

            if(Main.Palette.ReplaceMode && InputWrapper.IsControlJustPressed(MyControlsSpace.USE_SYMMETRY))
            {
                Main.Palette.ReplaceShipWide = !Main.Palette.ReplaceShipWide;
            }
        }

        bool colorPickModeInputPressed;
        void HandleInputs_ColorPickMode()
        {
            if(InputHandler.GetPressedOr(Main.Settings.colorPickMode1, Main.Settings.colorPickMode2))
            {
                if(!colorPickModeInputPressed)
                {
                    colorPickModeInputPressed = true;

                    PreventIronSight();

                    if(LocalInfo.ColorPickMode)
                        Main.Notifications.Show(0, "Color pick mode turned off.", MyFontEnum.Debug, 2000);

                    Main.Palette.ColorPickMode = !Main.Palette.ColorPickMode;

                    if(Main.Palette.ColorPickMode && Main.Palette.ReplaceMode)
                    {
                        Main.Palette.ReplaceMode = false;
                        Main.Notifications.Show(3, "Replace color mode turned off.", MyFontEnum.Debug, 2000);
                    }
                }
            }
            else
            {
                colorPickModeInputPressed = false;
            }
        }

        bool colorPickInputPressed;
        void HandleInputs_InstantColorPick()
        {
            if(InputHandler.GetPressedOr(Main.Settings.instantColorPick1, Main.Settings.instantColorPick2))
            {
                if(!colorPickInputPressed)
                {
                    colorPickInputPressed = true;

                    PreventIronSight();

                    if(Main.LocalToolHandler.AimedBlock != null || Main.LocalToolHandler.AimedPlayer != null)
                    {
                        if(LocalInfo.ColorPickMode)
                            Main.Palette.ColorPickMode = false;

                        PaintMaterial paint;
                        if(Main.LocalToolHandler.AimedBlock != null)
                            paint = new PaintMaterial(Main.LocalToolHandler.AimedBlock.ColorMaskHSV, Main.LocalToolHandler.AimedBlock.SkinSubtypeId);
                        else
                            paint = Main.LocalToolHandler.AimedPlayersPaint;

                        Main.Palette.GrabPaletteFromPaint(paint);
                    }
                    else
                    {
                        Main.Notifications.Show(0, "First aim at a block or player.", MyFontEnum.Red, 2000);
                    }
                }
            }
            else
            {
                colorPickInputPressed = false;
            }
        }

        bool replaceAllModeInputPressed;
        private void HandleInputs_ReplaceMode()
        {
            if(InputHandler.GetPressedOr(Main.Settings.replaceColorMode1, Main.Settings.replaceColorMode2))
            {
                if(!replaceAllModeInputPressed)
                {
                    replaceAllModeInputPressed = true;

                    PreventIronSight();

                    if(Main.AccessReplaceColor(null))
                    {
                        Main.Palette.ReplaceMode = !Main.Palette.ReplaceMode;
                        Main.Notifications.Show(0, "Replace color mode " + (Main.Palette.ReplaceMode ? "enabled." : "turned off."), MyFontEnum.Debug, 2000);

                        if(Main.Palette.ReplaceMode && Main.Palette.ColorPickMode)
                        {
                            Main.Palette.ColorPickMode = false;
                            Main.Notifications.Show(0, "Color picking cancelled.", MyFontEnum.Debug, 2000);
                        }
                    }
                    else
                    {
                        Main.Notifications.Show(0, Main.ReplaceColorAccessInfo, MyFontEnum.Red, 3000);
                    }
                }
            }
            else if(replaceAllModeInputPressed)
            {
                replaceAllModeInputPressed = false;
            }
        }

        private void PreventIronSight()
        {
            IMyAutomaticRifleGun holdingTool = Main.LocalToolHandler?.LocalTool?.Rifle;
            if(holdingTool == null)
                return;

            // if the bind involves pressing rifle aim then revert action by doing it again.
            if(InputWrapper.IsControlJustPressed(MyControlsSpace.SECONDARY_TOOL_ACTION))
            {
                holdingTool.EndShoot(MyShootActionEnum.SecondaryAction);

                holdingTool.Shoot(MyShootActionEnum.SecondaryAction, Vector3.Forward, null, null);
                holdingTool.EndShoot(MyShootActionEnum.SecondaryAction);
            }
        }
    }
}
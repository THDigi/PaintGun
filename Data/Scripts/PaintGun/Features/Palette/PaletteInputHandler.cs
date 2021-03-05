using Digi.ComponentLib;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Input;
using VRageMath;

namespace Digi.PaintGun.Features.Palette
{
    public class PaletteInputHandler : ModComponent
    {
        PlayerInfo LocalInfo => Palette.LocalInfo;

        public PaletteInputHandler(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
            CheckPlayerField.PlayerReady += PlayerReady;
        }

        protected override void UnregisterComponent()
        {
        }

        void PlayerReady()
        {
            CheckPlayerField.PlayerReady -= PlayerReady;
            UpdateMethods = UpdateFlags.UPDATE_INPUT;
        }

        protected override void UpdateInput(bool anyKeyOrMouse, bool inMenu, bool paused)
        {
            // monitor selected color slot while in color picker GUI
            if(inMenu && MyAPIGateway.Gui.ActiveGamePlayScreen == "ColorPick")
            {
                LocalInfo.SelectedColorIndex = MyAPIGateway.Session.Player.SelectedBuildColorSlot;
                LocalInfo.SetColorAt(LocalInfo.SelectedColorIndex, MyAPIGateway.Session.Player.BuildColorSlots[LocalInfo.SelectedColorIndex]);
                return;
            }

            if(paused)
                return;

            bool controllingLocalChar = (MyAPIGateway.Session.ControlledObject == MyAPIGateway.Session.Player?.Character);

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
                if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_LEFT) || MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_RIGHT))
                {
                    LocalInfo.SelectedColorIndex = MyAPIGateway.Session.Player.SelectedBuildColorSlot;
                }
            }
        }

        void HandleInputs_ToggleApplyColorOrSkin()
        {
            if(LocalInfo == null)
                return;

            if(!MyAPIGateway.Input.IsAnyAltKeyPressed() && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.CUBE_COLOR_CHANGE))
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

            if(GameConfig.UsingGamepad)
            {
                // x button is used for both, if it's not pressed then ignore
                if(!MyAPIGateway.Input.IsJoystickButtonNewPressed(MyJoystickButtonsEnum.J03)) // X
                    return;

                cycleDir = -1; // cycle right
                cycleSkins = MyAPIGateway.Input.IsJoystickButtonPressed(MyJoystickButtonsEnum.J05); // LB

                if(!cycleSkins)
                {
                    var useObject = MyAPIGateway.Session?.Player?.Character?.Components?.Get<MyCharacterDetectorComponent>()?.UseObject;
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
                    if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_RIGHT))
                        cycleDir = -1;
                    else if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_LEFT))
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
                if(!cycleSkins && Settings.requireCtrlForColorCycle != ctrl)
                    return;
            }

            if(cycleDir == 0)
                return;

            if(cycleSkins && Palette.OwnedSkinsCount == 0)
                return; // no skins yet, ignore for now

            // skin or color applying is off, can't cycle turned off palette
            if(cycleSkins ? !LocalInfo.ApplySkin : !LocalInfo.ApplyColor)
            {
                if(Settings.extraSounds)
                    HUDSounds.PlayUnable();

                // there's no gamepad equivalent to toggle palettes so it'll just show kb/m binds for both.
                var assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE));
                Notifications.Show(0, cycleSkins ? "Skin applying is turned off." : "Color applying is turned off.", MyFontEnum.Red, 1000);
                Notifications.Show(1, cycleSkins ? $"Press [Shift] + [{assigned}] to enable" : $"Press [{assigned}] to enable", MyFontEnum.Debug, 1000);
                return;
            }

            if(cycleDir < 0)
            {
                if(cycleSkins)
                {
                    var index = LocalInfo.SelectedSkinIndex;
                    do
                    {
                        if(++index >= Palette.BlockSkins.Count)
                            index = 0;
                    }
                    while(!Palette.GetSkinInfo(index).Selectable);

                    LocalInfo.SelectedSkinIndex = index;
                }
                else
                {
                    var index = LocalInfo.SelectedColorIndex;
                    if(Settings.selectColorZigZag)
                    {
                        if(index >= 13)
                            index = 0;
                        else if(index >= 7)
                            index -= 6;
                        else
                            index += 7;
                    }
                    else
                    {
                        if(++index >= LocalInfo.ColorsMasks.Count)
                            index = 0;
                    }

                    LocalInfo.SelectedColorIndex = index;
                }
            }
            else
            {
                if(cycleSkins)
                {
                    var index = LocalInfo.SelectedSkinIndex;
                    do
                    {
                        if(--index < 0)
                            index = (Palette.BlockSkins.Count - 1);
                    }
                    while(!Palette.GetSkinInfo(index).Selectable);

                    LocalInfo.SelectedSkinIndex = index;
                }
                else
                {
                    var index = LocalInfo.SelectedColorIndex;
                    if(Settings.selectColorZigZag)
                    {
                        if(index >= 7)
                            index -= 7;
                        else
                            index += 6;
                    }
                    else
                    {
                        if(--index < 0)
                            index = (LocalInfo.ColorsMasks.Count - 1);
                    }

                    LocalInfo.SelectedColorIndex = index;
                }
            }

            if(cycleSkins)
            {
                if(Settings.extraSounds)
                    HUDSounds.PlayItem();
            }
            else
            {
                MyAPIGateway.Session.Player.SelectedBuildColorSlot = LocalInfo.SelectedColorIndex;

                if(Settings.extraSounds)
                    HUDSounds.PlayClick();
            }
        }

        void HandleInputs_Symmetry()
        {
            if(LocalToolHandler.SymmetryInputAvailable && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
            {
                MyAPIGateway.CubeBuilder.UseSymmetry = !MyAPIGateway.CubeBuilder.UseSymmetry;
            }

            if(Palette.ReplaceMode && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
            {
                Palette.ReplaceShipWide = !Palette.ReplaceShipWide;
            }
        }

        bool colorPickModeInputPressed;
        void HandleInputs_ColorPickMode()
        {
            if(InputHandler.GetPressedOr(Settings.colorPickMode1, Settings.colorPickMode2))
            {
                if(!colorPickModeInputPressed)
                {
                    colorPickModeInputPressed = true;

                    PreventIronSight();

                    if(LocalInfo.ColorPickMode)
                        Notifications.Show(0, "Color pick mode turned off.", MyFontEnum.Debug, 2000);

                    Palette.ColorPickMode = !Palette.ColorPickMode;

                    if(Palette.ColorPickMode && Palette.ReplaceMode)
                    {
                        Palette.ReplaceMode = false;
                        Notifications.Show(3, "Replace color mode turned off.", MyFontEnum.Debug, 2000);
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
            if(InputHandler.GetPressedOr(Settings.instantColorPick1, Settings.instantColorPick2))
            {
                if(!colorPickInputPressed)
                {
                    colorPickInputPressed = true;

                    PreventIronSight();

                    if(LocalToolHandler.AimedBlock != null || LocalToolHandler.AimedBlock != null)
                    {
                        if(LocalInfo.ColorPickMode)
                            Palette.ColorPickMode = false;

                        PaintMaterial paint;
                        if(LocalToolHandler.AimedBlock != null)
                            paint = new PaintMaterial(LocalToolHandler.AimedBlock.ColorMaskHSV, LocalToolHandler.AimedBlock.SkinSubtypeId);
                        else
                            paint = LocalToolHandler.AimedPlayersPaint;

                        Palette.GrabPaletteFromPaint(paint);
                    }
                    else
                    {
                        Notifications.Show(0, "First aim at a block or player.", MyFontEnum.Red, 2000);
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
            if(InputHandler.GetPressedOr(Settings.replaceColorMode1, Settings.replaceColorMode2))
            {
                if(!replaceAllModeInputPressed)
                {
                    replaceAllModeInputPressed = true;

                    PreventIronSight();

                    if(Main.ReplaceColorAccess)
                    {
                        Palette.ReplaceMode = !Palette.ReplaceMode;
                        Notifications.Show(0, "Replace color mode " + (Palette.ReplaceMode ? "enabled." : "turned off."), MyFontEnum.Debug, 2000);

                        if(Palette.ReplaceMode && Palette.ColorPickMode)
                        {
                            Palette.ColorPickMode = false;
                            Notifications.Show(0, "Color picking cancelled.", MyFontEnum.Debug, 2000);
                        }
                    }
                    else
                    {
                        Notifications.Show(0, Main.ReplaceColorAccessInfo, MyFontEnum.Red, 3000);
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
            var holdingTool = LocalToolHandler?.LocalTool?.Rifle;
            if(holdingTool == null)
                return;

            // if the bind involves pressing rifle aim then revert action by doing it again.
            if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SECONDARY_TOOL_ACTION))
            {
                holdingTool.EndShoot(MyShootActionEnum.SecondaryAction);

                holdingTool.Shoot(MyShootActionEnum.SecondaryAction, Vector3.Forward, null, null);
                holdingTool.EndShoot(MyShootActionEnum.SecondaryAction);
            }
        }
    }
}
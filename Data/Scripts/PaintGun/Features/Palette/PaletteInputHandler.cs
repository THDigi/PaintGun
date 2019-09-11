using Digi.ComponentLib;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;

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
                        HandleInputs_CycleColorOrSkin();
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

        void HandleInputs_CycleColorOrSkin()
        {
            if(LocalInfo == null)
                return;

            if(!LocalInfo.ApplySkin && !LocalInfo.ApplyColor)
                return;

            if(MyAPIGateway.Input.IsAnyAltKeyPressed())
                return;

            var change = 0;

            if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_LEFT))
                change = 1;
            else if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_RIGHT))
                change = -1;
            else
                change = MyAPIGateway.Input.DeltaMouseScrollWheelValue();

            if(change == 0 || LocalInfo == null)
                return;

            bool pickSkin = MyAPIGateway.Input.IsAnyShiftKeyPressed();

            if(pickSkin ? !LocalInfo.ApplySkin : !LocalInfo.ApplyColor)
            {
                if(Settings.extraSounds)
                    HUDSounds.PlayUnable();

                var assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE));
                Notifications.Show(0, pickSkin ? "Skin applying is turned off." : "Color applying is turned off.", MyFontEnum.Red, 1000);
                Notifications.Show(1, pickSkin ? $"Press [Shift] + [{assigned}] to enable" : $"Press [{assigned}] to enable", MyFontEnum.White, 1000);
                return;
            }

            if(pickSkin && Palette.OwnedSkins == 0)
                return;

            if(change < 0)
            {
                if(pickSkin)
                {
                    var index = LocalInfo.SelectedSkinIndex;
                    do
                    {
                        if(++index >= Palette.BlockSkins.Count)
                            index = 0;
                    }
                    while(!Palette.GetSkinInfo(index).LocallyOwned);
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
                if(pickSkin)
                {
                    var index = LocalInfo.SelectedSkinIndex;
                    do
                    {
                        if(--index < 0)
                            index = (Palette.BlockSkins.Count - 1);
                    }
                    while(!Palette.GetSkinInfo(index).LocallyOwned);
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

            if(pickSkin)
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

                    if(LocalInfo.ColorPickMode)
                        Notifications.Show(0, "Color pick mode turned off.", MyFontEnum.White, 2000);

                    Palette.ColorPickMode = !Palette.ColorPickMode;

                    if(Palette.ColorPickMode && Palette.ReplaceMode)
                    {
                        Palette.ReplaceMode = false;
                        Notifications.Show(3, "Replace color mode turned off.", MyFontEnum.White, 2000);
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

                    if(Main.ReplaceColorAccess)
                    {
                        Palette.ReplaceMode = !Palette.ReplaceMode;
                        Notifications.Show(0, "Replace color mode " + (Palette.ReplaceMode ? "enabled." : "turned off."), MyFontEnum.White, 2000);

                        if(Palette.ReplaceMode && Palette.ColorPickMode)
                        {
                            Palette.ColorPickMode = false;
                            Notifications.Show(0, "Color picking cancelled.", MyFontEnum.White, 2000);
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
    }
}
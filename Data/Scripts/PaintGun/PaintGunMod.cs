using System;
using System.Collections.Generic;
using System.Text;
using Digi.PaintGun.SkinOwnershipTester;
using Draygo.API;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Digi.PaintGun
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public partial class PaintGunMod : MySessionComponentBase
    {
        #region Init and unload
        public override void LoadData()
        {
            try
            {
                instance = this;
                Log.ModName = MOD_NAME;
                Log.AutoClose = false;

                BlockSkins = new List<SkinInfo>(9)
                {
                    new SkinInfo(0, MyStringHash.NullOrEmpty, "Default", "PaintGun_SkinIcon_Default"),
                    new SkinInfo(1, MyStringHash.GetOrCompute("Clean_Armor"), "Clean", "PaintGun_SkinIcon_Clean"),
                    new SkinInfo(2, MyStringHash.GetOrCompute("CarbonFibre_Armor"), "Carbon Fiber", "PaintGun_SkinIcon_CarbonFibre"),
                    new SkinInfo(3, MyStringHash.GetOrCompute("DigitalCamouflage_Armor"), "Digital Camouflage", "PaintGun_SkinIcon_DigitalCamouflage"),
                    new SkinInfo(4, MyStringHash.GetOrCompute("Golden_Armor"), "Golden", "PaintGun_SkinIcon_Golden"),
                    new SkinInfo(5, MyStringHash.GetOrCompute("Silver_Armor"), "Silver", "PaintGun_SkinIcon_Silver"),
                    new SkinInfo(6, MyStringHash.GetOrCompute("Glamour_Armor"), "Glamour", "PaintGun_SkinIcon_Glamour"),
                    new SkinInfo(7, MyStringHash.GetOrCompute("Disco_Armor"), "Disco", "PaintGun_SkinIcon_Disco"),
                    new SkinInfo(8, MyStringHash.GetOrCompute("Wood_Armor"), "Wood", "PaintGun_SkinIcon_Wood"),
                    new SkinInfo(9, MyStringHash.GetOrCompute("Mossy_Armor"), "Mossy", "PaintGun_SkinIcon_Mossy"),
                };
            }
            catch(Exception e)
            {
                Log.Error(e);
                Log.Close();
            }
        }

        public override void BeforeStart()
        {
            try
            {
                init = true;
                isDS = (MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer);

                MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET, ReceivedPacket);

                if(MyAPIGateway.Multiplayer.IsServer)
                {
                    ownershipTestServer = new OwnershipTestServer(this);
                }

                if(!isDS) // stuff that shouldn't happen DS-side.
                {
                    ownershipTestPlayer = new OwnershipTestPlayer(this);

                    UpdateConfigValues();

                    textAPI = new HudAPIv2(() => TextAPIReady = true);
                    settings = new Settings();

                    MyAPIGateway.Utilities.MessageEntered += MessageEntered;
                    MyAPIGateway.Gui.GuiControlCreated += GuiControlCreated;
                    MyAPIGateway.Gui.GuiControlRemoved += GuiControlRemoved;

                    if(!MyAPIGateway.Multiplayer.IsServer)
                        SendToServer_RequestColorList(MyAPIGateway.Multiplayer.MyId);

                    EnsureColorDataEntry(MyAPIGateway.Multiplayer.MyId);

                    if(localColorData == null)
                        playerColorData.TryGetValue(MyAPIGateway.Multiplayer.MyId, out localColorData);

                    InitUIEdit();
                }

                // make the paintgun not be able to shoot normally, to avoid needing to add ammo and the stupid hardcoded screen shake
                var gunDef = MyDefinitionManager.Static.GetWeaponDefinition(new MyDefinitionId(typeof(MyObjectBuilder_WeaponDefinition), PAINTGUN_WEAPONID));

                for(int i = 0; i < gunDef.WeaponAmmoDatas.Length; i++)
                {
                    var ammoData = gunDef.WeaponAmmoDatas[i];

                    if(ammoData == null)
                        continue;

                    ammoData.ShootIntervalInMiliseconds = int.MaxValue;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
                Log.Close();
            }
        }

        private void InitUIEdit()
        {
            if(UIEDIT && MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE)
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

        protected override void UnloadData()
        {
            instance = null;

            try
            {
                if(init)
                {
                    init = false;

                    MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET, ReceivedPacket);
                    MyAPIGateway.Gui.GuiControlCreated -= GuiControlCreated;
                    MyAPIGateway.Gui.GuiControlRemoved -= GuiControlRemoved;

                    settings?.Close();
                    settings = null;

                    ownershipTestServer?.Close();
                    ownershipTestServer = null;

                    ownershipTestPlayer?.Close();
                    ownershipTestPlayer = null;

                    textAPI?.Close();
                    textAPI = null;
                }

                hudSoundEmitter?.Cleanup();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Log.Close();
        }
        #endregion

        HudAPIv2.HUDMessage skinDesyncWarning;
        HudAPIv2.HUDMessage skinDesyncWarningShadow;

        private void GuiControlCreated(object obj)
        {
            try
            {
                var name = obj.ToString();

                if(name.EndsWith("ScreenColorPicker"))
                {
                    if(TextAPIReady)
                    {
                        if(skinDesyncWarning == null)
                        {
                            const string TEXT = "PaintGun NOTE:\nThe 'Use color', 'Use skin' and 'skin selection' from this menu are not also selected for the PaintGun.\nYou can select skins and toggle color/skin directly with the PaintGun equipped.";
                            const double SCALE = 1.25;
                            var position = new Vector2D(0, 0.75);

                            skinDesyncWarning = new HudAPIv2.HUDMessage(new StringBuilder("<color=255,255,0>").Append(TEXT), position, Scale: SCALE, HideHud: true, Blend: BlendTypeEnum.PostPP);
                            skinDesyncWarningShadow = new HudAPIv2.HUDMessage(new StringBuilder("<color=0,0,0>").Append(TEXT), position, Scale: SCALE, HideHud: true, Blend: BlendTypeEnum.SDR);

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
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        #region Game config monitor
        private void GuiControlRemoved(object obj)
        {
            try
            {
                var name = obj.ToString();

                if(name.EndsWith("ScreenColorPicker"))
                {
                    if(TextAPIReady && skinDesyncWarning != null)
                    {
                        skinDesyncWarning.Visible = false;
                        skinDesyncWarningShadow.Visible = false;
                    }
                }
                else if(name.EndsWith("ScreenOptionsSpace")) // closing options menu just assumes you changed something so it'll re-check config settings
                {
                    UpdateConfigValues();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        /// <summary>
        /// The config calls are slow so we're caching the ones we use when world loads or player exits the optins menu.
        /// </summary>
        private void UpdateConfigValues()
        {
            var cfg = MyAPIGateway.Session.Config;

            gameHUD = !cfg.MinimalHud;
            gameHUDBkOpacity = cfg.HUDBkOpacity;

            var viewportSize = MyAPIGateway.Session.Camera.ViewportSize;
            aspectRatio = (double)viewportSize.X / (double)viewportSize.Y;

            UpdateUISettings();
        }
        #endregion

        #region Painting methods
        private void PaintBlock(MyCubeGrid grid, Vector3I gridPosition, PaintMaterial paint, bool executedSenderSide)
        {
            // HACK getting a MySlimBlock and sending it straight to arguments avoids getting prohibited errors.
            if(!grid.ChangeColorAndSkin(grid.GetCubeBlock(gridPosition), paint.ColorMask, paint.Skin))
            {
                var block = (IMySlimBlock)grid.GetCubeBlock(gridPosition);
                Log.Error($"Couldn't paint/skin! mode={(paint.ColorMask.HasValue ? "paint" : "no-paint")}&{(paint.Skin.HasValue ? "skin" : "no-skin")}; owner={block.OwnerId}; gridPos={gridPosition}; grid={grid.EntityId}");
            }
        }

        private void ReplaceColorInGrid(MyCubeGrid grid, BlockMaterial oldPaint, PaintMaterial paint, bool useGridSystem, bool executedSenderSide)
        {
            gridsInSystemCache.Clear();

            if(useGridSystem)
                grid.GetShipSubgrids(gridsInSystemCache);
            else
                gridsInSystemCache.Add(grid);

            int affected = 0;

            foreach(var g in gridsInSystemCache)
            {
                foreach(IMySlimBlock slim in g.CubeBlocks)
                {
                    var blockMaterial = new BlockMaterial(slim);

                    if(paint.ColorMask.HasValue && !ColorMaskEquals(blockMaterial.ColorMask, oldPaint.ColorMask))
                        continue;

                    if(paint.Skin.HasValue && blockMaterial.Skin != oldPaint.Skin)
                        continue;

                    PaintBlock(g, slim.Position, paint, executedSenderSide);
                    affected++;
                }
            }

            if(executedSenderSide)
            {
                ShowNotification(2, $"Replaced color for {affected} blocks.", MyFontEnum.White, 2000);
            }
        }

        #region Symmetry
        private void PaintBlockSymmetry(MyCubeGrid grid, Vector3I gridPosition, PaintMaterial paint, Vector3I mirrorPlane, OddAxis odd, bool executedSenderSide)
        {
            PaintBlock(grid, gridPosition, paint, executedSenderSide);

            bool oddX = (odd & OddAxis.X) == OddAxis.X;
            bool oddY = (odd & OddAxis.Y) == OddAxis.Y;
            bool oddZ = (odd & OddAxis.Z) == OddAxis.Z;

            var mirrorX = MirrorPaint(grid, 0, mirrorPlane, oddX, gridPosition, paint, executedSenderSide); // X
            var mirrorY = MirrorPaint(grid, 1, mirrorPlane, oddY, gridPosition, paint, executedSenderSide); // Y
            var mirrorZ = MirrorPaint(grid, 2, mirrorPlane, oddZ, gridPosition, paint, executedSenderSide); // Z
            Vector3I? mirrorYZ = null;

            if(mirrorX.HasValue && mirrorPlane.Y > int.MinValue) // XY
                MirrorPaint(grid, 1, mirrorPlane, oddY, mirrorX.Value, paint, executedSenderSide);

            if(mirrorX.HasValue && mirrorPlane.Z > int.MinValue) // XZ
                MirrorPaint(grid, 2, mirrorPlane, oddZ, mirrorX.Value, paint, executedSenderSide);

            if(mirrorY.HasValue && mirrorPlane.Z > int.MinValue) // YZ
                mirrorYZ = MirrorPaint(grid, 2, mirrorPlane, oddZ, mirrorY.Value, paint, executedSenderSide);

            if(mirrorPlane.X > int.MinValue && mirrorYZ.HasValue) // XYZ
                MirrorPaint(grid, 0, mirrorPlane, oddX, mirrorYZ.Value, paint, executedSenderSide);
        }

        private Vector3I? MirrorPaint(MyCubeGrid g, int axis, Vector3I mirror, bool odd, Vector3I originalPosition, PaintMaterial paint, bool executedSenderSide)
        {
            switch(axis)
            {
                case 0:
                    if(mirror.X > int.MinValue)
                    {
                        var mirrorX = originalPosition + new Vector3I(((mirror.X - originalPosition.X) * 2) - (odd ? 1 : 0), 0, 0);

                        if(g.CubeExists(mirrorX))
                            PaintBlock(g, mirrorX, paint, executedSenderSide);

                        return mirrorX;
                    }
                    break;

                case 1:
                    if(mirror.Y > int.MinValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((mirror.Y - originalPosition.Y) * 2) - (odd ? 1 : 0), 0);

                        if(g.CubeExists(mirrorY))
                            PaintBlock(g, mirrorY, paint, executedSenderSide);

                        return mirrorY;
                    }
                    break;

                case 2:
                    if(mirror.Z > int.MinValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((mirror.Z - originalPosition.Z) * 2) + (odd ? 1 : 0)); // reversed on odd

                        if(g.CubeExists(mirrorZ))
                            PaintBlock(g, mirrorZ, paint, executedSenderSide);

                        return mirrorZ;
                    }
                    break;
            }

            return null;
        }

        private bool MirrorCheckSameColor(MyCubeGrid g, int axis, Vector3I originalPosition, PaintMaterial paintMaterial, out Vector3I? mirror)
        {
            mirror = null;

            switch(axis)
            {
                case 0:
                    if(g.XSymmetryPlane.HasValue)
                    {
                        var mirrorX = originalPosition + new Vector3I(((g.XSymmetryPlane.Value.X - originalPosition.X) * 2) - (g.XSymmetryOdd ? 1 : 0), 0, 0);
                        var slim = ((IMyCubeGrid)g).GetCubeBlock(mirrorX);
                        mirror = mirrorX;

                        if(slim != null)
                            return paintMaterial.PaintEquals(slim);
                    }
                    break;

                case 1:
                    if(g.YSymmetryPlane.HasValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((g.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (g.YSymmetryOdd ? 1 : 0), 0);
                        var slim = ((IMyCubeGrid)g).GetCubeBlock(mirrorY);
                        mirror = mirrorY;

                        if(slim != null)
                            return paintMaterial.PaintEquals(slim);
                    }
                    break;

                case 2:
                    if(g.ZSymmetryPlane.HasValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((g.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (g.ZSymmetryOdd ? 1 : 0)); // reversed on odd
                        var slim = ((IMyCubeGrid)g).GetCubeBlock(mirrorZ);
                        mirror = mirrorZ;

                        if(slim != null)
                            return paintMaterial.PaintEquals(slim);
                    }
                    break;
            }

            return true;
        }

        private Vector3I? MirrorHighlight(MyCubeGrid g, int axis, Vector3I originalPosition)
        {
            switch(axis)
            {
                case 0:
                    if(g.XSymmetryPlane.HasValue)
                    {
                        var mirrorX = originalPosition + new Vector3I(((g.XSymmetryPlane.Value.X - originalPosition.X) * 2) - (g.XSymmetryOdd ? 1 : 0), 0, 0);
                        var block = g.GetCubeBlock(mirrorX) as IMySlimBlock;

                        if(block != null)
                        {
                            // TODO: validations for drawing mirror and validations for applying paint mirrored
                            DrawBlockSelection(block);
                        }

                        return mirrorX;
                    }
                    break;

                case 1:
                    if(g.YSymmetryPlane.HasValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((g.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (g.YSymmetryOdd ? 1 : 0), 0);
                        var block = g.GetCubeBlock(mirrorY) as IMySlimBlock;

                        if(block != null)
                            DrawBlockSelection(block);

                        return mirrorY;
                    }
                    break;

                case 2:
                    if(g.ZSymmetryPlane.HasValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((g.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (g.ZSymmetryOdd ? 1 : 0)); // reversed on odd
                        var block = g.GetCubeBlock(mirrorZ) as IMySlimBlock;

                        if(block != null)
                            DrawBlockSelection(block);

                        return mirrorZ;
                    }
                    break;
            }

            return null;
        }
        #endregion
        #endregion

        public override void HandleInput()
        {
            try
            {
                if(isDS || MyAPIGateway.Session == null || MyParticlesManager.Paused)
                    return;

                bool controllingLocalChar = (MyAPIGateway.Session.ControlledObject == MyAPIGateway.Session.Player?.Character);
                bool inputReadable = (InputHandler.IsInputReadable() && !MyAPIGateway.Session.IsCameraUserControlledSpectator);

                MatchSelectedColorSlots(inputReadable, controllingLocalChar);

                // FIXME: added controlled check but needs to get rid of the fake block UI if you're already selecting something...
                if(controllingLocalChar && localHeldTool != null)
                {
                    if(inputReadable)
                    {
                        HandleInputs_Symmetry();
                        HandleInputs_ToggleApplyColorOrSkin();
                        HandleInputs_CycleColorOrSkin();
                        HandleInputs_ColorPickMode();
                        HandleInputs_InstantColorPick();
                        HandleInputs_ReplaceMode();
                    }

                    bool trigger = inputReadable && MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.PRIMARY_TOOL_ACTION);

                    HandleInputs_Trigger(trigger);
                    HandleInputs_Painting(trigger);
                }

                SendSelectedSlotToServer();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void MatchSelectedColorSlots(bool inputReadable, bool controllingLocalChar)
        {
            // apply selected slot when inside the color picker menu
            if(MyAPIGateway.Gui.IsCursorVisible && MyAPIGateway.Gui.ActiveGamePlayScreen == "ColorPick")
            {
                localColorData.SelectedSlot = (byte)MyAPIGateway.Session.Player.SelectedBuildColorSlot;
                SetToolColor(localColorData.Colors[localColorData.SelectedSlot]);
            }

            // apply selected slot when changing colors via cubebuilder
            if(inputReadable && controllingLocalChar && MyAPIGateway.CubeBuilder.IsActivated)
            {
                if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_LEFT) || MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_RIGHT))
                {
                    localColorData.SelectedSlot = (byte)MyAPIGateway.Session.Player.SelectedBuildColorSlot;
                    SetToolColor(localColorData.Colors[localColorData.SelectedSlot]);
                }
            }
        }

        private void SendSelectedSlotToServer()
        {
            if(localColorData == null || tick % 30 != 0)
                return;

            if(localColorData.SelectedSlot != prevSelectedColorSlot || localColorData.SelectedSkinIndex != prevSelectedSkinIndex)
            {
                prevSelectedColorSlot = (byte)localColorData.SelectedSlot;
                prevSelectedSkinIndex = (byte)localColorData.SelectedSkinIndex;
                SendToServer_SelectedSlots((byte)localColorData.SelectedSlot, (byte)localColorData.SelectedSkinIndex);
            }
        }

        private void HandleInputs_ToggleApplyColorOrSkin()
        {
            if(localColorData == null)
                return;

            if(!MyAPIGateway.Input.IsAnyAltKeyPressed() && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.CUBE_COLOR_CHANGE))
            {
                bool skin = MyAPIGateway.Input.IsAnyShiftKeyPressed();

                if(skin)
                {
                    localColorData.ApplySkin = !localColorData.ApplySkin;
                }
                else
                {
                    localColorData.ApplyColor = !localColorData.ApplyColor;

                    SetToolColor(localColorData.ApplyColor ? localColorData.Colors[localColorData.SelectedSlot] : DEFAULT_COLOR);
                }
            }
        }

        private void HandleInputs_CycleColorOrSkin()
        {
            if(localColorData == null)
                return;

            if(!localColorData.ApplySkin && !localColorData.ApplyColor)
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

            if(change == 0 || localColorData == null)
                return;

            bool pickSkin = MyAPIGateway.Input.IsAnyShiftKeyPressed();

            if(pickSkin ? !localColorData.ApplySkin : !localColorData.ApplyColor)
            {
                if(settings.extraSounds)
                    PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);

                var assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE));
                ShowNotification(0, pickSkin ? "Skin applying is turned off." : "Color applying is turned off.", MyFontEnum.Red, 1000);
                ShowNotification(1, pickSkin ? $"Press [Shift] + [{assigned}] to enable" : $"Press [{assigned}] to enable", MyFontEnum.White, 1000);
                return;
            }

            if(change < 0)
            {
                if(pickSkin)
                {
                    do
                    {
                        if(++localColorData.SelectedSkinIndex >= BlockSkins.Count)
                            localColorData.SelectedSkinIndex = 0;
                    }
                    while(!BlockSkins[localColorData.SelectedSkinIndex].LocallyOwned);
                }
                else
                {
                    if(settings.selectColorZigZag)
                    {
                        if(localColorData.SelectedSlot >= 13)
                            localColorData.SelectedSlot = 0;
                        else if(localColorData.SelectedSlot >= 7)
                            localColorData.SelectedSlot -= 6;
                        else
                            localColorData.SelectedSlot += 7;
                    }
                    else
                    {
                        if(++localColorData.SelectedSlot >= localColorData.Colors.Count)
                            localColorData.SelectedSlot = 0;
                    }
                }
            }
            else
            {
                if(pickSkin)
                {
                    do
                    {
                        if(--localColorData.SelectedSkinIndex < 0)
                            localColorData.SelectedSkinIndex = (byte)(BlockSkins.Count - 1);
                    }
                    while(!BlockSkins[localColorData.SelectedSkinIndex].LocallyOwned);
                }
                else
                {
                    if(settings.selectColorZigZag)
                    {
                        if(localColorData.SelectedSlot >= 7)
                            localColorData.SelectedSlot -= 7;
                        else
                            localColorData.SelectedSlot += 6;
                    }
                    else
                    {
                        if(--localColorData.SelectedSlot < 0)
                            localColorData.SelectedSlot = (byte)(localColorData.Colors.Count - 1);
                    }
                }
            }

            if(settings.extraSounds)
            {
                if(pickSkin)
                    PlayHudSound(SOUND_HUD_ITEM, 0.25f);
                else
                    PlayHudSound(SOUND_HUD_CLICK, 0.1f);
            }

            MyAPIGateway.Session.Player.SelectedBuildColorSlot = localColorData.SelectedSlot;
            SetToolColor(localColorData.Colors[localColorData.SelectedSlot]);
        }

        private void HandleInputs_Symmetry()
        {
            if(symmetryInputAvailable && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
            {
                MyAPIGateway.CubeBuilder.UseSymmetry = !MyAPIGateway.CubeBuilder.UseSymmetry;
            }

            if(replaceAllMode && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
            {
                replaceGridSystem = !replaceGridSystem;
            }
        }

        private void HandleInputs_ColorPickMode()
        {
            if(InputHandler.GetPressedOr(settings.colorPickMode1, settings.colorPickMode2))
            {
                if(!colorPickModeInputPressed)
                {
                    colorPickModeInputPressed = true;

                    if(colorPickMode)
                        ShowNotification(0, "Color pick mode turned off.", MyFontEnum.White, 2000);

                    SendToServer_ColorPickMode(!colorPickMode);
                }
            }
            else
            {
                colorPickModeInputPressed = false;
            }
        }

        private void HandleInputs_InstantColorPick()
        {
            if(InputHandler.GetPressedOr(settings.instantColorPick1, settings.instantColorPick2))
            {
                if(!colorPickInputPressed)
                {
                    colorPickInputPressed = true;

                    if(selectedSlimBlock != null || selectedPlayer != null)
                    {
                        if(colorPickMode)
                            SendToServer_ColorPickMode(false);

                        var targetColor = (selectedSlimBlock != null ? selectedSlimBlock.ColorMaskHSV : selectedPlayerColorMask);
                        var targetSkin = (selectedSlimBlock != null ? selectedSlimBlock.SkinSubtypeId : selectedPlayerBlockSkin);
                        var blockMaterial = new BlockMaterial(targetColor, targetSkin);

                        PickColorAndSkinFromBlock((byte)localColorData.SelectedSlot, blockMaterial);
                    }
                    else
                    {
                        ShowNotification(0, "First aim at a block or player.", MyFontEnum.Red, 2000);
                    }
                }
            }
            else
            {
                colorPickInputPressed = false;
            }
        }

        private void HandleInputs_ReplaceMode()
        {
            if(InputHandler.GetPressedOr(settings.replaceColorMode1, settings.replaceColorMode2))
            {
                if(!replaceAllModeInputPressed)
                {
                    replaceAllModeInputPressed = true;

                    if(ReplaceColorAccess)
                    {
                        replaceAllMode = !replaceAllMode;
                        ShowNotification(0, "Replace color mode " + (replaceAllMode ? "enabled." : "turned off."), MyFontEnum.White, 2000);
                    }
                    else
                    {
                        ShowNotification(0, "Replace color mode is only available in creative game mode or with SM creative tools on.", MyFontEnum.Red, 3000);
                    }
                }
            }
            else if(replaceAllModeInputPressed)
            {
                replaceAllModeInputPressed = false;
            }
        }

        private void HandleInputs_Trigger(bool trigger)
        {
            if(trigger && (IgnoreAmmoConsumption || localHeldTool.Ammo > 0))
            {
                if(!triggerInputPressed)
                {
                    triggerInputPressed = true;
                    SendToServer_PaintGunFiring(localHeldTool, true);
                }
            }
            else if(triggerInputPressed)
            {
                triggerInputPressed = false;
                SendToServer_PaintGunFiring(localHeldTool, false);
            }
        }

        private void HandleInputs_Painting(bool trigger)
        {
            if(tick % PAINT_SKIP_TICKS != 0)
                return;

            if(replaceAllMode && !ReplaceColorAccess) // if access no longer allows it, disable the replace mode
            {
                replaceAllMode = false;
                ShowNotification(0, "Replace color mode turned off.", MyFontEnum.White, 2000);
            }

            var painted = HandleTool(trigger);

            if(painted && !colorPickMode && !IgnoreAmmoConsumption) // expend the ammo manually when painting
            {
                var character = MyAPIGateway.Session.Player.Character;

                if(MyAPIGateway.Multiplayer.IsServer)
                {
                    var inv = character.GetInventory();

                    if(inv != null)
                        inv.RemoveItemsOfType(1, PAINT_MAG, false); // inventory actions get synchronized to clients automatically if called server-side
                }
                else
                {
                    SendToServer_RemoveAmmo(character.EntityId);
                }
            }

            GenerateAimInfo();
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if(!init)
                    return;

                unchecked { ++tick; }

                if(ownershipTestPlayer != null && ownershipTestPlayer.NeedsUpdate)
                {
                    ownershipTestPlayer.Update(tick);
                }

                if(ownershipTestServer != null && ownershipTestServer.NeedsUpdate)
                {
                    ownershipTestServer.Update(tick);
                }

                uiEdit?.Update();
                InitializePlayer();
                DetectHudToggle();
                ReadPlayerColors();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void InitializePlayer()
        {
            if(!isDS && !playerObjectFound)
            {
                var colors = MyAPIGateway.Session.Player?.DefaultBuildColorSlots;

                if(colors != null && colors.HasValue)
                {
                    DEFAULT_COLOR = colors.Value.ItemAt(0);
                    playerObjectFound = true;

                    ownershipTestPlayer?.TestForLocalPlayer();
                }
            }
        }

        private void DetectHudToggle()
        {
            // HUD toggle monitor; needs to be in BeforeSimulation because MinimalHud has the previous value if used in HandleInput()
            if(!isDS && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.TOGGLE_HUD))
                gameHUD = !MyAPIGateway.Session.Config.MinimalHud;
        }

        private void ReadPlayerColors()
        {
            if(!MyAPIGateway.Multiplayer.IsServer || tick % 10 != 0)
                return;

            foreach(var kv in MyCubeBuilder.AllPlayersColors)
            {
                var steamId = GetSteamId(kv.Key.ToString());

                if(playerColorData.ContainsKey(steamId))
                {
                    var cd = playerColorData[steamId];

                    // send only changed colors
                    if(CheckColorList(steamId, cd.Colors, kv.Value))
                    {
                        cd.Colors.Clear();
                        cd.Colors.AddList(kv.Value); // add them separately to avoid the reference being automatically updated and not detecting changes
                    }
                }
                else
                {
                    // new list to not use the same reference as it needs to detect changes
                    playerColorData.Add(steamId, new PlayerColorData(steamId, new List<Vector3>(kv.Value)));

                    // send all colors if player is online
                    if(IsPlayerOnline(steamId))
                    {
                        var myId = MyAPIGateway.Multiplayer.MyId;

                        players.Clear();
                        MyAPIGateway.Players.GetPlayers(players);

                        foreach(var p in players)
                        {
                            if(myId == p.SteamUserId) // don't re-send to yourself
                                continue;

                            SendToPlayer_SendColorList(p.SteamUserId, steamId, 0, kv.Value);
                        }

                        players.Clear();
                    }
                }
            }
        }

        public override void Draw()
        {
            try
            {
                if(isDS || MyAPIGateway.Session == null)
                    return;

                viewProjInvCompute = true;

                DrawToolParticles();
                DrawCharacterSelection();
                DrawHUDPalette();

                bool controllingLocalChar = (MyAPIGateway.Session.ControlledObject == MyAPIGateway.Session.Player?.Character);

                if(controllingLocalChar && localHeldTool != null)
                {
                    DrawSymmetry();
                    DrawBlockSelection();
                }

                //DebugDrawCharacterSphere();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void DebugDrawCharacterSphere()
        {
            var character = MyAPIGateway.Session.Player.Character;
            var sphere = GetCharacterSelectionSphere(character);
            var matrix = MatrixD.CreateTranslation(sphere.Center);
            var color = Color.Lime;

            MySimpleObjectDraw.DrawTransparentSphere(ref matrix, (float)sphere.Radius, ref color, MySimpleObjectRasterizer.Wireframe, 12, MATERIAL_PALETTE_COLOR, MATERIAL_PALETTE_COLOR, 0.001f, blendType: HELPERS_BLEND_TYPE);
        }

        private void DrawToolParticles()
        {
            int toolDrawCount = ToolDraw.Count;

            if(toolDrawCount > 0)
            {
                for(int i = 0; i < toolDrawCount; ++i)
                {
                    ToolDraw[i].Draw();
                }
            }
        }

        private void DrawCharacterSelection()
        {
            if(selectedPlayer == null)
                return;

            var selectedCharacter = selectedPlayer.Character;

            if(selectedCharacter == null || selectedCharacter.MarkedForClose || selectedCharacter.Closed)
            {
                selectedPlayer = null;
            }
            else if(selectedCharacter.Visible)
            {
                DrawCharacterSelection(selectedCharacter);
            }
        }

        private readonly Color PALETTE_COLOR_BG = new Color(41, 54, 62);

        private void DrawHUDPalette()
        {
            bool hideTextAPI = true;

            if(localHeldTool != null && localColorData != null && !MyAPIGateway.Gui.IsCursorVisible && !(settings.hidePaletteWithHUD && !gameHUD))
            {
                hideTextAPI = false;

                var cam = MyAPIGateway.Session.Camera;
                var camMatrix = cam.WorldMatrix;
                var scaleFOV = (float)Math.Tan(cam.FovWithZoom * 0.5);
                scaleFOV *= settings.paletteScale;

                var character = MyAPIGateway.Session.Player.Character;

                if(IsAimingDownSights(character))
                {
                    MyTransparentGeometry.AddPointBillboard(MATERIAL_WHITEDOT, Color.Lime, camMatrix.Translation + camMatrix.Forward * PAINT_AIM_START_OFFSET, 0.005f, 0, blendType: HELPERS_BLEND_TYPE);
                }

                var worldPos = HUDtoWorld(new Vector2((float)settings.paletteScreenPos.X, (float)settings.paletteScreenPos.Y));
                var bgAlpha = (settings.paletteBackgroundOpacity < 0 ? gameHUDBkOpacity : settings.paletteBackgroundOpacity);

                #region Color selector
                if(localColorData.ApplyColor)
                {
                    float squareWidth = 0.0014f * scaleFOV;
                    float squareHeight = 0.0010f * scaleFOV;
                    float selectedWidth = (squareWidth + (squareWidth / 3f));
                    float selectedHeight = (squareHeight + (squareHeight / 3f));
                    double spacingAdd = 0.0006 * scaleFOV;
                    double spacingWidth = (squareWidth * 2) + spacingAdd;
                    double spacingHeight = (squareHeight * 2) + spacingAdd;
                    const int MIDDLE_INDEX = 7;
                    const float BG_WIDTH_MUL = 3.85f;
                    const float BG_HEIGHT_MUL = 1.3f;

                    MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, PALETTE_COLOR_BG * bgAlpha, worldPos, camMatrix.Left, camMatrix.Up, (float)(spacingWidth * BG_WIDTH_MUL), (float)(spacingHeight * BG_HEIGHT_MUL), Vector2.Zero, UI_BG_BLENDTYPE);

                    var pos = worldPos + camMatrix.Left * (spacingWidth * (MIDDLE_INDEX / 2)) + camMatrix.Up * (spacingHeight / 2);

                    for(int i = 0; i < localColorData.Colors.Count; i++)
                    {
                        var keenHSV = localColorData.Colors[i];
                        var rgb = ColorMaskToRGB(keenHSV);

                        if(i == MIDDLE_INDEX)
                            pos += camMatrix.Left * (spacingWidth * MIDDLE_INDEX) + camMatrix.Down * spacingHeight;

                        if(i == localColorData.SelectedSlot)
                            MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, Color.White, pos, camMatrix.Left, camMatrix.Up, selectedWidth, selectedHeight, Vector2.Zero, UI_FG_BLENDTYPE);

                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, rgb, pos, camMatrix.Left, camMatrix.Up, squareWidth, squareHeight, Vector2.Zero, UI_FG_BLENDTYPE);

                        pos += camMatrix.Right * spacingWidth;
                    }
                }
                #endregion

                #region Skin selector
                if(localColorData.ApplySkin)
                {
                    int ownedSkins = 0;

                    for(int i = 0; i < BlockSkins.Count; ++i)
                    {
                        var skin = BlockSkins[i];

                        if(skin.LocallyOwned)
                            ownedSkins++;
                    }

                    if(ownedSkins > 0)
                    {
                        float iconSize = 0.0030f * scaleFOV;
                        float selectedIconSize = 0.0034f * scaleFOV;
                        var selectedSkinIndex = localColorData.SelectedSkinIndex;
                        double iconSpacingAdd = 0.0012 * scaleFOV;
                        double iconSpacingWidth = (iconSize * 2) + iconSpacingAdd;
                        float iconBgSpacingAddWidth = 0.0006f * scaleFOV;
                        float iconBgSpacingAddHeight = 0.0008f * scaleFOV;
                        double halfOwnedSkins = ownedSkins * 0.5;

                        var pos = worldPos;

                        if(localColorData.ApplyColor)
                            pos += camMatrix.Up * (0.0075f * scaleFOV);

                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, PALETTE_COLOR_BG * bgAlpha, pos, camMatrix.Left, camMatrix.Up, (float)(iconSpacingWidth * halfOwnedSkins) + iconBgSpacingAddWidth, iconSize + iconBgSpacingAddHeight, Vector2.Zero, UI_BG_BLENDTYPE);

                        pos += camMatrix.Left * ((iconSpacingWidth * halfOwnedSkins) - (iconSpacingWidth * 0.5));

                        for(int i = 0; i < BlockSkins.Count; ++i)
                        {
                            var skin = BlockSkins[i];

                            if(!skin.LocallyOwned)
                                continue;

                            if(selectedSkinIndex == i)
                            {
                                MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, Color.White, pos, camMatrix.Left, camMatrix.Up, selectedIconSize, selectedIconSize, Vector2.Zero, UI_FG_BLENDTYPE);
                            }

                            MyTransparentGeometry.AddBillboardOriented(skin.Icon, Color.White, pos, camMatrix.Left, camMatrix.Up, iconSize, iconSize, Vector2.Zero, UI_FG_BLENDTYPE);

                            pos += camMatrix.Right * iconSpacingWidth;
                        }
                    }
                }
                #endregion

                // FIXME: needs more color testing - vanilla title bg doesn't match my bg even though they're copied colors...
                //{
                //    worldPos = HUDtoWorld(new Vector2((float)settings.aimInfoScreenPos.X, (float)settings.aimInfoScreenPos.Y));
                //
                //    worldPos += camMatrix.Down * (0.02f * scaleFOV);
                //
                //    var posA = worldPos + camMatrix.Left * (0.05f * scaleFOV);
                //    var posB = worldPos + camMatrix.Up * (0.2f * scaleFOV);
                //    var posC = worldPos;
                //    var dir = camMatrix.Forward;
                //
                //    MyTransparentGeometry.AddTriangleBillboard(posA, posB, posC, dir, dir, dir,
                //        Vector2.Zero, Vector2.Zero, Vector2.Zero, MATERIAL_PALETTE_COLOR, 0, posA, UI_TITLE_BG_COLOR.ToVector4().ToLinearRGB(), UI_BG_BLENDTYPE);
                //}
            }

            if(hideTextAPI)
                SetUIVisibility(false);
        }

        #region Aimed object UI
        internal Vector2D uiPosition => settings.aimInfoScreenPos;
        internal Vector2D uiTextBgPosition = new Vector2D(0, -0.071);
        internal Vector2D uiProgressBarPosition = new Vector2D(0.005, -0.079);

        internal const float UI_BOX_WIDTH = 0.337f * (16f / 9f);

        internal const float UI_TITLE_SCALE = 1f;
        internal const float UI_TITLE_BG_HEIGHT = 0.071f;
        internal readonly Color UI_TITLE_BG_COLOR = new Vector4(53f / 255f, 4f / 15f, 76f / 255f, 0.9f); // from MyGuiScreenHudSpace.RecreateControls() @ MyGuiControlBlockInfo

        internal const float UI_TEXT_SCALE = 0.8f;
        internal const float UI_TEXT_BG_HEIGHT = 0.4f;
        internal readonly Color UI_TEXT_BG_COLOR = new Vector4(13f / 85f, 52f / 255f, 59f / 255f, 0.9f);

        internal const BlendTypeEnum UI_FG_BLENDTYPE = BlendTypeEnum.PostPP;
        internal const BlendTypeEnum UI_BG_BLENDTYPE = BlendTypeEnum.PostPP;

        internal const float UI_COLORBOX_WIDTH = 0.07f;
        internal const float UI_COLORBOX_HEIGHT = 0.07f;

        internal readonly Color UI_PROGRESSBAR_COLOR = new Vector4(0.478431374f, 0.549019635f, 0.6039216f, 1f);
        internal readonly Color UI_PROGRESSBAR_BG_COLOR = new Vector4(0.266666681f, 0.3019608f, 0.3372549f, 0.9f);
        internal const float UI_PROGRESSBAR_WIDTH = 0.02f * (16f / 9f);
        internal const float UI_PROGRESSBAR_HEIGHT = 0.384f;

        internal HudAPIv2.HUDMessage uiTitle;
        internal HudAPIv2.BillBoardHUDMessage uiTitleBg;
        internal HudAPIv2.HUDMessage uiText;
        internal HudAPIv2.BillBoardHUDMessage uiTextBg;
        internal HudAPIv2.BillBoardHUDMessage uiTargetColor;
        internal HudAPIv2.BillBoardHUDMessage uiPaintColor;
        internal HudAPIv2.BillBoardHUDMessage uiProgressBar;
        internal HudAPIv2.BillBoardHUDMessage uiProgressBarBg;
        internal readonly HudAPIv2.MessageBase[] ui = new HudAPIv2.MessageBase[8];

        private void SetUIVisibility(bool set)
        {
            if(uiTitle == null)
                return;

            if(set == textAPIvisible)
                return;

            textAPIvisible = set;

            for(int i = 0; i < ui.Length; ++i)
            {
                ui[i].Visible = set;
            }
        }

        private void SetUIOption(HudAPIv2.Options flag, bool set)
        {
            if(uiTitle == null)
                return;

            for(int i = 0; i < ui.Length; ++i)
            {
                var msgBase = ui[i];

                var hudMessage = msgBase as HudAPIv2.HUDMessage;
                if(hudMessage != null)
                {
                    if(set)
                        hudMessage.Options |= flag;
                    else
                        hudMessage.Options &= ~flag;
                    continue;
                }

                var hudBillboard = msgBase as HudAPIv2.BillBoardHUDMessage;
                if(hudBillboard != null)
                {
                    if(set)
                        hudBillboard.Options |= flag;
                    else
                        hudBillboard.Options &= ~flag;
                    continue;
                }
            }
        }

        internal void UpdateUISettings()
        {
            if(uiTitle == null)
                return;

            SetUIOption(HudAPIv2.Options.HideHud, settings.hidePaletteWithHUD);

            var alpha = (settings.aimInfoBackgroundOpacity < 0 ? gameHUDBkOpacity : settings.aimInfoBackgroundOpacity);

            uiTitleBg.BillBoardColor = UI_TITLE_BG_COLOR * alpha;
            uiTextBg.BillBoardColor = UI_TEXT_BG_COLOR * alpha;
            uiProgressBarBg.BillBoardColor = UI_PROGRESSBAR_BG_COLOR * MathHelper.Clamp(alpha * 2, 0.1f, 0.9f);

            float aspectRatioMod = (float)(1d / aspectRatio);
            float boxBgWidth = UI_BOX_WIDTH * aspectRatioMod;
            float colorWidth = UI_COLORBOX_WIDTH * aspectRatioMod;
            float progressBarWidth = UI_PROGRESSBAR_WIDTH * aspectRatioMod;

            for(int i = 0; i < ui.Length; ++i)
            {
                var msgBase = ui[i];

                if(msgBase is HudAPIv2.HUDMessage)
                {
                    var obj = (HudAPIv2.HUDMessage)msgBase;

                    obj.Origin = uiPosition;
                }
                else if(msgBase is HudAPIv2.BillBoardHUDMessage)
                {
                    var obj = (HudAPIv2.BillBoardHUDMessage)msgBase;

                    obj.Origin = uiPosition;
                }
            }

            //float scale = settings.aimInfoScale;
            const float scale = 1f; // FIXME: make it scaleable?

            uiTitleBg.Width = boxBgWidth * scale;
            uiTextBg.Width = boxBgWidth * scale;
            uiPaintColor.Width = colorWidth * scale;
            uiTargetColor.Width = colorWidth * scale;
            uiProgressBar.Width = progressBarWidth * scale;
            uiProgressBarBg.Width = progressBarWidth * scale;

            uiTitle.Offset = new Vector2D(0.012, -0.018) * scale;
            uiTitleBg.Offset = new Vector2D(uiTitleBg.Width * 0.5, uiTitleBg.Height * -0.5);

            uiText.Offset = new Vector2D(0.07 * aspectRatioMod, -0.09) * scale;
            uiTextBg.Offset = new Vector2D(uiTextBg.Width * 0.5, uiTextBg.Height * -0.5) + (uiTextBgPosition * scale);

            uiTargetColor.Offset = new Vector2D(0.09 * aspectRatioMod, -0.214) * scale;
            uiPaintColor.Offset = new Vector2D(0.09 * aspectRatioMod, -0.335) * scale;

            uiProgressBar.Offset = new Vector2D(uiProgressBar.Width * 0.5, uiProgressBar.Height * -0.5) + (uiProgressBarPosition * scale);
            uiProgressBarBg.Offset = new Vector2D(uiProgressBarBg.Width * 0.5, uiProgressBarBg.Height * -0.5) + (uiProgressBarPosition * scale);

            uiTitle.Scale = UI_TITLE_SCALE * scale;
            uiTitleBg.Scale = scale;
            uiText.Scale = UI_TEXT_SCALE * scale;
            uiTextBg.Scale = scale;
            uiTargetColor.Scale = scale;
            uiPaintColor.Scale = scale;
            uiProgressBar.Scale = scale;
            uiProgressBarBg.Scale = scale;
        }

        public void GenerateAimInfo()
        {
            if(!TextAPIReady)
                return;

            if(uiTitle == null)
            {
                // NOTE: this creation order is needed to have background elements stay in background when everything uses PostPP (or SDR)
                ui[3] = uiTextBg = new HudAPIv2.BillBoardHUDMessage(MATERIAL_PALETTE_BACKGROUND, uiPosition, UI_TEXT_BG_COLOR, Width: UI_BOX_WIDTH, Height: UI_TEXT_BG_HEIGHT, Blend: UI_BG_BLENDTYPE);
                ui[1] = uiTitleBg = new HudAPIv2.BillBoardHUDMessage(MATERIAL_PALETTE_BACKGROUND, uiPosition, UI_TITLE_BG_COLOR, Width: UI_BOX_WIDTH, Height: UI_TITLE_BG_HEIGHT, Blend: UI_BG_BLENDTYPE);

                ui[0] = uiTitle = new HudAPIv2.HUDMessage(new StringBuilder(), uiPosition, Scale: UI_TITLE_SCALE, Blend: UI_FG_BLENDTYPE);
                ui[2] = uiText = new HudAPIv2.HUDMessage(new StringBuilder(), uiPosition, Scale: UI_TEXT_SCALE, Blend: UI_FG_BLENDTYPE);

                ui[4] = uiTargetColor = new HudAPIv2.BillBoardHUDMessage(MATERIAL_ICON_GENERIC_BLOCK, uiPosition, Color.White, Width: UI_COLORBOX_WIDTH, Height: UI_COLORBOX_HEIGHT, Blend: UI_FG_BLENDTYPE);
                ui[5] = uiPaintColor = new HudAPIv2.BillBoardHUDMessage(MATERIAL_ICON_PAINT_AMMO, uiPosition, Color.White, Width: UI_COLORBOX_WIDTH, Height: UI_COLORBOX_HEIGHT, Blend: UI_FG_BLENDTYPE);

                ui[7] = uiProgressBarBg = new HudAPIv2.BillBoardHUDMessage(MATERIAL_PALETTE_BACKGROUND, uiPosition, UI_PROGRESSBAR_BG_COLOR, Width: UI_PROGRESSBAR_WIDTH, Height: UI_PROGRESSBAR_HEIGHT, Blend: UI_BG_BLENDTYPE);
                ui[6] = uiProgressBar = new HudAPIv2.BillBoardHUDMessage(MATERIAL_PALETTE_BACKGROUND, uiPosition, UI_PROGRESSBAR_COLOR, Width: UI_PROGRESSBAR_WIDTH, Height: UI_PROGRESSBAR_HEIGHT, Blend: UI_FG_BLENDTYPE);

                UpdateUISettings();
            }

            bool targetCharacter = (colorPickMode && selectedPlayer != null);
            bool visible = (!MyAPIGateway.Gui.IsCursorVisible && localColorData != null && (targetCharacter || selectedSlimBlock != null));

            for(int i = 0; i < ui.Length; ++i)
            {
                ui[i].Visible = visible;
            }

            textAPIvisible = visible;

            if(!visible)
                return;

            BlockMaterial targetMaterial;
            var paint = GetLocalPaintMaterial();
            int ammo = (localHeldTool != null ? localHeldTool.Ammo : 0);

            var title = uiTitle.Message.Clear().Append("<color=220,244,252>");

            if(targetCharacter)
            {
                uiTargetColor.Material = MATERIAL_ICON_GENERIC_CHARACTER;
                targetMaterial = new BlockMaterial(selectedPlayerColorMask, selectedPlayerBlockSkin);

                title.Append(selectedPlayer.DisplayName);
            }
            else
            {
                uiTargetColor.Material = MATERIAL_ICON_GENERIC_BLOCK;
                targetMaterial = new BlockMaterial(selectedSlimBlock);

                var selectedDef = (MyCubeBlockDefinition)selectedSlimBlock.BlockDefinition;
                title.Append(selectedDef.DisplayNameText);
            }

            var targetSkin = GetSkinInfo(targetMaterial.Skin);

            uiTargetColor.BillBoardColor = ColorMaskToRGB(targetMaterial.ColorMask);
            uiPaintColor.BillBoardColor = (paint.ColorMask.HasValue ? ColorMaskToRGB(paint.ColorMask.Value) : Color.Gray);

            float progress = 0f;

            if(paint.ColorMask.HasValue)
                progress = ColorScalar(targetMaterial.ColorMask, paint.ColorMask.Value);
            else if(paint.Skin.HasValue)
                progress = (targetMaterial.Skin == paint.Skin.Value ? 1f : 0.25f);

            var height = UI_PROGRESSBAR_HEIGHT * progress;

            uiProgressBar.Height = height;
            uiProgressBar.Offset = new Vector2D(uiProgressBar.Width * 0.5, -UI_PROGRESSBAR_HEIGHT + uiProgressBar.Height * 0.5) + uiProgressBarPosition;

            var text = uiText.Message;
            text.Clear().Append(blockInfoStatus[0]);
            text.Append('\n');
            text.Append('\n');

            {
                text.Append("<color=220,244,252>");

                if(colorPickMode && selectedPlayer != null)
                    text.Append("Engineer's selected paint:");
                else
                    text.Append("Block's material:");

                text.Append('\n');
                text.Append("<color=white>        ").Append(ColorMaskToString(targetMaterial.ColorMask)).Append("\n");

                text.Append("<color=white>        Skin: ");
                if(!targetSkin.LocallyOwned)
                    text.Append("<color=red>");
                text.Append(targetSkin.Name).Append('\n');
            }

            {
                text.Append('\n');
                text.Append("<color=220,244,252>");
                if(colorPickMode)
                {
                    text.Append("Replace slot: ").Append(localColorData.SelectedSlot + 1);
                }
                else
                {
                    text.Append("Paint: ");

                    if(IgnoreAmmoConsumption)
                        text.Append("Inf.");
                    else
                        text.Append(ammo);
                }
                text.Append('\n');
            }

            {
                text.Append("        ");
                if(paint.ColorMask.HasValue)
                    text.Append("<color=white>").Append(ColorMaskToString(paint.ColorMask.Value)).Append('\n');
                else
                    text.Append('\n');
            }

            {
                text.Append("        ");
                if(paint.Skin.HasValue)
                {
                    var skin = GetSkinInfo(paint.Skin.Value);
                    text.Append("<color=white>Skin: ").Append(skin.Name).Append('\n');
                }
                else
                {
                    text.Append('\n');
                }
            }

            text.Append("<color=white>");

            if(blockInfoStatus[1] != null)
                text.Append('\n').Append(blockInfoStatus[1]);

            if(blockInfoStatus[2] != null)
                text.Append('\n').Append(blockInfoStatus[2]);
        }
        #endregion

        private void DrawSymmetry()
        {
            if(symmetryInputAvailable && MyAPIGateway.CubeBuilder.UseSymmetry && selectedGrid != null && (selectedGrid.XSymmetryPlane.HasValue || selectedGrid.YSymmetryPlane.HasValue || selectedGrid.ZSymmetryPlane.HasValue))
            {
                var matrix = selectedGrid.WorldMatrix;
                var quad = new MyQuadD();
                Vector3D gridSize = (Vector3I.One + (selectedGrid.Max - selectedGrid.Min)) * selectedGrid.GridSizeHalf;
                const float alpha = 0.4f;

                if(selectedGrid.XSymmetryPlane.HasValue)
                {
                    var center = matrix.Translation + matrix.Right * ((selectedGrid.XSymmetryPlane.Value.X * selectedGrid.GridSize) - (selectedGrid.XSymmetryOdd ? selectedGrid.GridSizeHalf : 0));

                    var minY = matrix.Up * ((selectedGrid.Min.Y - 1.5f) * selectedGrid.GridSize);
                    var maxY = matrix.Up * ((selectedGrid.Max.Y + 1.5f) * selectedGrid.GridSize);
                    var minZ = matrix.Backward * ((selectedGrid.Min.Z - 1.5f) * selectedGrid.GridSize);
                    var maxZ = matrix.Backward * ((selectedGrid.Max.Z + 1.5f) * selectedGrid.GridSize);

                    quad.Point0 = center + maxY + maxZ;
                    quad.Point1 = center + maxY + minZ;
                    quad.Point2 = center + minY + minZ;
                    quad.Point3 = center + minY + maxZ;

                    MyTransparentGeometry.AddQuad(MATERIAL_VANILLA_SQUARE, ref quad, Color.Red * alpha, ref center, blendType: HELPERS_BLEND_TYPE);
                }

                if(selectedGrid.YSymmetryPlane.HasValue)
                {
                    var center = matrix.Translation + matrix.Up * ((selectedGrid.YSymmetryPlane.Value.Y * selectedGrid.GridSize) - (selectedGrid.YSymmetryOdd ? selectedGrid.GridSizeHalf : 0));

                    var minZ = matrix.Backward * ((selectedGrid.Min.Z - 1.5f) * selectedGrid.GridSize);
                    var maxZ = matrix.Backward * ((selectedGrid.Max.Z + 1.5f) * selectedGrid.GridSize);
                    var minX = matrix.Right * ((selectedGrid.Min.X - 1.5f) * selectedGrid.GridSize);
                    var maxX = matrix.Right * ((selectedGrid.Max.X + 1.5f) * selectedGrid.GridSize);

                    quad.Point0 = center + maxZ + maxX;
                    quad.Point1 = center + maxZ + minX;
                    quad.Point2 = center + minZ + minX;
                    quad.Point3 = center + minZ + maxX;

                    MyTransparentGeometry.AddQuad(MATERIAL_VANILLA_SQUARE, ref quad, Color.Green * alpha, ref center, blendType: HELPERS_BLEND_TYPE);
                }

                if(selectedGrid.ZSymmetryPlane.HasValue)
                {
                    var center = matrix.Translation + matrix.Backward * ((selectedGrid.ZSymmetryPlane.Value.Z * selectedGrid.GridSize) + (selectedGrid.ZSymmetryOdd ? selectedGrid.GridSizeHalf : 0));

                    var minY = matrix.Up * ((selectedGrid.Min.Y - 1.5f) * selectedGrid.GridSize);
                    var maxY = matrix.Up * ((selectedGrid.Max.Y + 1.5f) * selectedGrid.GridSize);
                    var minX = matrix.Right * ((selectedGrid.Min.X - 1.5f) * selectedGrid.GridSize);
                    var maxX = matrix.Right * ((selectedGrid.Max.X + 1.5f) * selectedGrid.GridSize);

                    quad.Point0 = center + maxY + maxX;
                    quad.Point1 = center + maxY + minX;
                    quad.Point2 = center + minY + minX;
                    quad.Point3 = center + minY + maxX;

                    MyTransparentGeometry.AddQuad(MATERIAL_VANILLA_SQUARE, ref quad, Color.Blue * alpha, ref center, blendType: HELPERS_BLEND_TYPE);
                }
            }
        }

        private void DrawBlockSelection()
        {
            if(selectedSlimBlock == null)
                return;

            if(selectedSlimBlock.IsDestroyed || selectedSlimBlock.IsFullyDismounted)
            {
                selectedSlimBlock = null;
            }
            else
            {
                DrawBlockSelection(selectedSlimBlock, !selectedInvalid);

                // symmetry highlight
                if(SymmetryAccess && MyCubeBuilder.Static.UseSymmetry && (selectedGrid.XSymmetryPlane.HasValue || selectedGrid.YSymmetryPlane.HasValue || selectedGrid.ZSymmetryPlane.HasValue))
                {
                    var mirrorX = MirrorHighlight(selectedGrid, 0, selectedSlimBlock.Position); // X
                    var mirrorY = MirrorHighlight(selectedGrid, 1, selectedSlimBlock.Position); // Y
                    var mirrorZ = MirrorHighlight(selectedGrid, 2, selectedSlimBlock.Position); // Z
                    Vector3I? mirrorYZ = null;

                    if(mirrorX.HasValue && selectedGrid.YSymmetryPlane.HasValue) // XY
                        MirrorHighlight(selectedGrid, 1, mirrorX.Value);

                    if(mirrorX.HasValue && selectedGrid.ZSymmetryPlane.HasValue) // XZ
                        MirrorHighlight(selectedGrid, 2, mirrorX.Value);

                    if(mirrorY.HasValue && selectedGrid.ZSymmetryPlane.HasValue) // YZ
                        mirrorYZ = MirrorHighlight(selectedGrid, 2, mirrorY.Value);

                    if(selectedGrid.XSymmetryPlane.HasValue && mirrorYZ.HasValue) // XYZ
                        MirrorHighlight(selectedGrid, 0, mirrorYZ.Value);
                }
            }
        }

        #region Tool update & targeting
        /// <summary>
        /// Returns True if tool has painted.
        /// </summary>
        public bool HandleTool(bool trigger)
        {
            try
            {
                selectedGrid = null;
                selectedPlayer = null;
                selectedSlimBlock = null;
                selectedInvalid = false;
                symmetryInputAvailable = false;

                var character = MyAPIGateway.Session.Player.Character;
                IMyPlayer targetPlayer;
                IMyCubeGrid targetGrid;
                IMySlimBlock targetBlock;
                GetTarget(character, out targetGrid, out targetBlock, out targetPlayer);

                // TODO testing aim to paint subpart
#if false
                {
                    if(MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.SECONDARY_TOOL_ACTION))
                    {
                        MyEntitySubpart targetSubpart = null;


                        const float MAX_DISTANCE = 5;
                        var view = character.GetHeadMatrix(false, true);
                        var rayDir = view.Forward;
                        var rayFrom = view.Translation;
                        var rayTo = view.Translation + rayDir * MAX_DISTANCE;

                        entitiesInRange.Clear();

                        LineD lineD = new LineD(rayFrom, rayTo);
                        using(raycastResults.GetClearToken<MyLineSegmentOverlapResult<MyEntity>>())
                        {
                            MyGamePruningStructure.GetAllEntitiesInRay(ref lineD, raycastResults, MyEntityQueryType.Both);

                            foreach(MyLineSegmentOverlapResult<MyEntity> res in raycastResults)
                            {
                                if(res.Element == null)
                                    continue;

                                var parent = res.Element.GetTopMostParent(null);
                                var block = res.Element as IMyCubeBlock;

                                if(block != null)
                                {
                                    MatrixD wmInv = block.PositionComp.WorldMatrixNormalizedInv;
                                    Vector3D localFrom = Vector3D.Transform(rayFrom, ref wmInv);
                                    Vector3D localTo = Vector3D.Transform(rayTo, ref wmInv);

                                    Ray ray = new Ray(localFrom, Vector3.Normalize(localTo - localFrom));
                                    float? num3 = ray.Intersects(block.PositionComp.LocalAABB);
                                    float? num4 = num3;
                                    num3 = (num4.HasValue ? new float?(num4.GetValueOrDefault() + 0.01f) : null);

                                    if(num3.HasValue)
                                    {
                                        if(num3.GetValueOrDefault() <= MAX_DISTANCE && num3.HasValue)
                                        {
                                            var detectionPoint = rayFrom + rayDir * num3.Value;

                                            DetectionInfo info;
                                            if(entitiesInRange.TryGetValue(parent.EntityId, out info))
                                            {
                                                if(Vector3.DistanceSquared(info.DetectionPoint, rayFrom) > Vector3.DistanceSquared(detectionPoint, rayFrom))
                                                {
                                                    entitiesInRange[parent.EntityId] = new DetectionInfo(parent, detectionPoint);
                                                }
                                            }
                                            else
                                            {
                                                entitiesInRange[parent.EntityId] = new DetectionInfo(parent, detectionPoint);
                                            }
                                        }
                                    }
                                }
                            }

                            raycastResults.Clear();
                        }


                        if(entitiesInRange.Count > 0)
                        {
                            float prevDistSq = 3.40282347E+38f;
                            IMyEntity lastDetectedEntity = null;
                            Vector3D hitPosition = Vector3D.Zero;

                            foreach(var info in entitiesInRange.Values)
                            {
                                float distSq = (float)Vector3D.DistanceSquared(info.DetectionPoint, rayFrom);

                                if(info.Entity.Physics != null && info.Entity.Physics.Enabled && distSq < prevDistSq)
                                {
                                    lastDetectedEntity = info.Entity;
                                    hitPosition = info.DetectionPoint;
                                    prevDistSq = distSq;
                                }
                            }

                            entitiesInRange.Clear();

                            targetGrid = lastDetectedEntity as MyCubeGrid;

                            if(targetGrid != null)
                            {
                                MatrixD gridWMInv = targetGrid.PositionComp.WorldMatrixNormalizedInv;
                                Vector3D value = Vector3D.Transform(hitPosition, gridWMInv);
                                Vector3I pos;
                                targetGrid.FixTargetCube(out pos, value / (double)targetGrid.GridSize);
                                targetBlock = targetGrid.GetCubeBlock(pos);

                                if(targetBlock?.FatBlock != null)
                                {
                                    var subparts = ((MyCubeBlock)targetBlock.FatBlock).Subparts;

                                    foreach(var subpart in subparts.Values)
                                    {
                                        MatrixD wmInv = subpart.PositionComp.WorldMatrixNormalizedInv;
                                        Vector3D localFrom = Vector3D.Transform(rayFrom, ref wmInv);
                                        Vector3D localTo = Vector3D.Transform(rayTo, ref wmInv);

                                        Ray ray = new Ray(localFrom, Vector3.Normalize(localTo - localFrom));
                                        float? num3 = ray.Intersects(subpart.PositionComp.LocalAABB);
                                        float? num4 = num3;
                                        num3 = (num4.HasValue ? new float?(num4.GetValueOrDefault() + 0.01f) : null);

                                        if(num3.HasValue)
                                        {
                                            if(num3.GetValueOrDefault() <= MAX_DISTANCE && num3.HasValue)
                                            {
                                                var detectionPoint = rayFrom + rayDir * num3.Value;

                                                DetectionInfo info;
                                                if(entitiesInRange.TryGetValue(subpart.EntityId, out info))
                                                {
                                                    if(Vector3.DistanceSquared(info.DetectionPoint, rayFrom) > Vector3.DistanceSquared(detectionPoint, rayFrom))
                                                    {
                                                        entitiesInRange[subpart.EntityId] = new DetectionInfo(subpart, detectionPoint);
                                                    }
                                                }
                                                else
                                                {
                                                    entitiesInRange[subpart.EntityId] = new DetectionInfo(subpart, detectionPoint);
                                                }
                                            }
                                        }
                                    }

                                    if(entitiesInRange.Count > 0)
                                    {
                                        prevDistSq = 3.40282347E+38f;
                                        lastDetectedEntity = null;
                                        hitPosition = Vector3D.Zero;

                                        foreach(var info in entitiesInRange.Values)
                                        {
                                            float distSq = (float)Vector3D.DistanceSquared(info.DetectionPoint, rayFrom);

                                            if(info.Entity.Physics != null && info.Entity.Physics.Enabled && distSq < prevDistSq)
                                            {
                                                lastDetectedEntity = info.Entity;
                                                hitPosition = info.DetectionPoint;
                                                prevDistSq = distSq;
                                            }
                                        }

                                        entitiesInRange.Clear();

                                        targetSubpart = lastDetectedEntity as MyEntitySubpart;
                                    }
                                }
                            }
                        }



                        if(targetSubpart != null)
                        {
                            MyAPIGateway.Utilities.ShowNotification($"targetSubpart={targetSubpart}", 160);

                            if(MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.PRIMARY_TOOL_ACTION))
                            {
                                targetSubpart.Render.ColorMaskHsv = GetBuildColorMask();
                                MyAPIGateway.Utilities.ShowNotification("Painted!", 1000);
                            }
                        }

                        return false;
                    }
                }
#endif

                if(colorPickMode && targetPlayer != null)
                {
                    return HandleTool_ColorPickMode(trigger, targetPlayer);
                }

                selectedGrid = targetGrid as MyCubeGrid;

                PaintMaterial paintMaterial = GetLocalPaintMaterial();

                BlockMaterial blockMaterial = new BlockMaterial();
                string blockName = string.Empty;

                if(targetBlock != null)
                {
                    blockMaterial = new BlockMaterial(targetBlock.GetColorMask(), targetBlock.SkinSubtypeId);
                    blockName = (targetBlock.FatBlock == null ? targetBlock.ToString() : targetBlock.FatBlock.DefinitionDisplayNameText);
                }

                if(!IsBlockValid(targetBlock, paintMaterial, blockMaterial, trigger))
                    return false;

                selectedSlimBlock = targetBlock;

                if(!IgnoreAmmoConsumption && localHeldTool.Ammo == 0)
                {
                    if(trigger)
                    {
                        PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);
                        ShowNotification(1, "No ammo.", MyFontEnum.Red);
                    }

                    SetGUIToolStatus(0, "No ammo!", "red");
                    return false;
                }

                if(trigger)
                {
                    float paintSpeed = (1.0f / GetBlockSurface(selectedSlimBlock));
                    var paintedMaterial = PaintProcess(paintMaterial, blockMaterial, paintSpeed, blockName);

                    if(replaceAllMode && ReplaceColorAccess)
                    {
                        SendToServer_ReplaceColor(selectedGrid, new BlockMaterial(selectedSlimBlock), paintedMaterial, replaceGridSystem);
                    }
                    else
                    {
                        if(SymmetryAccess && MyAPIGateway.CubeBuilder.UseSymmetry && (selectedGrid.XSymmetryPlane.HasValue || selectedGrid.YSymmetryPlane.HasValue || selectedGrid.ZSymmetryPlane.HasValue))
                        {
                            var mirrorPlane = new Vector3I(
                                (selectedGrid.XSymmetryPlane.HasValue ? selectedGrid.XSymmetryPlane.Value.X : int.MinValue),
                                (selectedGrid.YSymmetryPlane.HasValue ? selectedGrid.YSymmetryPlane.Value.Y : int.MinValue),
                                (selectedGrid.ZSymmetryPlane.HasValue ? selectedGrid.ZSymmetryPlane.Value.Z : int.MinValue));

                            OddAxis odd = OddAxis.NONE;

                            if(selectedGrid.XSymmetryOdd)
                                odd |= OddAxis.X;

                            if(selectedGrid.YSymmetryOdd)
                                odd |= OddAxis.Y;

                            if(selectedGrid.ZSymmetryOdd)
                                odd |= OddAxis.Z;

                            SendToServer_PaintBlock(selectedGrid, selectedSlimBlock.Position, paintedMaterial, mirrorPlane, odd);
                        }
                        else
                        {
                            SendToServer_PaintBlock(selectedGrid, selectedSlimBlock.Position, paintedMaterial);
                        }
                    }

                    return true;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
        }

        const double PAINT_DISTANCE = 5;
        const double PAINT_AIM_START_OFFSET = 2.5; // forward offset of ray start when aiming down sights

        private List<IHitInfo> hits = new List<IHitInfo>();
        private Dictionary<long, DetectionInfo> detections = new Dictionary<long, DetectionInfo>();
        private List<MyLineSegmentOverlapResult<MyEntity>> rayOverlapResults = new List<MyLineSegmentOverlapResult<MyEntity>>();

        private struct DetectionInfo
        {
            public readonly IMyEntity Entity;
            public readonly Vector3D DetectionPoint;

            public DetectionInfo(IMyEntity entity, Vector3D detectionPoint)
            {
                Entity = entity;
                DetectionPoint = detectionPoint;
            }
        }

        private void GetTarget(IMyCharacter character, out IMyCubeGrid targetGrid, out IMySlimBlock targetBlock, out IMyPlayer targetPlayer)
        {
            targetGrid = null;
            targetBlock = null;
            targetPlayer = null;

            var head = character.GetHeadMatrix(false, true);
            var rayDir = head.Forward;
            var rayFrom = head.Translation;
            bool aiming = IsAimingDownSights(character);

            if(aiming)
                rayFrom += rayDir * PAINT_AIM_START_OFFSET;

            var rayTo = head.Translation + rayDir * PAINT_DISTANCE;

            if(colorPickMode && GetTargetCharacter(rayFrom, rayDir, PAINT_DISTANCE, character, ref targetPlayer))
                return;

            targetGrid = MyAPIGateway.CubeBuilder.FindClosestGrid();

            if(targetGrid == null || targetGrid.Physics == null || !targetGrid.Physics.Enabled)
            {
                targetGrid = null;
                return;
            }

            if(aiming)
            {
                var blockPos = targetGrid.RayCastBlocks(rayFrom, rayTo);

                if(blockPos.HasValue)
                    targetBlock = targetGrid.GetCubeBlock(blockPos.Value);

                return;
            }

            hits.Clear();
            detections.Clear();
            rayOverlapResults.Clear();

            // HACK copied and converted from MyDrillSensorRayCast.ReadEntitiesInRange()
            MyAPIGateway.Physics.CastRay(rayFrom, rayTo, hits, 24);

            foreach(var hit in hits)
            {
                if(hit.HitEntity == null)
                    continue;

                var hitPos = hit.Position;
                var parent = hit.HitEntity.GetTopMostParent();

                var grid = parent as IMyCubeGrid;
                if(grid != null)
                    hitPos += (grid.GridSizeEnum == MyCubeSize.Small ? (hit.Normal * -0.02f) : (hit.Normal * -0.08f));

                DetectionInfo detected;

                if(detections.TryGetValue(parent.EntityId, out detected))
                {
                    float dist1 = Vector3.DistanceSquared(rayFrom, detected.DetectionPoint);
                    float dist2 = Vector3.DistanceSquared(rayFrom, hitPos);

                    if(dist1 > dist2)
                        detections[parent.EntityId] = new DetectionInfo(parent, hitPos);
                }
                else
                {
                    detections[parent.EntityId] = new DetectionInfo(parent, hitPos);
                }
            }

            hits.Clear();

            var line = new LineD(rayFrom, rayTo);
            MyGamePruningStructure.GetAllEntitiesInRay(ref line, rayOverlapResults);

            foreach(var result in rayOverlapResults)
            {
                if(result.Element == null)
                    continue;

                var block = result.Element as IMyCubeBlock;

                if(block == null)
                    continue;

                var def = (MyCubeBlockDefinition)block.SlimBlock.BlockDefinition;

                if(def.HasPhysics)
                    continue;

                var parent = result.Element.GetTopMostParent();

                var blockInvMatrix = block.PositionComp.WorldMatrixNormalizedInv;
                var localRayFrom = Vector3D.Transform(rayFrom, ref blockInvMatrix);
                var localRayTo = Vector3D.Transform(rayTo, ref blockInvMatrix);
                var localLine = new Line(localRayFrom, localRayTo);

                //float? dist = new Ray(localRayFrom, Vector3.Normalize(localRayTo - localRayFrom)).Intersects(block.PositionComp.LocalAABB) + 0.01f;

                float dist;

                if(!block.PositionComp.LocalAABB.Intersects(localLine, out dist))
                    continue;

                var hitPos = rayFrom + rayDir * dist;
                DetectionInfo detected;

                if(detections.TryGetValue(parent.EntityId, out detected))
                {
                    var dist1 = Vector3.DistanceSquared(detected.DetectionPoint, rayFrom);
                    var dist2 = Vector3.DistanceSquared(hitPos, rayFrom);

                    if(dist1 > dist2)
                        detections[parent.EntityId] = new DetectionInfo(parent, hitPos);
                }
                else
                {
                    detections[parent.EntityId] = new DetectionInfo(parent, hitPos);
                }
            }

            rayOverlapResults.Clear();

            if(detections.Count == 0)
                return;

            float num = float.MaxValue;
            DetectionInfo closest = new DetectionInfo(null, Vector3D.Zero);

            foreach(var detected in detections.Values)
            {
                var ent = detected.Entity;

                if(ent.Physics == null || !ent.Physics.Enabled)
                    continue;

                float dist = (float)Vector3D.DistanceSquared(detected.DetectionPoint, rayFrom);

                if(dist < num)
                {
                    closest = detected;
                    num = dist;
                }
            }

            detections.Clear();

            targetGrid = closest.Entity as IMyCubeGrid;

            if(targetGrid == null)
                return;

            var localHitPos = Vector3D.Transform(closest.DetectionPoint, targetGrid.WorldMatrixNormalizedInv);
            Vector3I blockGridPos;
            targetGrid.FixTargetCube(out blockGridPos, localHitPos / targetGrid.GridSize);
            targetBlock = targetGrid.GetCubeBlock(blockGridPos);
        }

        private bool GetTargetCharacter(Vector3D rayFrom, Vector3D rayDir, double rayLength, IMyCharacter character, ref IMyPlayer targetPlayer)
        {
            players.Clear();
            MyAPIGateway.Players.GetPlayers(players);

            var ray = new RayD(rayFrom, rayDir);

            foreach(var p in players)
            {
                var c = p.Character;

                if(c == null || c == character)
                    continue;

                var sphere = GetCharacterSelectionSphere(c);

                if(Vector3D.DistanceSquared(rayFrom, sphere.Center) > (rayLength * rayLength))
                    continue;

                var dist = sphere.Intersects(ray);

                if(!dist.HasValue || dist.Value > rayLength)
                    continue;

                targetPlayer = p;
                break;
            }

            players.Clear();

            return (targetPlayer != null);
        }

        private bool IsBlockValid(IMySlimBlock block, PaintMaterial paintMaterial, BlockMaterial blockMaterial, bool trigger)
        {
            if(block == null)
            {
                if(colorPickMode)
                {
                    ShowNotification(0, "Aim at a block or player and click to pick color.", MyFontEnum.Blue);
                }
                else if(replaceAllMode)
                {
                    ShowNotification(0, "Aim at a block to replace its color from the entire grid/ship.", MyFontEnum.Blue);
                }
                else if(trigger)
                {
                    //PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);

                    if(!IgnoreAmmoConsumption && localHeldTool.Ammo == 0)
                        ShowNotification(1, "No ammo.", MyFontEnum.Red);
                    else
                        ShowNotification(1, "Aim at a block to paint it.", MyFontEnum.Red);
                }

                return false;
            }

            selectedSlimBlock = block;
            selectedInvalid = false;

            #region Symmetry toggle info
            symmetryStatus = null;

            if(!replaceAllMode && ReplaceColorAccess)
            {
                if(selectedGrid.XSymmetryPlane.HasValue || selectedGrid.YSymmetryPlane.HasValue || selectedGrid.ZSymmetryPlane.HasValue)
                {
                    bool inputReadable = (InputHandler.IsInputReadable() && !MyAPIGateway.Session.IsCameraUserControlledSpectator);
                    var assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY));

                    if(inputReadable)
                    {
                        symmetryInputAvailable = true;

                        if(MyAPIGateway.CubeBuilder.UseSymmetry)
                            symmetryStatus = (TextAPIReady ? $"[{assigned}] <color=yellow>Symmetry: ON" : $"([{assigned}]) Symmetry: ON");
                        else
                            symmetryStatus = (TextAPIReady ? $"[{assigned}] Symmetry: OFF" : $"([{assigned}]) Symmetry: OFF");
                    }
                    else
                    {
                        if(MyAPIGateway.CubeBuilder.UseSymmetry)
                            symmetryStatus = (TextAPIReady ? "<color=yellow>Symmetry: ON" : "Symmetry: ON");
                        else
                            symmetryStatus = (TextAPIReady ? "Symmetry: OFF" : "Symmetry: OFF");
                    }
                }
                else
                {
                    symmetryStatus = (TextAPIReady ? "<color=gray>Symetry: not set-up" : "Symetry: not set-up");
                }
            }
            #endregion

            if(colorPickMode)
            {
                if(trigger)
                {
                    SendToServer_ColorPickMode(false);
                    PickColorAndSkinFromBlock((byte)localColorData.SelectedSlot, blockMaterial);
                }
                else
                {
                    if(!ColorMaskEquals(blockMaterial.ColorMask, prevColorMaskPreview))
                    {
                        prevColorMaskPreview = blockMaterial.ColorMask;
                        SetToolColor(blockMaterial.ColorMask);

                        if(settings.extraSounds)
                            PlayHudSound(SOUND_HUD_ITEM, 0.75f);
                    }

                    if(paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
                        SetGUIToolStatus(0, "Click to get this color.", "lime");
                    else if(!paintMaterial.ColorMask.HasValue && paintMaterial.Skin.HasValue)
                        SetGUIToolStatus(0, "Click to select this skin.", "lime");
                    else
                        SetGUIToolStatus(0, "Click to get this material.", "lime");

                    SetGUIToolStatus(1, null);
                }

                return false;
            }

            if(!paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
            {
                var assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE));

                ShowNotification(0, "No paint or skin enabled.", MyFontEnum.Red);
                ShowNotification(1, $"Press [{assigned}] to toggle color or combined with [Shift] to toggle skin.", MyFontEnum.White);

                SetGUIToolStatus(0, "No paint or skin enabled.", "red");
                SetGUIToolStatus(1, null);
                return false;
            }

            if(!AllowedToPaintGrid(block.CubeGrid, MyAPIGateway.Session.Player.IdentityId))
            {
                selectedInvalid = true;

                if(trigger)
                {
                    PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);
                    ShowNotification(0, "Can't paint enemy ships.", MyFontEnum.Red);
                }

                SetGUIToolStatus(0, "Not allied ship.", "red");
                SetGUIToolStatus(1, null);

                return false;
            }

            bool materialEquals = paintMaterial.PaintEquals(blockMaterial);

            if(replaceAllMode)
            {
                selectedInvalid = materialEquals;

                var assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY));

                if(selectedInvalid)
                    SetGUIToolStatus(0, "Already this material.", "red");
                else
                    SetGUIToolStatus(0, "Click to replace material.", "lime");

                SetGUIToolStatus(1, $"[{assigned}] Replace mode: {(replaceGridSystem ? "Ship-wide" : "Grid")}", (replaceGridSystem ? "yellow" : null));

                return (selectedInvalid ? false : true);
            }

            if(!InstantPaintAccess)
            {
                var def = (MyCubeBlockDefinition)block.BlockDefinition;
                bool built = (block.BuildLevelRatio >= def.CriticalIntegrityRatio);

                if(!built || block.CurrentDamage > (block.MaxIntegrity / 10.0f))
                {
                    if(trigger)
                    {
                        PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);

                        ShowNotification(0, "Unfinished blocks can't be painted!", MyFontEnum.Red);
                    }

                    selectedInvalid = true;

                    SetGUIToolStatus(0, (!built ? "Block not built" : "Block damaged"), "red");
                    SetGUIToolStatus(1, null);

                    return false;
                }
            }

            var grid = block.CubeGrid as MyCubeGrid;
            bool symmetry = SymmetryAccess && MyCubeBuilder.Static.UseSymmetry && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue);
            bool symmetrySameColor = true;

            if(materialEquals)
            {
                if(symmetry)
                {
                    Vector3I? mirrorX = null;
                    Vector3I? mirrorY = null;
                    Vector3I? mirrorZ = null;
                    Vector3I? mirrorYZ = null;

                    // NOTE: do not optimize, all methods must be called
                    if(!MirrorCheckSameColor(grid, 0, block.Position, paintMaterial, out mirrorX))
                        symmetrySameColor = false;

                    if(!MirrorCheckSameColor(grid, 1, block.Position, paintMaterial, out mirrorY))
                        symmetrySameColor = false;

                    if(!MirrorCheckSameColor(grid, 2, block.Position, paintMaterial, out mirrorZ))
                        symmetrySameColor = false;

                    if(mirrorX.HasValue && grid.YSymmetryPlane.HasValue) // XY
                    {
                        if(!MirrorCheckSameColor(grid, 1, mirrorX.Value, paintMaterial, out mirrorX))
                            symmetrySameColor = false;
                    }

                    if(mirrorX.HasValue && grid.ZSymmetryPlane.HasValue) // XZ
                    {
                        if(!MirrorCheckSameColor(grid, 2, mirrorX.Value, paintMaterial, out mirrorX))
                            symmetrySameColor = false;
                    }

                    if(mirrorY.HasValue && grid.ZSymmetryPlane.HasValue) // YZ
                    {
                        if(!MirrorCheckSameColor(grid, 2, mirrorY.Value, paintMaterial, out mirrorYZ))
                            symmetrySameColor = false;
                    }

                    if(grid.XSymmetryPlane.HasValue && mirrorYZ.HasValue) // XYZ
                    {
                        if(!MirrorCheckSameColor(grid, 0, mirrorYZ.Value, paintMaterial, out mirrorX))
                            symmetrySameColor = false;
                    }
                }

                if(!symmetry || symmetrySameColor)
                {
                    selectedInvalid = true;

                    if(symmetry)
                        SetGUIToolStatus(0, "Block(s) color match.", "lime");
                    else
                        SetGUIToolStatus(0, "Colors match.", "lime");

                    SetGUIToolStatus(1, symmetryStatus);
                    return false;
                }
            }

            if(!trigger)
            {
                if(symmetry && !symmetrySameColor)
                    SetGUIToolStatus(0, "Click to update symmetry paint.");
                else if(InstantPaintAccess)
                    SetGUIToolStatus(0, "Click to paint.");
                else
                    SetGUIToolStatus(0, "Hold click to paint.");

                SetGUIToolStatus(1, symmetryStatus);
            }

            return true;
        }

        private bool HandleTool_ColorPickMode(bool trigger, IMyPlayer targetPlayer)
        {
            if(!playerColorData.ContainsKey(targetPlayer.SteamUserId))
                return false;

            var cd = playerColorData[targetPlayer.SteamUserId];
            var targetColorMask = cd.Colors[cd.SelectedSlot];
            selectedPlayerColorMask = targetColorMask;
            selectedPlayerBlockSkin = BlockSkins[cd.SelectedSkinIndex].SubtypeId;
            selectedPlayer = targetPlayer;

            if(trigger)
            {
                SendToServer_ColorPickMode(false);

                var targetMaterial = new BlockMaterial(selectedPlayerColorMask, selectedPlayerBlockSkin);

                PickColorAndSkinFromBlock((byte)localColorData.SelectedSlot, targetMaterial);
            }
            else
            {
                if(!ColorMaskEquals(targetColorMask, prevColorMaskPreview))
                {
                    prevColorMaskPreview = targetColorMask;

                    SetToolColor(targetColorMask);

                    if(settings.extraSounds)
                        PlayHudSound(SOUND_HUD_ITEM, 0.75f);
                }

                SetGUIToolStatus(0, "Click to get color from player.");
                SetGUIToolStatus(1, null);
            }

            return false;
        }

        internal PaintMaterial PaintProcess(PaintMaterial paintMaterial, BlockMaterial blockMaterial, float paintSpeed, string blockName)
        {
            if(replaceAllMode)
            {
                // notification for this is done in the ReceivedPacket method to avoid re-iterating blocks

                return paintMaterial;
            }

            if(!paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
            {
                return paintMaterial;
            }

            if(InstantPaintAccess)
            {
                SetGUIToolStatus(0, "Painted!", "lime");
                SetGUIToolStatus(1, symmetryStatus);
                return paintMaterial;
            }

            if(!paintMaterial.ColorMask.HasValue && paintMaterial.Skin.HasValue)
            {
                SetGUIToolStatus(0, "Skinned!", "lime");
                SetGUIToolStatus(1, symmetryStatus);
                return paintMaterial;
            }

            var colorMask = (paintMaterial.ColorMask.HasValue ? paintMaterial.ColorMask.Value : blockMaterial.ColorMask);
            var blockColor = blockMaterial.ColorMask;

            // If hue is within reason change saturation and value directly.
            if(Math.Abs(blockColor.X - colorMask.X) < COLOR_EPSILON)
            {
                paintSpeed *= PAINT_SPEED * PAINT_SKIP_TICKS;
                paintSpeed *= MyAPIGateway.Session.WelderSpeedMultiplier;

                for(int i = 0; i < 3; i++)
                {
                    if(blockColor.GetDim(i) > colorMask.GetDim(i))
                        blockColor.SetDim(i, Math.Max(blockColor.GetDim(i) - paintSpeed, colorMask.GetDim(i)));
                    else
                        blockColor.SetDim(i, Math.Min(blockColor.GetDim(i) + paintSpeed, colorMask.GetDim(i)));
                }

                if(ColorMaskEquals(blockColor, colorMask))
                {
                    blockColor = colorMask;

                    SetGUIToolStatus(0, "Painting done!", "lime");

                    if(settings.extraSounds)
                        PlayHudSound(SOUND_HUD_COLOR, 0.8f);
                }
                else
                {
                    var percent = ColorPercent(blockColor, colorMask);

                    SetGUIToolStatus(0, $"Painting {percent}%...");
                }
            }
            else // if hue is too far off, first "remove" the paint.
            {
                paintSpeed *= DEPAINT_SPEED * PAINT_SKIP_TICKS;
                paintSpeed *= MyAPIGateway.Session.GrinderSpeedMultiplier;

                blockColor.Y = Math.Max(blockColor.Y - paintSpeed, DEFAULT_COLOR.Y);
                blockColor.Z = (blockColor.Z > 0 ? Math.Max(blockColor.Z - paintSpeed, DEFAULT_COLOR.Z) : Math.Min(blockColor.Z + paintSpeed, DEFAULT_COLOR.Z));

                // when saturation and value reach the default color, change hue and begin painting
                if(Math.Abs(blockColor.Y - DEFAULT_COLOR.Y) < COLOR_EPSILON && Math.Abs(blockColor.Z - DEFAULT_COLOR.Z) < COLOR_EPSILON)
                {
                    blockColor.X = colorMask.X;
                }

                if(ColorMaskEquals(blockColor, DEFAULT_COLOR))
                {
                    blockColor = DEFAULT_COLOR;
                    blockColor.X = colorMask.X;

                    if(colorMask == DEFAULT_COLOR)
                        SetGUIToolStatus(0, "Removing paint done!");
                    else
                        SetGUIToolStatus(0, "Removing paint 100%...");
                }
                else
                {
                    var percent = ColorPercent(blockColor, DEFAULT_COLOR);

                    SetGUIToolStatus(0, $"Removing paint {percent}%...");
                }
            }

            return new PaintMaterial(blockColor, paintMaterial.Skin);
        }

        public void ToolHolstered()
        {
            localHeldTool = null;
            symmetryInputAvailable = false;
            selectedSlimBlock = null;
            selectedPlayer = null;
            selectedInvalid = false;

            GenerateAimInfo(); // hides the info if visible

            if(colorPickMode)
            {
                SendToServer_ColorPickMode(false);

                ShowNotification(0, "Color picking cancelled.", MyFontEnum.White, 1000);

                PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);
            }
            else if(replaceAllMode)
            {
                ShowNotification(0, "Replace color mode turned off.", MyFontEnum.White, 2000);

                replaceAllMode = false;
            }
        }
        #endregion

        public void MessageEntered(string msg, ref bool send)
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
                            SendToServer_ColorPickMode(true);
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

                        PickColorAndSkinFromBlock((byte)localColorData.SelectedSlot, material);
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
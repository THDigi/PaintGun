using System;
using System.Collections.Generic;
using System.Text;
using Draygo.API;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRageMath;

namespace Digi.PaintGun
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public partial class PaintGunMod : MySessionComponentBase
    {
        #region Init and unload
        public override void LoadData()
        {
            instance = this;
            Log.SetUp(MOD_NAME, WORKSHOP_ID);
        }

        public void Init()
        {
            init = true;
            isPlayer = !(MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer);

            Log.Init();

            MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET, ReceivedPacket);

            if(isPlayer) // stuff that shouldn't happen DS-side.
            {
                UpdateConfigValues();

                textAPI = new HudAPIv2();
                settings = new Settings();

                MyAPIGateway.Utilities.MessageEntered += MessageEntered;
                MyAPIGateway.Gui.GuiControlRemoved += GuiControlRemoved;

                if(!MyAPIGateway.Multiplayer.IsServer)
                    SendToServer_RequestColorList(MyAPIGateway.Multiplayer.MyId);
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
                    MyAPIGateway.Gui.GuiControlRemoved -= GuiControlRemoved;
                }

                if(settings != null)
                {
                    settings.Close();
                    settings = null;
                }

                hudSoundEmitter?.Cleanup();

                if(textAPI != null)
                {
                    textAPI.Close();
                    textAPI = null;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            Log.Close();
        }
        #endregion

        #region Game config monitor
        private void GuiControlRemoved(object obj)
        {
            try
            {
                if(obj.ToString().EndsWith("ScreenOptionsSpace")) // closing options menu just assumes you changed something so it'll re-check config settings
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
        }
        #endregion

        #region Painting methods
        private void PaintBlock(MyCubeGrid grid, Vector3I gridPosition, Vector3 color)
        {
            grid.ChangeColor(grid.GetCubeBlock(gridPosition), color); // HACK getting a MySlimBlock and sending it straight to arguments avoids getting prohibited errors.
        }

        private void PaintBlockSymmetry(MyCubeGrid grid, Vector3I gridPosition, Vector3 color, Vector3I mirrorPlane, OddAxis odd)
        {
            grid.ChangeColor(grid.GetCubeBlock(gridPosition), color);

            bool oddX = (odd & OddAxis.X) == OddAxis.X;
            bool oddY = (odd & OddAxis.Y) == OddAxis.Y;
            bool oddZ = (odd & OddAxis.Z) == OddAxis.Z;

            var mirrorX = MirrorPaint(grid, 0, mirrorPlane, oddX, gridPosition, color); // X
            var mirrorY = MirrorPaint(grid, 1, mirrorPlane, oddY, gridPosition, color); // Y
            var mirrorZ = MirrorPaint(grid, 2, mirrorPlane, oddZ, gridPosition, color); // Z
            Vector3I? mirrorYZ = null;

            if(mirrorX.HasValue && mirrorPlane.Y > int.MinValue) // XY
                MirrorPaint(grid, 1, mirrorPlane, oddY, mirrorX.Value, color);

            if(mirrorX.HasValue && mirrorPlane.Z > int.MinValue) // XZ
                MirrorPaint(grid, 2, mirrorPlane, oddZ, mirrorX.Value, color);

            if(mirrorY.HasValue && mirrorPlane.Z > int.MinValue) // YZ
                mirrorYZ = MirrorPaint(grid, 2, mirrorPlane, oddZ, mirrorY.Value, color);

            if(mirrorPlane.X > int.MinValue && mirrorYZ.HasValue) // XYZ
                MirrorPaint(grid, 0, mirrorPlane, oddX, mirrorYZ.Value, color);
        }

        private void ReplaceColorInGrid(MyCubeGrid grid, ulong steamId, Vector3 oldColor, Vector3 newColor, bool useGridSystem)
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
                    if(ColorMaskEquals(slim.GetColorMask(), oldColor))
                    {
                        // GetCubeBlock() is a workaround for MySlimBlock being prohibited
                        g.ChangeColor(g.GetCubeBlock(slim.Position), newColor);
                        affected++;
                    }
                }
            }

            if(MyAPIGateway.Multiplayer.MyId == steamId)
            {
                ShowNotification(2, $"Replaced color in {affected} blocks.", MyFontEnum.White, 2000);
            }
        }

        #region Symmetry
        private Vector3I? MirrorPaint(MyCubeGrid g, int axis, Vector3I mirror, bool odd, Vector3I originalPosition, Vector3 color)
        {
            switch(axis)
            {
                case 0:
                    if(mirror.X > int.MinValue)
                    {
                        var mirrorX = originalPosition + new Vector3I(((mirror.X - originalPosition.X) * 2) - (odd ? 1 : 0), 0, 0);

                        if(g.CubeExists(mirrorX))
                            g.ChangeColor(g.GetCubeBlock(mirrorX), color);

                        return mirrorX;
                    }
                    break;

                case 1:
                    if(mirror.Y > int.MinValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((mirror.Y - originalPosition.Y) * 2) - (odd ? 1 : 0), 0);

                        if(g.CubeExists(mirrorY))
                            g.ChangeColor(g.GetCubeBlock(mirrorY), color);

                        return mirrorY;
                    }
                    break;

                case 2:
                    if(mirror.Z > int.MinValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((mirror.Z - originalPosition.Z) * 2) + (odd ? 1 : 0)); // reversed on odd

                        if(g.CubeExists(mirrorZ))
                            g.ChangeColor(g.GetCubeBlock(mirrorZ), color);

                        return mirrorZ;
                    }
                    break;
            }

            return null;
        }

        private bool MirrorCheckSameColor(MyCubeGrid g, int axis, Vector3I originalPosition, Vector3 colorMask, out Vector3I? mirror)
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
                            return ColorMaskEquals(slim.GetColorMask(), colorMask);
                    }
                    break;

                case 1:
                    if(g.YSymmetryPlane.HasValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((g.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (g.YSymmetryOdd ? 1 : 0), 0);
                        var slim = ((IMyCubeGrid)g).GetCubeBlock(mirrorY);
                        mirror = mirrorY;

                        if(slim != null)
                            return ColorMaskEquals(slim.GetColorMask(), colorMask);
                    }
                    break;

                case 2:
                    if(g.ZSymmetryPlane.HasValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((g.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (g.ZSymmetryOdd ? 1 : 0)); // reversed on odd
                        var slim = ((IMyCubeGrid)g).GetCubeBlock(mirrorZ);
                        mirror = mirrorZ;

                        if(slim != null)
                            return ColorMaskEquals(slim.GetColorMask(), colorMask);
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
                            DrawInflatedSelectionBox(block, MATERIAL_GIZMIDRAWLINE);

                        return mirrorX;
                    }
                    break;

                case 1:
                    if(g.YSymmetryPlane.HasValue)
                    {
                        var mirrorY = originalPosition + new Vector3I(0, ((g.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (g.YSymmetryOdd ? 1 : 0), 0);
                        var block = g.GetCubeBlock(mirrorY) as IMySlimBlock;

                        if(block != null)
                            DrawInflatedSelectionBox(block, MATERIAL_GIZMIDRAWLINE);

                        return mirrorY;
                    }
                    break;

                case 2:
                    if(g.ZSymmetryPlane.HasValue)
                    {
                        var mirrorZ = originalPosition + new Vector3I(0, 0, ((g.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (g.ZSymmetryOdd ? 1 : 0)); // reversed on odd
                        var block = g.GetCubeBlock(mirrorZ) as IMySlimBlock;

                        if(block != null)
                            DrawInflatedSelectionBox(block, MATERIAL_GIZMIDRAWLINE);

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
            if(!isPlayer || MyParticlesManager.Paused)
                return;

            try
            {
                // check selected slot and send updates to server
                if(tick % 10 == 0)
                {
                    if(localColorData == null && !playerColorData.TryGetValue(MyAPIGateway.Multiplayer.MyId, out localColorData))
                    {
                        localColorData = null;
                    }

                    if(localColorData != null && localColorData.SelectedSlot != prevSelectedColorSlot)
                    {
                        prevSelectedColorSlot = localColorData.SelectedSlot;
                        SendToServer_SelectedColorSlot((byte)localColorData.SelectedSlot);
                    }
                }

                // sync selected slot when inside the color picker menu
                if(MyAPIGateway.Gui.IsCursorVisible && MyAPIGateway.Gui.ActiveGamePlayScreen == "ColorPick")
                {
                    localColorData.SelectedSlot = MyAPIGateway.Session.Player.SelectedBuildColorSlot;
                    SetToolColor(localColorData.Colors[localColorData.SelectedSlot]);
                }

                // DEBUG TODO added controlled check but needs to get rid of the fake block UI if you're already selecting something...
                if(localHeldTool != null && MyAPIGateway.Session.ControlledObject == MyAPIGateway.Session.Player.Character)
                {
                    bool inputReadable = InputHandler.IsInputReadable();

                    if(inputReadable)
                    {
                        if(symmetryInputAvailable && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
                        {
                            MyAPIGateway.CubeBuilder.UseSymmetry = !MyAPIGateway.CubeBuilder.UseSymmetry;
                        }

                        if(replaceAllMode && MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE_SYMMETRY))
                        {
                            replaceGridSystem = !replaceGridSystem;
                        }

                        if(!MyAPIGateway.Input.IsAnyAltKeyPressed())
                        {
                            var change = 0;

                            if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_LEFT))
                                change = 1;
                            else if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SWITCH_RIGHT))
                                change = -1;
                            else
                                change = MyAPIGateway.Input.DeltaMouseScrollWheelValue();

                            if(change != 0 && localColorData != null)
                            {
                                if(settings.extraSounds)
                                    PlayHudSound(SOUND_HUD_CLICK, 0.1f);

                                if(change < 0)
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
                                            localColorData.SelectedSlot = (localColorData.Colors.Count - 1);
                                    }
                                }

                                MyAPIGateway.Session.Player.SelectedBuildColorSlot = localColorData.SelectedSlot;
                                SetToolColor(localColorData.Colors[localColorData.SelectedSlot]);
                            }
                        }

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

                                    if(SendToServer_SetColor((byte)localColorData.SelectedSlot, targetColor, true))
                                    {
                                        PlayHudSound(SOUND_HUD_MOUSE_CLICK, 0.25f);
                                        ShowNotification(0, $"Slot {localColorData.SelectedSlot + 1} set to {ColorMaskToString(targetColor)}", MyFontEnum.White, 2000);
                                    }
                                    else
                                    {
                                        PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);
                                    }
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

                    bool trigger = inputReadable && MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.PRIMARY_TOOL_ACTION);

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

                    if(tick % PAINT_SKIP_TICKS == 0)
                    {
                        if(replaceAllMode && !ReplaceColorAccess) // if access no longer allows it, disable the replace mode
                        {
                            replaceAllMode = false;
                            ShowNotification(0, "Replace color mode turned off.", MyFontEnum.White, 2000);
                        }

                        var painted = HoldingTool(trigger);

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

                    if(symmetryInputAvailable)
                    {
                        if(MyAPIGateway.CubeBuilder.UseSymmetry && selectedGrid != null && (selectedGrid.XSymmetryPlane.HasValue || selectedGrid.YSymmetryPlane.HasValue || selectedGrid.ZSymmetryPlane.HasValue))
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

                    if(selectedSlimBlock != null)
                    {
                        if(selectedSlimBlock.IsDestroyed || selectedSlimBlock.IsFullyDismounted)
                        {
                            selectedSlimBlock = null;
                        }
                        else
                        {
                            DrawInflatedSelectionBox(selectedSlimBlock, (selectedInvalid ? MATERIAL_GIZMIDRAWLINERED : MATERIAL_GIZMIDRAWLINE));

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
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if(!init)
                {
                    if(MyAPIGateway.Session == null || MyAPIGateway.Multiplayer == null)
                        return;

                    Init();
                }

                if(isPlayer && !playerObjectFound)
                {
                    var colors = MyAPIGateway.Session.Player?.DefaultBuildColorSlots;

                    if(colors != null && colors.HasValue)
                    {
                        DEFAULT_COLOR = colors.Value.ItemAt(0);
                        playerObjectFound = true;
                    }
                }

                unchecked
                {
                    ++tick;
                }

                // HUD toggle monitor; required here because it gets the previous value if used in HandleInput()
                if(MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.TOGGLE_HUD))
                    gameHUD = !MyAPIGateway.Session.Config.MinimalHud;

                if(MyAPIGateway.Multiplayer.IsServer && tick % 10 == 0)
                {
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
                            playerColorData.Add(steamId, new PlayerColorData(steamId, new List<Vector3>(kv.Value))); // new list to not use the same reference, reason noted before

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
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void Draw()
        {
            try
            {
                if(!init || !isPlayer)
                    return;

                viewProjInvCompute = true;

                int toolDrawCount = ToolDraw.Count;

                if(toolDrawCount > 0)
                {
                    for(int i = 0; i < toolDrawCount; ++i)
                    {
                        ToolDraw[i].Draw();
                    }
                }

                if(selectedPlayer != null)
                {
                    var selectedCharacter = selectedPlayer.Character;

                    if(selectedCharacter == null || selectedCharacter.MarkedForClose || selectedCharacter.Closed)
                    {
                        selectedPlayer = null;
                    }
                    else if(selectedCharacter.Visible)
                    {
                        var color = Color.Green;
                        var matrix = selectedCharacter.WorldMatrix;
                        var box = (BoundingBoxD)selectedCharacter.LocalAABB;
                        var worldToLocal = selectedCharacter.WorldMatrixInvScaled;

                        MySimpleObjectDraw.DrawAttachedTransparentBox(ref matrix, ref box, ref color, selectedCharacter.Render.GetRenderObjectID(), ref worldToLocal, MySimpleObjectRasterizer.Wireframe, Vector3I.One, 0.05f, null, MATERIAL_GIZMIDRAWLINE, false, blendType: HELPERS_BLEND_TYPE);
                    }
                }

                bool hideTextAPI = true;

                if(localHeldTool != null && localColorData != null && !MyAPIGateway.Gui.IsCursorVisible && !(settings.hidePaletteWithHUD && !gameHUD))
                {
                    hideTextAPI = false;

                    var cam = MyAPIGateway.Session.Camera;
                    var camMatrix = cam.WorldMatrix;
                    var scaleFOV = (float)Math.Tan(cam.FovWithZoom * 0.5);
                    scaleFOV *= settings.paletteScale;

                    #region Draw HUD palette
                    {
                        var worldPos = HUDtoWorld(new Vector2((float)settings.paletteScreenPos.X, (float)settings.paletteScreenPos.Y));

                        float squareWidth = 0.0014f * scaleFOV;
                        float squareHeight = 0.0012f * scaleFOV;
                        float selectedWidth = (squareWidth + (squareWidth / 7f));
                        float selectedHeight = (squareHeight + (squareHeight / 7f));
                        double spacingAdd = 0.0006 * scaleFOV;
                        double spacingWidth = (squareWidth * 2) + spacingAdd;
                        double spacingHeight = (squareHeight * 2) + spacingAdd;
                        const int MIDDLE_INDEX = 7;
                        const float BG_WIDTH_MUL = 3.85f;
                        const float BG_HEIGHT_MUL = 1.3f;

                        var pos = worldPos + camMatrix.Left * (spacingWidth * (MIDDLE_INDEX / 2)) + camMatrix.Up * (spacingHeight / 2);

                        for(int i = 0; i < localColorData.Colors.Count; i++)
                        {
                            var v = localColorData.Colors[i];
                            var c = ColorMaskToRGB(v);

                            if(i == MIDDLE_INDEX)
                                pos += camMatrix.Left * (spacingWidth * MIDDLE_INDEX) + camMatrix.Down * spacingHeight;

                            if(i == localColorData.SelectedSlot)
                                MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_SELECTED, Color.White, pos, camMatrix.Left, camMatrix.Up, selectedWidth, selectedHeight, Vector2.Zero, FOREGROUND_BLEND_TYPE);

                            MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, c, pos, camMatrix.Left, camMatrix.Up, squareWidth, squareHeight, Vector2.Zero, FOREGROUND_BLEND_TYPE);

                            pos += camMatrix.Right * spacingWidth;
                        }

                        var color = BLOCKINFO_TITLE_BG_COLOR * (settings.paletteBackgroundOpacity < 0 ? gameHUDBkOpacity : settings.paletteBackgroundOpacity);
                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, color, worldPos, camMatrix.Left, camMatrix.Up, (float)(spacingWidth * BG_WIDTH_MUL), (float)(spacingHeight * BG_HEIGHT_MUL), Vector2.Zero, BACKGROUND_BLEND_TYPE);
                    }
                    #endregion

                    #region HUD aim info (textAPI or vanilla block info)
                    bool targetCharacter = (colorPickMode && selectedPlayer != null);
                    bool showAimInfo = (selectedSlimBlock != null || targetCharacter);

                    if(showAimInfo)
                    {
                        if(textObject != null) // textAPI text with manually drawn billboards for GUI
                        {
                            Vector3 targetColor = (targetCharacter ? selectedPlayerColorMask : selectedSlimBlock.ColorMaskHSV);
                            var paintColor = localColorData.Colors[localColorData.SelectedSlot];

                            var worldPos = HUDtoWorld(BLOCKINFO_POSITION);
                            var worldSize = BLOCKINFO_SIZE;

                            if(Math.Abs(aspectRatio - (16d / 10d)) <= 0.01) // HACK 16:10 aspect ratio manual fix
                                worldSize.X = 0.018f;

                            worldSize *= 2 * scaleFOV;

                            if(Math.Abs(aspectRatio - (5d / 4d)) <= 0.01) // HACK 5:4 aspect ratio manual fix
                                worldSize.X *= ASPECT_RATIO_5_4_FIX;

                            var topPos = worldPos + camMatrix.Left * worldSize.X + camMatrix.Up * (worldSize.Y * 2); // center-top position on the box

                            var titleBgHeight = (0.0065f * scaleFOV);
                            var lowerBgHeight = (worldSize.Y - titleBgHeight);

                            var titleBgPos = topPos + camMatrix.Down * titleBgHeight;
                            var lowerBgPos = topPos + camMatrix.Down * (titleBgHeight * 2 + lowerBgHeight);

                            MyTransparentGeometry.AddBillboardOriented(MATERIAL_VANILLA_SQUARE, BLOCKINFO_TITLE_BG_COLOR, titleBgPos, camMatrix.Left, camMatrix.Up, worldSize.X, titleBgHeight, Vector2.Zero, BACKGROUND_BLEND_TYPE);
                            MyTransparentGeometry.AddBillboardOriented(MATERIAL_VANILLA_SQUARE, BLOCKINFO_LOWER_BG_COLOR, lowerBgPos, camMatrix.Left, camMatrix.Up, worldSize.X, lowerBgHeight, Vector2.Zero, BACKGROUND_BLEND_TYPE);

                            var topLeft = topPos + camMatrix.Left * worldSize.X;
                            var blockIconPos = topLeft + camMatrix.Down * (BLOCKINFO_ICONS_OFFSET.Y * scaleFOV) + camMatrix.Left * (BLOCKINFO_ICONS_OFFSET.X * scaleFOV);
                            var paintIconPos = blockIconPos + camMatrix.Down * (BLOCKINFO_ICONS_OFFSET.Z * scaleFOV);
                            var iconSize = BLOCKINFO_ICON_SIZE * scaleFOV;

                            if(targetCharacter)
                                MyTransparentGeometry.AddBillboardOriented(MATERIAL_ICON_GENERIC_CHARACTER, ColorMaskToRGB(targetColor), blockIconPos, camMatrix.Left, camMatrix.Up, iconSize, iconSize, Vector2.Zero, FOREGROUND_BLEND_TYPE);
                            else
                                MyTransparentGeometry.AddBillboardOriented(MATERIAL_ICON_GENERIC_BLOCK, ColorMaskToRGB(targetColor), blockIconPos, camMatrix.Left, camMatrix.Up, iconSize, iconSize, Vector2.Zero, FOREGROUND_BLEND_TYPE);

                            MyTransparentGeometry.AddBillboardOriented(MATERIAL_ICON_PAINT_AMMO, ColorMaskToRGB(paintColor), paintIconPos, camMatrix.Left, camMatrix.Up, iconSize, iconSize, Vector2.Zero, FOREGROUND_BLEND_TYPE);

                            var progressBarPos = topLeft + camMatrix.Left * (BLOCKINFO_BAR_OFFSET_X * scaleFOV) + camMatrix.Down * (titleBgHeight * 2 + lowerBgHeight);
                            var progressBarWidth = BLOCKINFO_BAR_WIDTH * scaleFOV;
                            var progressBarHeight = lowerBgHeight * BLOCKINFO_BAR_HEIGHT_SCALE;

                            MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, BLOCKINFO_BAR_BG_COLOR, progressBarPos, camMatrix.Left, camMatrix.Up, progressBarWidth, progressBarHeight, Vector2.Zero, BACKGROUND_BLEND_TYPE);

                            var progress = ColorPercentFull(targetColor, paintColor);

                            progressBarPos += camMatrix.Down * (progressBarHeight * (1 - progress));
                            progressBarHeight *= progress;

                            MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, BLOCKINFO_BAR_COLOR, progressBarPos, camMatrix.Left, camMatrix.Up, progressBarWidth, progressBarHeight, Vector2.Zero, FOREGROUND_BLEND_TYPE);
                        }
                        else // otherwise, use the game's block info, but it's limited, can't show the color
                        {
                            // TODO this entire thing is broken due to recently added tool hints overwriting it
                            var targetColor = selectedSlimBlock.ColorMaskHSV;
                            var paintColor = localColorData.Colors[localColorData.SelectedSlot];
                            var info = Sandbox.Game.Gui.MyHud.BlockInfo;

                            info.Visible = true;

                            if(targetCharacter)
                            {
                                info.BlockName = selectedPlayer.DisplayName;
                                info.BlockIcons = new string[] { @"Textures\GUI\Icons\buttons\Character.dds" };
                                info.GridSize = MyCubeSize.Small;
                                info.BlockBuiltBy = 0;
                            }
                            else
                            {
                                var def = (MyCubeBlockDefinition)selectedSlimBlock.BlockDefinition;
                                info.BlockName = def.DisplayNameText;
                                info.BlockIcons = def.Icons;
                                info.GridSize = selectedSlimBlock.CubeGrid.GridSizeEnum;

                                if(prevSlimBlock != selectedSlimBlock)
                                {
                                    prevSlimBlock = selectedSlimBlock;
                                    selectedBlockBuiltBy = selectedSlimBlock.GetObjectBuilder().BuiltBy; // HACK using objectbuilder for built by because there's no other way to get it.
                                }

                                info.BlockBuiltBy = selectedBlockBuiltBy;
                            }

                            info.ShowDetails = false;
                            info.ShowAvailable = true;
                            info.MissingComponentIndex = -1;
                            info.CriticalComponentIndex = 0;
                            info.OwnershipIntegrity = 0f;
                            info.CriticalIntegrity = 0.999f;
                            info.BlockIntegrity = ColorPercentFull(targetColor, paintColor);

                            int ammo = (localHeldTool != null ? localHeldTool.Ammo : 0);

                            info.Components.Clear();
                            info.Components.AddArray(blockInfoLines);

                            int i = blockInfoLines.Length;
                            blockInfoLines[--i].ComponentName = blockInfoStatus[0];

                            var line = blockInfoLines[--i];
                            line.ComponentName = "Target's color:\n  " + ColorMaskToString(targetColor);
                            line.Icons = line.Icons ?? new string[1];
                            line.Icons[0] = (targetCharacter ? @"Textures\GUI\Icons\buttons\Character.dds" : @"Textures\GUI\Icons\Fake.dds");
                            blockInfoLines[i] = line;

                            line = blockInfoLines[--i];
                            line.ComponentName = "Paint: " + ammo + "\n  " + ColorMaskToString(paintColor);
                            line.Icons = line.Icons ?? new string[1];
                            line.Icons[0] = ModContext.ModPath + @"\Textures\Icons\PaintGunMag.dds";
                            blockInfoLines[i] = line;

                            if(blockInfoStatus[1] != null)
                                blockInfoLines[--i].ComponentName = blockInfoStatus[1];
                            else
                                blockInfoLines[--i].ComponentName = "\nKeys & commands: /pg help";
                        }
                    }
                    #endregion
                }

                if(textAPIvisible && hideTextAPI && titleObject != null && titleObject.Visible)
                {
                    titleObject.Visible = false;
                    textObject.Visible = false;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        #region Aimed object info GUI
        public void GenerateAimInfo()
        {
            if(textAPI == null || !textAPI.Heartbeat)
                return;

            if(titleObject == null)
                titleObject = new HudAPIv2.HUDMessage(new StringBuilder(), Vector2D.Zero, Scale: BLOCKINFO_TITLE_SCALE, HideHud: settings.hidePaletteWithHUD, Blend: FOREGROUND_BLEND_TYPE);

            if(textObject == null)
                textObject = new HudAPIv2.HUDMessage(new StringBuilder(), Vector2D.Zero, Scale: BLOCKINFO_TEXT_SCALE, HideHud: settings.hidePaletteWithHUD, Blend: FOREGROUND_BLEND_TYPE);

            bool targetCharacter = (colorPickMode && selectedPlayer != null);
            bool visible = (!MyAPIGateway.Gui.IsCursorVisible && localColorData != null && (targetCharacter || selectedSlimBlock != null));

            titleObject.Visible = visible;
            textObject.Visible = visible;
            textAPIvisible = visible;

            if(!visible)
                return;

            Vector3 targetColor;
            var paintColor = localColorData.Colors[localColorData.SelectedSlot];
            int ammo = (localHeldTool != null ? localHeldTool.Ammo : 0);

            var title = titleObject.Message.Clear().Append("<color=220,244,252>");

            if(targetCharacter)
            {
                targetColor = selectedPlayerColorMask;

                title.Append(selectedPlayer.DisplayName);
            }
            else
            {
                targetColor = selectedSlimBlock.ColorMaskHSV;

                var selectedDef = (MyCubeBlockDefinition)selectedSlimBlock.BlockDefinition;
                title.Append(selectedDef.DisplayNameText);
            }


            var s = textObject.Message;
            s.Clear()
                .Append(blockInfoStatus[0])
                .Append("\n\n<color=220,244,252>");

            if(colorPickMode && selectedPlayer != null)
                s.Append("Player's selected color:");
            else
                s.Append("Block's color:");

            s.Append("\n\n<color=white>        ")
                .Append(ColorMaskToString(targetColor))
                .Append("\n\n<color=220,244,252>");

            if(colorPickMode)
                s.Append("Replace slot: ").Append(localColorData.SelectedSlot + 1);
            else
                s.Append("Paint: ").Append(ammo);

            s.Append("\n\n<color=white>        ")
                .Append(ColorMaskToString(paintColor))
                .Append("\n\n");

            if(blockInfoStatus[1] != null)
                s.Append(blockInfoStatus[1]);
            else
                s.Append("\n<color=gray>Keys & commands: /pg help");

            var hudPos = new Vector2D(BLOCKINFO_POSITION.X, BLOCKINFO_POSITION.Y);
            titleObject.Origin = hudPos + BLOCKINFO_TITLE_OFFSET;
            textObject.Origin = hudPos + BLOCKINFO_TEXT_OFFSET;
        }
        #endregion

        #region Tool update & targeting
        public bool HoldingTool(bool trigger)
        {
            try
            {
                var character = MyAPIGateway.Session.Player.Character;

                selectedGrid = null;
                selectedPlayer = null;
                selectedSlimBlock = null;
                selectedInvalid = false;
                symmetryInputAvailable = false;

                IMyPlayer targetPlayer;
                IMyCubeGrid targetGrid;
                IMySlimBlock targetBlock;

                GetTarget(character, out targetGrid, out targetBlock, out targetPlayer);

                // DEBUG TESTING aim to paint subpart
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
                    if(!playerColorData.ContainsKey(targetPlayer.SteamUserId))
                        return false;

                    var cd = playerColorData[targetPlayer.SteamUserId];
                    var targetColorMask = cd.Colors[cd.SelectedSlot];
                    selectedPlayerColorMask = targetColorMask;
                    selectedPlayer = targetPlayer;

                    if(trigger)
                    {
                        SendToServer_ColorPickMode(false);

                        if(SendToServer_SetColor((byte)localColorData.SelectedSlot, targetColorMask, true))
                        {
                            PlayHudSound(SOUND_HUD_MOUSE_CLICK, 0.25f);

                            ShowNotification(0, $"Slot {localColorData.SelectedSlot + 1} set to {ColorMaskToString(targetColorMask)}", MyFontEnum.White, 3000);
                        }
                        else
                        {
                            PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);
                        }
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

                selectedGrid = targetGrid as MyCubeGrid;

                var colorMask = GetBuildColorMask();
                Vector3 blockColorMask;
                string blockName;

                if(!IsBlockValid(targetBlock, colorMask, trigger, out blockName, out blockColorMask))
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

                #region Painting the target block
                if(trigger)
                {
                    float paintSpeed = (1.0f / GetBlockSurface(selectedSlimBlock));
                    var paintedColor = PaintProcess(blockColorMask, colorMask, paintSpeed, blockName);

                    if(replaceAllMode && ReplaceColorAccess)
                    {
                        SendToServer_ReplaceColor(selectedGrid, selectedSlimBlock.GetColorMask(), paintedColor, replaceGridSystem);
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

                            SendToServer_PaintBlock(selectedGrid, selectedSlimBlock.Position, paintedColor, mirrorPlane, odd);
                        }
                        else
                        {
                            SendToServer_PaintBlock(selectedGrid, selectedSlimBlock.Position, paintedColor);
                        }
                    }

                    return true;
                }
                #endregion
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
        }

        private void GetTarget(IMyCharacter character, out IMyCubeGrid targetGrid, out IMySlimBlock targetBlock, out IMyPlayer targetPlayer)
        {
            targetGrid = null;
            targetBlock = null;
            targetPlayer = null;

            const double RAY_START = 1.6; // a forward offset to allow penetrating blocks by getting closer
            const double RAY_LENGTH = 5;

            var head = character.GetHeadMatrix(false, true);
            var rayFrom = head.Translation + head.Forward * RAY_START;
            var rayTo = head.Translation + head.Forward * (RAY_START + RAY_LENGTH);

            if(colorPickMode)
            {
                players.Clear();
                MyAPIGateway.Players.GetPlayers(players);

                var ray = new RayD(rayFrom, head.Forward);

                foreach(var p in players)
                {
                    if(p.Character == null || p.Character == character)
                        continue;

                    double? dist;
                    p.Character.WorldAABB.Intersects(ref ray, out dist);

                    if(!dist.HasValue || dist.Value > RAY_LENGTH)
                        continue;

                    targetPlayer = p;
                    break;
                }

                players.Clear();

                if(targetPlayer != null)
                    return;
            }

            targetGrid = MyAPIGateway.CubeBuilder.FindClosestGrid();

            if(targetGrid != null)
            {
                var blockPos = targetGrid.RayCastBlocks(rayFrom, rayTo);

                if(blockPos.HasValue)
                    targetBlock = targetGrid.GetCubeBlock(blockPos.Value);
            }
        }

        private bool IsBlockValid(IMySlimBlock block, Vector3 colorMask, bool trigger, out string blockName, out Vector3 blockColor)
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

                blockName = null;
                blockColor = DEFAULT_COLOR;
                return false;
            }

            selectedSlimBlock = block;
            selectedInvalid = false;
            blockColor = block.GetColorMask();
            blockName = (block.FatBlock == null ? block.ToString() : block.FatBlock.DefinitionDisplayNameText);

            #region Symmetry toggle info
            symmetryStatus = null;

            if(!replaceAllMode && ReplaceColorAccess)
            {
                if(selectedGrid.XSymmetryPlane.HasValue || selectedGrid.YSymmetryPlane.HasValue || selectedGrid.ZSymmetryPlane.HasValue)
                {
                    var controlSymmetry = MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY);
                    assigned.Clear();

                    if(controlSymmetry.GetKeyboardControl() != MyKeys.None)
                        assigned.Append(MyAPIGateway.Input.GetKeyName(controlSymmetry.GetKeyboardControl()));

                    if(controlSymmetry.GetSecondKeyboardControl() != MyKeys.None)
                    {
                        if(assigned.Length > 0)
                            assigned.Append(" or ");

                        assigned.Append(MyAPIGateway.Input.GetKeyName(controlSymmetry.GetSecondKeyboardControl()));
                    }

                    if(InputHandler.IsInputReadable())
                    {
                        symmetryInputAvailable = true;

                        if(MyAPIGateway.CubeBuilder.UseSymmetry)
                            symmetryStatus = (GUIUsed ? $"<color=yellow>Symmetry enabled.\n<color=white>Press {assigned} to turn off." : $"Symmetry is ON\n({assigned} to toggle)");
                        else
                            symmetryStatus = (GUIUsed ? $"Symmetry is off.\nPress {assigned} to enable." : $"Symmetry is OFF\n({assigned} to toggle)");
                    }
                    else
                    {
                        if(MyAPIGateway.CubeBuilder.UseSymmetry)
                            symmetryStatus = (GUIUsed ? "<color=yellow>Symmetry enabled." : "Symmetry ON");
                        else
                            symmetryStatus = (GUIUsed ? "Symmetry is off." : "Symmetry OFF");
                    }
                }
                else
                {
                    symmetryStatus = (GUIUsed ? "<color=red>No symmetry on this ship.\n<color=gray>Keys & commands: /pg help" : "No symmetry on this ship.");
                }
            }
            #endregion

            if(colorPickMode)
            {
                if(trigger)
                {
                    SendToServer_ColorPickMode(false);

                    if(SendToServer_SetColor((byte)localColorData.SelectedSlot, blockColor, true))
                    {
                        PlayHudSound(SOUND_HUD_MOUSE_CLICK, 0.25f);
                        ShowNotification(0, $"Slot {localColorData.SelectedSlot + 1} set to {ColorMaskToString(blockColor)}", MyFontEnum.White, 2000);
                    }
                    else
                    {
                        PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);
                    }
                }
                else
                {
                    if(!ColorMaskEquals(blockColor, prevColorMaskPreview))
                    {
                        prevColorMaskPreview = blockColor;
                        SetToolColor(blockColor);

                        if(settings.extraSounds)
                            PlayHudSound(SOUND_HUD_ITEM, 0.75f);
                    }

                    SetGUIToolStatus(0, "Click to get this color.", "lime");
                    SetGUIToolStatus(1, null);
                }

                return false;
            }

            if(!block.CubeGrid.ColorGridOrBlockRequestValidation(MyAPIGateway.Session.Player.IdentityId))
            {
                if(trigger)
                {
                    PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);

                    ShowNotification(0, "Can't paint enemy ships!", MyFontEnum.Red);
                }

                SetGUIToolStatus(0, "Not allied ship.", "red");
                SetGUIToolStatus(1, null);

                return false;
            }

            if(replaceAllMode)
            {
                selectedInvalid = ColorMaskEquals(blockColor, colorMask);

                var assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY));

                if(selectedInvalid)
                    SetGUIToolStatus(0, "Already painted this color.", "red");
                else
                    SetGUIToolStatus(0, "Click to replace color.", "lime");

                SetGUIToolStatus(1, $"Replace mode: {(replaceGridSystem ? "Ship-wide" : "Grid")}\n(Press {assigned} to change)", (replaceGridSystem ? "yellow" : null));

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

            if(ColorMaskEquals(blockColor, colorMask))
            {
                if(symmetry)
                {
                    Vector3I? mirrorX = null;
                    Vector3I? mirrorY = null;
                    Vector3I? mirrorZ = null;
                    Vector3I? mirrorYZ = null;

                    // NOTE: do not optimize, all methods must be called
                    if(!MirrorCheckSameColor(grid, 0, block.Position, colorMask, out mirrorX))
                        symmetrySameColor = false;

                    if(!MirrorCheckSameColor(grid, 1, block.Position, colorMask, out mirrorY))
                        symmetrySameColor = false;

                    if(!MirrorCheckSameColor(grid, 2, block.Position, colorMask, out mirrorZ))
                        symmetrySameColor = false;

                    if(mirrorX.HasValue && grid.YSymmetryPlane.HasValue) // XY
                    {
                        if(!MirrorCheckSameColor(grid, 1, mirrorX.Value, colorMask, out mirrorX))
                            symmetrySameColor = false;
                    }

                    if(mirrorX.HasValue && grid.ZSymmetryPlane.HasValue) // XZ
                    {
                        if(!MirrorCheckSameColor(grid, 2, mirrorX.Value, colorMask, out mirrorX))
                            symmetrySameColor = false;
                    }

                    if(mirrorY.HasValue && grid.ZSymmetryPlane.HasValue) // YZ
                    {
                        if(!MirrorCheckSameColor(grid, 2, mirrorY.Value, colorMask, out mirrorYZ))
                            symmetrySameColor = false;
                    }

                    if(grid.XSymmetryPlane.HasValue && mirrorYZ.HasValue) // XYZ
                    {
                        if(!MirrorCheckSameColor(grid, 0, mirrorYZ.Value, colorMask, out mirrorX))
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

        public Vector3 PaintProcess(Vector3 blockColor, Vector3 colorMask, float paintSpeed, string blockName)
        {
            if(replaceAllMode)
            {
                // notification for this is done in the ReceivedPacket method to avoid re-iterating blocks

                return colorMask;
            }

            if(InstantPaintAccess)
            {
                SetGUIToolStatus(0, "Painted!", "lime");
                SetGUIToolStatus(1, symmetryStatus);
                return colorMask;
            }

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
            else
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

            return blockColor;
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

                        if(titleObject != null)
                        {
                            if(settings.hidePaletteWithHUD)
                            {
                                titleObject.Options |= HudAPIv2.Options.HideHud;
                                textObject.Options |= HudAPIv2.Options.HideHud;
                            }
                            else
                            {
                                titleObject.Options &= ~HudAPIv2.Options.HideHud;
                                textObject.Options &= ~HudAPIv2.Options.HideHud;
                            }
                        }

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
                            prevColorMaskPreview = GetBuildColorMask();
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

                        if(SendToServer_SetColor((byte)localColorData.SelectedSlot, colorMask, true))
                        {
                            PlayHudSound(SOUND_HUD_MOUSE_CLICK, 0.25f);
                            MyVisualScriptLogicProvider.SendChatMessage($"Slot {localColorData.SelectedSlot + 1} set to {ColorMaskToString(colorMask)}", MOD_NAME, 0, MyFontEnum.Green);
                        }
                        else
                        {
                            PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);
                        }

                        return;
                    }

                    var help = new StringBuilder();

                    var assignedLG = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.LANDING_GEAR));
                    var assignedSecondaryClick = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.SECONDARY_TOOL_ACTION));
                    var assignedCubeSize = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE));

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
                    help.Append("Shift+").Append(assignedSecondaryClick).Append('\n');
                    help.Append("  Replaces selected color with aimed block/player's color.").Append('\n');
                    help.Append('\n');
                    help.Append("Shift+").Append(assignedLG).Append('\n');
                    help.Append("  Toggle color picker mode.").Append('\n');
                    help.Append('\n');
                    help.Append("Shift+").Append(assignedCubeSize).Append('\n');
                    help.Append("  (Creative or SM) Toggle replace color mode.").Append('\n');
                    help.Append('\n');
                    help.Append("MouseScroll or ").Append(assignedColorPrev).Append("/").Append(assignedColorNext).Append('\n');
                    help.Append("  Change selected color slot.").Append('\n');
                    help.Append('\n');
                    help.Append("##### Config path #####").Append('\n');
                    help.Append('\n');
                    help.Append("%appdata%/SpaceEngineers/Storage").Append('\n');
                    help.Append("    /").Append(Log.workshopId).Append(".sbm_PaintGun/paintgun.cfg").Append('\n');

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
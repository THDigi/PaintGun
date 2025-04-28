﻿using System;
using System.Collections.Generic;
using Digi.ComponentLib;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Features.Tool
{
    public class LocalToolHandler : ModComponent
    {
        public PaintGunItem LocalTool;

        public IMySlimBlock AimedBlock;
        public IMyPlayer AimedPlayer;
        public PaintMaterial AimedPlayersPaint;

        PaintMaterial? _toolPreviewPaint;
        public PaintMaterial? ToolPreviewPaint
        {
            get { return _toolPreviewPaint; }
            set
            {
                _toolPreviewPaint = value;
                LocalTool.PaintPreviewMaterial = value;
            }
        }

        public SelectionState AimedState;

        public bool SymmetryInputAvailable = false;

        public event ToolEventDelegate LocalToolEquipped;
        public event ToolEventDelegate LocalToolHolstered;
        public delegate void ToolEventDelegate(PaintGunItem item);

        public const int PAINT_UPDATE_TICKS = Constants.TICKS_PER_SECOND / 10; // frequency that tool checks and paints
        public const float PAINT_SPEED = 0.5f;
        public const float DEPAINT_SPEED = 0.6f;
        public const float PAINT_HUE_TOLERANCE = 0.05f; // how much hue (in colormask form) difference can be to allow direct color change, higher than this makes it first remove the color.
        public const double PAINT_DISTANCE = 5;
        public const double PAINT_AIM_START_OFFSET = 2.5; // forward offset of ray start when aiming down sights
        public const float COLOR_EPSILON = 0.000001f;

        List<IHitInfo> hits = new List<IHitInfo>();
        Dictionary<long, DetectionInfo> detections = new Dictionary<long, DetectionInfo>();
        List<MyLineSegmentOverlapResult<MyEntity>> rayOverlapResults = new List<MyLineSegmentOverlapResult<MyEntity>>();

        public LocalToolHandler(PaintGunMod main) : base(main)
        {
            UpdateMethods = UpdateFlags.UPDATE_INPUT;
        }

        protected override void RegisterComponent()
        {
            Main.Palette.LocalInfo.ColorPickModeChanged += ColorPickModeChanged;
            Main.ToolHandler.ToolSpawned += ToolSpawned;
            Main.ToolHandler.ToolRemoved += ToolRemoved;
        }

        protected override void UnregisterComponent()
        {
            if(!IsRegistered)
                return;

            Main.Palette.LocalInfo.ColorPickModeChanged -= ColorPickModeChanged;
            Main.ToolHandler.ToolSpawned -= ToolSpawned;
            Main.ToolHandler.ToolRemoved -= ToolRemoved;
        }

        void ColorPickModeChanged(PlayerInfo pi)
        {
            if(!pi.ColorPickMode)
                ToolPreviewPaint = null;
        }

        void ToolSpawned(PaintGunItem item)
        {
            if(item.OwnerIsLocalPlayer)
            {
                LocalTool = item;
                LocalToolEquipped?.Invoke(item);
            }
        }

        void ToolRemoved(PaintGunItem item)
        {
            if(LocalTool != null && LocalTool == item)
            {
                HandleTool_Holstered();
                LocalToolHolstered?.Invoke(item);
            }
        }

        protected override void UpdateInput(bool anyKeyOrMouse, bool inMenu, bool paused)
        {
            bool controllingLocalChar = (MyAPIGateway.Session.ControlledObject == MyAPIGateway.Session.Player?.Character);
            if(controllingLocalChar && LocalTool != null)
            {
                bool inputReadable = (InputHandler.IsInputReadable() && !MyAPIGateway.Session.IsCameraUserControlledSpectator);
                bool trigger = false;
                if(inputReadable)
                {
                    if(Main.GameConfig.UsingGamepad)
                        trigger = Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(Constants.GamepadBind_Paint)) > 0;
                    else
                        trigger = InputWrapper.IsControlPressed(MyControlsSpace.PRIMARY_TOOL_ACTION);
                }

                if(!Main.CheckPlayerField.Ready)
                {
                    //if(trigger)
                    //    Notifications.Show(0, "Please wait while the game figures out that you exist...", MyFontEnum.Red, 1000);

                    return;
                }

                HandleInputs_Trigger(trigger);
                HandleInputs_Painting(trigger);
            }
        }

        bool triggerInputPressed;
        void HandleInputs_Trigger(bool trigger)
        {
            if(trigger && (LocalTool.Ammo > 0 || !Main.AccessRequiresAmmo(null)))
            {
                if(!triggerInputPressed)
                {
                    triggerInputPressed = true;
                    Spraying(true);
                }
            }
            else if(triggerInputPressed)
            {
                triggerInputPressed = false;
                Spraying(false);
            }
        }

        void Spraying(bool spraying)
        {
            LocalTool.Spraying = spraying;
            Main.NetworkLibHandler.PacketToolSpraying.Send(spraying);
        }

        void HandleInputs_Painting(bool trigger)
        {
            if(Main.Tick % PAINT_UPDATE_TICKS != 0)
                return;

            if(Main.Palette.ReplaceMode && !Main.AccessReplaceColor(null)) // if access no longer allows it, disable the replace mode
            {
                Main.Palette.ReplaceMode = false;
                Main.Notifications.Show(0, "Replace color mode turned off due to loss of access.", MyFontEnum.Red, 2000);
            }

            HandleTool(trigger);
        }

        #region Tool update & targeting
        /// <summary>
        /// Returns True if tool has painted.
        /// </summary>
        void HandleTool(bool trigger)
        {
            AimedPlayer = null;
            AimedBlock = null;
            AimedState = SelectionState.Invalid;
            SymmetryInputAvailable = false;

            IMyCharacter character = MyAPIGateway.Session.Player.Character;
            IMyCubeGrid targetGrid;
            IMySlimBlock targetBlock;
            IMyPlayer targetPlayer;
            Vector3D aimPoint;
            GetTarget(character, out targetGrid, out targetBlock, out targetPlayer, out aimPoint);

            if(targetPlayer != null && Main.Palette.ColorPickMode)
            {
                HandleTool_ColorPickFromPlayer(trigger, targetPlayer);
                return;
            }

            if(targetBlock == null)
            {
                if(Main.Palette.ColorPickMode)
                {
                    Main.Notifications.Show(0, "Aim at a block or player and click to pick color.", MyFontEnum.Blue);
                }
                else if(!Utils.SafeZoneCanPaint(aimPoint, MyAPIGateway.Multiplayer.MyId))
                {
                    // sound likely already played by the shoot restriction in the safe zone
                    //HUDSounds.PlayUnable();

                    if(trigger)
                        Main.Notifications.Show(0, "Can't paint in this safe zone.", MyFontEnum.Red);
                }
                else if(Main.Palette.ReplaceMode)
                {
                    Main.Notifications.Show(0, $"Aim at a block to replace its color on {(Main.Palette.ReplaceShipWide ? "the entire ship" : "this grid")}.", MyFontEnum.Blue);
                }
                else if(trigger)
                {
                    //Main.HUDSounds.PlayUnable();

                    if(LocalTool.Ammo == 0 && Main.AccessRequiresAmmo(null))
                        Main.Notifications.Show(0, "No ammo and no target.", MyFontEnum.Red);
                    else
                        Main.Notifications.Show(0, "Aim at a block to paint it.", MyFontEnum.Red);
                }

                return;
            }

            Main.SelectionGUI.UpdateSymmetryStatus(targetBlock);

            PaintMaterial paintMaterial = Main.Palette.GetLocalPaintMaterial();
            BlockMaterial blockMaterial = new BlockMaterial(targetBlock);
            AimedBlock = targetBlock;

            if(Main.Palette.ColorPickMode)
            {
                HandleTool_ColorPickModeFromBlock(paintMaterial, blockMaterial, trigger);
                return;
            }

            if(!ValidateMainBlock(targetBlock, paintMaterial, blockMaterial, trigger))
                return;

            string blockName = Utils.GetBlockName(targetBlock);

            if(LocalTool.Ammo == 0 && Main.AccessRequiresAmmo(null))
            {
                if(trigger)
                {
                    //Main.HUDSounds.PlayUnable();
                    Main.Notifications.Show(1, "No ammo.", MyFontEnum.Red);
                }

                Main.SelectionGUI.SetGUIStatus(0, "No ammo!", "red");
                return;
            }

            AimedState = SelectionState.Valid;

            if(trigger)
            {
                float paintSpeed = (1.0f / Utils.GetBlockSurface(targetBlock));
                PaintMaterial finalMaterial = HandleTool_PaintProcess(paintMaterial, blockMaterial, paintSpeed, blockName);

                if(Main.Palette.ReplaceMode && Main.AccessReplaceColor(null))
                {
                    Main.Painting.ToolReplacePaint(targetGrid, blockMaterial, finalMaterial, Main.Palette.ReplaceShipWide);
                }
                else
                {
                    bool useMirroring = (Main.AccessSymmetry(null) && MyAPIGateway.CubeBuilder.UseSymmetry);
                    Main.Painting.ToolPaintBlock(targetGrid, targetBlock.Min, finalMaterial, useMirroring);
                }
            }
        }

        void HandleTool_ColorPickFromPlayer(bool trigger, IMyPlayer targetPlayer)
        {
            PlayerInfo pi = Main.Palette.GetPlayerInfo(targetPlayer.SteamUserId);

            if(pi == null)
            {
                Log.Error($"{GetType().Name} :: PlayerInfo for {targetPlayer.DisplayName} ({targetPlayer.SteamUserId.ToString()}) not found!", Log.PRINT_MESSAGE);
                return;
            }

            AimedPlayer = targetPlayer;
            AimedPlayersPaint = pi.GetPaintMaterial();

            if(trigger)
            {
                Main.Palette.ColorPickMode = false;
                Main.Palette.GrabPaletteFromPaint(AimedPlayersPaint, changeApply: true);
                return;
            }

            if(!ToolPreviewPaint.HasValue || !ToolPreviewPaint.Value.PaintEquals(AimedPlayersPaint))
            {
                ToolPreviewPaint = AimedPlayersPaint;
                Main.HUDSounds.PlayItem();
            }

            Main.SelectionGUI.SetGUIStatus(0, "Click to get engineer's selected color.");
            Main.SelectionGUI.SetGUIStatus(1, null);
            return;
        }

        void GetTarget(IMyCharacter character, out IMyCubeGrid targetGrid, out IMySlimBlock targetBlock, out IMyPlayer targetPlayer, out Vector3D aimPoint)
        {
            targetGrid = null;
            targetBlock = null;
            targetPlayer = null;

            bool aiming = Utils.IsAimingDownSights(character);
            MatrixD head = character.GetHeadMatrix(false, true);
            Vector3D rayDir = head.Forward;
            Vector3D rayFrom = head.Translation;

            if(aiming)
                rayFrom += rayDir * PAINT_AIM_START_OFFSET;

            Vector3D rayTo = head.Translation + rayDir * PAINT_DISTANCE;
            aimPoint = rayTo;

            targetGrid = MyAPIGateway.CubeBuilder.FindClosestGrid();

            if(targetGrid == null || targetGrid.Physics == null || !targetGrid.Physics.Enabled)
            {
                targetGrid = null;
                return;
            }

            // older selection behavior when aiming down sights
            if(aiming)
            {
                Vector3I? blockPos = targetGrid.RayCastBlocks(rayFrom, rayTo);

                if(blockPos.HasValue)
                    targetBlock = targetGrid.GetCubeBlock(blockPos.Value);

                return;
            }

            // HACK copied and converted from MyDrillSensorRayCast.ReadEntitiesInRange() + MyCasterComponent.OnWorldPosChanged()
            // last cloned in SE v201
            #region Welder-like block selection
            hits.Clear();
            detections.Clear();
            rayOverlapResults.Clear();

            MyAPIGateway.Physics.CastRay(rayFrom, rayTo, hits, 24);

            foreach(IHitInfo hit in hits)
            {
                IMyEntity parent = hit.HitEntity?.GetTopMostParent();
                if(parent == null || parent.Physics == null || !parent.Physics.Enabled)
                    continue;

                Vector3D hitPos = hit.Position;
                IMyCubeGrid grid = parent as IMyCubeGrid;
                if(grid != null)
                {
                    // just how it's set in game code /shrug
                    hitPos += hit.Normal * -0.005f;
                }

                DetectionInfo detected;
                if(detections.TryGetValue(parent.EntityId, out detected))
                {
                    double dist1 = Vector3D.DistanceSquared(rayFrom, detected.DetectionPoint);
                    double dist2 = Vector3D.DistanceSquared(rayFrom, hitPos);
                    if(dist1 > dist2)
                        detections[parent.EntityId] = new DetectionInfo(parent, hitPos);
                }
                else
                {
                    detections[parent.EntityId] = new DetectionInfo(parent, hitPos);
                }
            }

            hits.Clear();

            LineD line = new LineD(rayFrom, rayTo);
            MyGamePruningStructure.GetAllEntitiesInRay(ref line, rayOverlapResults);

            foreach(MyLineSegmentOverlapResult<MyEntity> result in rayOverlapResults)
            {
                if(result.Element == null)
                    continue;

                IMyCubeBlock block = result.Element as IMyCubeBlock;
                if(block == null)
                    continue;

                MyCubeBlockDefinition def = (MyCubeBlockDefinition)block.SlimBlock.BlockDefinition;
                if(def.HasPhysics)
                    continue; // this section is only for things that are not caught by the raycast

                MyEntity parent = result.Element.GetTopMostParent();
                if(parent.Physics == null || !parent.Physics.Enabled)
                    continue;

                MatrixD invMatrix = block.PositionComp.WorldMatrixNormalizedInv;
                Vector3 localFrom = Vector3D.Transform(rayFrom, ref invMatrix);
                Vector3 localTo = Vector3D.Transform(rayTo, ref invMatrix);

                float? intersectDistance = new Ray(localFrom, Vector3.Normalize(localTo - localFrom)).Intersects(block.PositionComp.LocalAABB) + 0.01f;
                if(intersectDistance.HasValue && intersectDistance <= PAINT_DISTANCE)
                {
                    Vector3D hitPos = rayFrom + rayDir * intersectDistance.Value;
                    DetectionInfo detected;
                    if(detections.TryGetValue(parent.EntityId, out detected))
                    {
                        double dist1 = Vector3D.DistanceSquared(rayFrom, detected.DetectionPoint);
                        double dist2 = Vector3D.DistanceSquared(rayFrom, hitPos);
                        if(dist1 > dist2)
                            detections[parent.EntityId] = new DetectionInfo(parent, hitPos);
                    }
                    else
                    {
                        detections[parent.EntityId] = new DetectionInfo(parent, hitPos);
                    }
                }
            }

            rayOverlapResults.Clear();

            if(detections.Count == 0)
                return;

            double closetDist = double.MaxValue;
            DetectionInfo closestObj = new DetectionInfo(null, Vector3D.Zero);

            foreach(DetectionInfo detected in detections.Values)
            {
                double dist = Vector3D.DistanceSquared(detected.DetectionPoint, rayFrom);
                if(dist < closetDist)
                {
                    closestObj = detected;
                    closetDist = dist;
                }
            }

            detections.Clear();

            targetGrid = closestObj.Entity as IMyCubeGrid;
            if(targetGrid == null)
                return;

            Vector3D localPos = Vector3D.Transform(closestObj.DetectionPoint, targetGrid.WorldMatrixNormalizedInv);
            Vector3I cube;
            targetGrid.FixTargetCube(out cube, localPos / targetGrid.GridSize);
            targetBlock = targetGrid.GetCubeBlock(cube);
            #endregion Welder-like block selection
        }

        bool GetTargetCharacter(Vector3D rayFrom, Vector3D rayDir, double rayLength, IMyCharacter character, ref IMyPlayer targetPlayer)
        {
            List<IMyPlayer> players = Main.Caches.Players.Get();
            MyAPIGateway.Players.GetPlayers(players);

            RayD ray = new RayD(rayFrom, rayDir);

            foreach(IMyPlayer p in players)
            {
                IMyCharacter c = p.Character;
                if(c == null || c == character)
                    continue;

                BoundingSphereD sphere = Utils.GetCharacterSelectionSphere(c);
                if(Vector3D.DistanceSquared(rayFrom, sphere.Center) > (rayLength * rayLength))
                    continue;

                double? dist = sphere.Intersects(ray);
                if(!dist.HasValue || dist.Value > rayLength)
                    continue;

                targetPlayer = p;
                break;
            }

            Main.Caches.Players.Return(players);

            return (targetPlayer != null);
        }

        public bool IsMirrorBlockValid(IMySlimBlock block, PaintMaterial paintMaterial)
        {
            if(block == null)
                return false;

            if(Main.Palette.ColorPickMode)
                return false;

            if(!paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
                return false;

            if(!Utils.AllowedToPaintGrid(block.CubeGrid, MyAPIGateway.Session.Player.IdentityId))
                return false;

            if(!Utils.SafeZoneCanPaint(block, MyAPIGateway.Multiplayer.MyId))
                return false;

            BlockMaterial blockMaterial = new BlockMaterial(block);
            bool materialEquals = paintMaterial.PaintEquals(blockMaterial);

            if(Main.Palette.ReplaceMode)
                return !materialEquals;

            if(Main.CanPaintBlock(block, null) != null)
                return false;

            return !materialEquals;
        }

        bool ValidateMainBlock(IMySlimBlock block, PaintMaterial paintMaterial, BlockMaterial blockMaterial, bool trigger)
        {
            if(!paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
            {
                string assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE));

                Main.Notifications.Show(0, "No paint or skin enabled.", MyFontEnum.Red);
                Main.Notifications.Show(1, $"Press [{assigned}] to toggle color or combined with [Shift] to toggle skin.", MyFontEnum.Debug);

                Main.SelectionGUI.SetGUIStatus(0, "No paint or skin enabled.", "red");
                Main.SelectionGUI.SetGUIStatus(1, null);
                return false;
            }

            if(!Utils.AllowedToPaintGrid(block.CubeGrid, MyAPIGateway.Session.Player.IdentityId))
            {
                if(trigger)
                {
                    Main.HUDSounds.PlayUnable();
                    Main.Notifications.Show(0, "Can't paint enemy ships.", MyFontEnum.Red, 2000);
                }

                Main.SelectionGUI.SetGUIStatus(0, "Not allied ship.", "red");
                Main.SelectionGUI.SetGUIStatus(1, null);
                return false;
            }

            if(!Utils.SafeZoneCanPaint(block, MyAPIGateway.Multiplayer.MyId))
            {
                if(trigger)
                {
                    Main.HUDSounds.PlayUnable();
                    Main.Notifications.Show(0, "Can't paint in this safe zone.", MyFontEnum.Red, 2000);
                }

                Main.SelectionGUI.SetGUIStatus(0, "Protected by safe zone", "red");
                Main.SelectionGUI.SetGUIStatus(1, null);
                return false;
            }

            bool materialEquals = paintMaterial.PaintEquals(blockMaterial);

            if(Main.Palette.ReplaceMode)
            {
                AimedState = (materialEquals ? SelectionState.Invalid : SelectionState.Valid);

                string assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY));

                if(AimedState == SelectionState.Invalid)
                    Main.SelectionGUI.SetGUIStatus(0, "Already this material.", "red");
                else
                    Main.SelectionGUI.SetGUIStatus(0, "Click to replace material.", "lime");

                Main.SelectionGUI.SetGUIStatus(1, $"[{assigned}] {(Main.Palette.ReplaceShipWide ? "<color=yellow>" : "")}Replace mode: {(Main.Palette.ReplaceShipWide ? "Ship-wide" : "Grid")}");

                return (AimedState == SelectionState.Valid);
            }

            string reason = Main.CanPaintBlock(block, null);
            if(reason != null)
            {
                AimedState = SelectionState.Invalid;

                if(trigger)
                {
                    Main.HUDSounds.PlayUnable();
                    Main.Notifications.Show(0, $"Can't paint! Block is {reason}", MyFontEnum.Red);
                }

                Main.SelectionGUI.SetGUIStatus(0, $"Block is {reason}", "red");
                Main.SelectionGUI.SetGUIStatus(1, null);
                return false;
            }

            MyCubeGrid grid = (MyCubeGrid)block.CubeGrid;
            bool symmetry = Main.AccessSymmetry(null) && MyCubeBuilder.Static.UseSymmetry && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue);
            bool symmetrySameColor = true;

            if(materialEquals)
            {
                AimedState = SelectionState.Invalid;

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

                    if(!symmetrySameColor)
                        AimedState = SelectionState.InvalidButMirrorValid;
                }

                if(!symmetry || symmetrySameColor)
                {
                    AimedState = SelectionState.Invalid;

                    if(symmetry)
                        Main.SelectionGUI.SetGUIStatus(0, "All materials match.", "lime");
                    else
                        Main.SelectionGUI.SetGUIStatus(0, "Materials match.", "lime");

                    Main.SelectionGUI.SetGUIStatus(1, Main.SelectionGUI.SymmetryStatusText);
                    return false;
                }
            }

            if(!trigger)
            {
                if(symmetry && !symmetrySameColor)
                    Main.SelectionGUI.SetGUIStatus(0, "Click to update symmetry paint.");
                else if(Main.AccessInstantPaint(null))
                    Main.SelectionGUI.SetGUIStatus(0, "Click to paint.");
                else
                    Main.SelectionGUI.SetGUIStatus(0, "Hold click to paint.");

                Main.SelectionGUI.SetGUIStatus(1, Main.SelectionGUI.SymmetryStatusText);
            }

            return true;
        }

        bool MirrorCheckSameColor(IMyCubeGrid grid, int axis, Vector3I originalPosition, PaintMaterial paintMaterial, out Vector3I? mirrorPosition)
        {
            mirrorPosition = null;

            switch(axis)
            {
                case 0:
                    if(grid.XSymmetryPlane.HasValue)
                        mirrorPosition = originalPosition + new Vector3I(((grid.XSymmetryPlane.Value.X - originalPosition.X) * 2) - (grid.XSymmetryOdd ? 1 : 0), 0, 0);
                    break;

                case 1:
                    if(grid.YSymmetryPlane.HasValue)
                        mirrorPosition = originalPosition + new Vector3I(0, ((grid.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (grid.YSymmetryOdd ? 1 : 0), 0);
                    break;

                case 2:
                    if(grid.ZSymmetryPlane.HasValue)
                        mirrorPosition = originalPosition + new Vector3I(0, 0, ((grid.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (grid.ZSymmetryOdd ? 1 : 0)); // reversed on odd
                    break;
            }

            if(mirrorPosition.HasValue)
            {
                IMySlimBlock slim = grid.GetCubeBlock(mirrorPosition.Value);

                if(slim != null)
                    return paintMaterial.PaintEquals(slim);
            }

            return true;
        }

        void HandleTool_ColorPickModeFromBlock(PaintMaterial paintMaterial, BlockMaterial blockMaterial, bool trigger)
        {
            if(trigger)
            {
                Main.Palette.ColorPickMode = false;
                Main.Palette.GrabPaletteFromPaint(new PaintMaterial(blockMaterial.ColorMask, blockMaterial.Skin));
                return;
            }

            if(!ToolPreviewPaint.HasValue || !ToolPreviewPaint.Value.PaintEquals(blockMaterial))
            {
                ToolPreviewPaint = new PaintMaterial(blockMaterial.ColorMask, blockMaterial.Skin);
                Main.HUDSounds.PlayItem();
            }

            if(paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
                Main.SelectionGUI.SetGUIStatus(0, "Click to get this color.", "lime");
            else if(!paintMaterial.ColorMask.HasValue && paintMaterial.Skin.HasValue)
                Main.SelectionGUI.SetGUIStatus(0, "Click to select this skin.", "lime");
            else
                Main.SelectionGUI.SetGUIStatus(0, "Click to get this material.", "lime");

            Main.SelectionGUI.SetGUIStatus(1, null);
        }

        PaintMaterial HandleTool_PaintProcess(PaintMaterial paintMaterial, BlockMaterial blockMaterial, float paintSpeed, string blockName)
        {
            if(Main.Palette.ReplaceMode)
            {
                // notification for this is done in the ReceivedPacket method to avoid re-iterating blocks

                return paintMaterial;
            }

            if(!paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
            {
                return paintMaterial;
            }

            if(Main.AccessInstantPaint(null))
            {
                Main.SelectionGUI.SetGUIStatus(0, "Painted!", "lime");
                Main.SelectionGUI.SetGUIStatus(1, Main.SelectionGUI.SymmetryStatusText);
                return paintMaterial;
            }

            if(!paintMaterial.ColorMask.HasValue && paintMaterial.Skin.HasValue)
            {
                Main.SelectionGUI.SetGUIStatus(0, "Skinned!", "lime");
                Main.SelectionGUI.SetGUIStatus(1, Main.SelectionGUI.SymmetryStatusText);
                return paintMaterial;
            }

            Vector3 paintColorMask = (paintMaterial.ColorMask.HasValue ? paintMaterial.ColorMask.Value : blockMaterial.ColorMask);
            Vector3 blockColorMask = blockMaterial.ColorMask;

            // If hue is within reason change saturation and value directly.
            if(Math.Abs(blockColorMask.X - paintColorMask.X) < PAINT_HUE_TOLERANCE)
            {
                paintSpeed *= PAINT_SPEED * PAINT_UPDATE_TICKS;
                paintSpeed *= MyAPIGateway.Session.WelderSpeedMultiplier;
                paintSpeed *= Main.ServerSettings.PaintSpeedMultiplier;

                for(int i = 0; i < 3; i++)
                {
                    if(blockColorMask.GetDim(i) > paintColorMask.GetDim(i))
                        blockColorMask.SetDim(i, Math.Max(blockColorMask.GetDim(i) - paintSpeed, paintColorMask.GetDim(i)));
                    else
                        blockColorMask.SetDim(i, Math.Min(blockColorMask.GetDim(i) + paintSpeed, paintColorMask.GetDim(i)));
                }

                if(Utils.ColorMaskEquals(blockColorMask, paintColorMask))
                {
                    blockColorMask = paintColorMask;

                    Main.SelectionGUI.SetGUIStatus(0, "Painting done!", "lime");
                    Main.HUDSounds.PlayColor();
                }
                else
                {
                    int percent = Utils.ColorPercent(blockColorMask, paintColorMask);

                    Main.SelectionGUI.SetGUIStatus(0, $"Painting {percent.ToString()}%...");
                }
            }
            else // if hue is too far off, first "remove" the paint.
            {
                Vector3 defaultColorMask = Main.Palette.DefaultColorMask;

                paintSpeed *= DEPAINT_SPEED * PAINT_UPDATE_TICKS;
                paintSpeed *= MyAPIGateway.Session.GrinderSpeedMultiplier;
                paintSpeed *= Main.ServerSettings.PaintSpeedMultiplier;

                blockColorMask.Y = Math.Max(blockColorMask.Y - paintSpeed, defaultColorMask.Y);
                blockColorMask.Z = (blockColorMask.Z > 0 ? Math.Max(blockColorMask.Z - paintSpeed, defaultColorMask.Z) : Math.Min(blockColorMask.Z + paintSpeed, defaultColorMask.Z));

                // when saturation and value reach the default color, change hue and begin painting
                if(Math.Abs(blockColorMask.Y - defaultColorMask.Y) < COLOR_EPSILON && Math.Abs(blockColorMask.Z - defaultColorMask.Z) < COLOR_EPSILON)
                {
                    blockColorMask.X = paintColorMask.X;
                }

                // block was stripped of color
                if(Utils.ColorMaskEquals(blockColorMask, defaultColorMask))
                {
                    // set the X (hue) to the paint's hue so that the other condition starts painting it saturation&value.
                    blockColorMask = new Vector3(paintColorMask.X, defaultColorMask.Y, defaultColorMask.Z);

                    if(Utils.ColorMaskEquals(paintColorMask, defaultColorMask))
                    {
                        blockColorMask = paintColorMask;
                        Main.SelectionGUI.SetGUIStatus(0, "Removing paint done!");
                    }
                    else
                    {
                        Main.SelectionGUI.SetGUIStatus(0, "Removing paint 100%...");
                    }
                }
                else
                {
                    int percent = Utils.ColorPercent(blockColorMask, defaultColorMask);

                    Main.SelectionGUI.SetGUIStatus(0, $"Removing paint {percent.ToString()}%...");
                }
            }

            return new PaintMaterial(blockColorMask, paintMaterial.Skin);
        }
        #endregion Tool update & targeting

        void HandleTool_Holstered()
        {
            if(Main.Palette.ColorPickMode)
            {
                Main.Palette.ColorPickMode = false;
                Main.Notifications.Show(0, "Color picking cancelled.", MyFontEnum.Debug, 2000);
                Main.HUDSounds.PlayUnable();
            }

            if(Main.Palette.ReplaceMode)
            {
                Main.Palette.ReplaceMode = false;
                Main.Notifications.Show(0, "Replace color mode turned off.", MyFontEnum.Debug, 2000);
            }

            LocalTool = null;
            SymmetryInputAvailable = false;
            AimedBlock = null;
            AimedPlayer = null;
            AimedState = SelectionState.Invalid;
        }
    }
}
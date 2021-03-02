using System;
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
using VRage.Input;
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

                if(_toolPreviewPaint.HasValue)
                {
                    if(_toolPreviewPaint.Value.ColorMask.HasValue)
                        LocalTool.PaintPreviewColorRGB = Utils.ColorMaskToRGB(_toolPreviewPaint.Value.ColorMask.Value);
                    else
                        LocalTool.PaintPreviewColorRGB = Utils.ColorMaskToRGB(Palette.DefaultColorMask);
                }
                else
                {
                    LocalTool.PaintPreviewColorRGB = null;
                }
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
            Palette.LocalInfo.OnColorPickModeChanged += ColorPickModeChanged;
            ToolHandler.ToolSpawned += ToolSpawned;
            ToolHandler.ToolRemoved += ToolRemoved;
        }

        protected override void UnregisterComponent()
        {
            if(!IsRegistered)
                return;

            Palette.LocalInfo.OnColorPickModeChanged -= ColorPickModeChanged;
            ToolHandler.ToolSpawned -= ToolSpawned;
            ToolHandler.ToolRemoved -= ToolRemoved;
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
            if(item.OwnerIsLocalPlayer)
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
                    if(MyAPIGateway.Input.IsJoystickLastUsed)
                        trigger = Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg)) > 0; // right trigger
                    else
                        trigger = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.PRIMARY_TOOL_ACTION);
                }

                if(!CheckPlayerField.Ready)
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
            if(trigger && (Main.IgnoreAmmoConsumption || LocalTool.Ammo > 0))
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
            NetworkLibHandler.PacketToolSpraying.Send(spraying);
        }

        void HandleInputs_Painting(bool trigger)
        {
            if(Main.Tick % PAINT_UPDATE_TICKS != 0)
                return;

            if(Palette.ReplaceMode && !Main.ReplaceColorAccess) // if access no longer allows it, disable the replace mode
            {
                Palette.ReplaceMode = false;
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

            var character = MyAPIGateway.Session.Player.Character;
            IMyCubeGrid targetGrid;
            IMySlimBlock targetBlock;
            IMyPlayer targetPlayer;
            Vector3D aimPoint;
            GetTarget(character, out targetGrid, out targetBlock, out targetPlayer, out aimPoint);

            if(targetPlayer != null && Palette.ColorPickMode)
            {
                HandleTool_ColorPickFromPlayer(trigger, targetPlayer);
                return;
            }

            if(targetBlock == null)
            {
                if(Palette.ColorPickMode)
                {
                    Notifications.Show(0, "Aim at a block or player and click to pick color.", MyFontEnum.Blue);
                }
                else if(!Utils.SafeZoneCanPaint(aimPoint, MyAPIGateway.Multiplayer.MyId))
                {
                    // sound likely already played by the shoot restriction in the safe zone
                    //HUDSounds.PlayUnable();

                    if(trigger)
                        Notifications.Show(0, "Can't paint in this safe zone.", MyFontEnum.Red);
                }
                else if(Palette.ReplaceMode)
                {
                    Notifications.Show(0, $"Aim at a block to replace its color on {(Palette.ReplaceShipWide ? "the entire ship" : "this grid")}.", MyFontEnum.Blue);
                }
                else if(trigger)
                {
                    HUDSounds.PlayUnable();

                    if(!Main.IgnoreAmmoConsumption && LocalTool.Ammo == 0)
                        Notifications.Show(0, "No ammo and no target.", MyFontEnum.Red);
                    else
                        Notifications.Show(0, "Aim at a block to paint it.", MyFontEnum.Red);
                }

                return;
            }

            SelectionGUI.UpdateSymmetryStatus(targetBlock);

            var paintMaterial = Palette.GetLocalPaintMaterial();
            var blockMaterial = new BlockMaterial(targetBlock);
            AimedBlock = targetBlock;

            if(Palette.ColorPickMode)
            {
                HandleTool_ColorPickModeFromBlock(paintMaterial, blockMaterial, trigger);
                return;
            }

            if(!ValidateMainBlock(targetBlock, paintMaterial, blockMaterial, trigger))
                return;

            var blockName = Utils.GetBlockName(targetBlock);

            if(!Main.IgnoreAmmoConsumption && LocalTool.Ammo == 0)
            {
                if(trigger)
                {
                    HUDSounds.PlayUnable();
                    Notifications.Show(1, "No ammo.", MyFontEnum.Red);
                }

                SelectionGUI.SetGUIStatus(0, "No ammo!", "red");
                return;
            }

            AimedState = SelectionState.Valid;

            if(trigger)
            {
                float paintSpeed = (1.0f / Utils.GetBlockSurface(targetBlock));
                var finalMaterial = HandleTool_PaintProcess(paintMaterial, blockMaterial, paintSpeed, blockName);

                if(Palette.ReplaceMode && Main.ReplaceColorAccess)
                {
                    NetworkLibHandler.PacketReplacePaint.Send(targetGrid, blockMaterial, finalMaterial, Palette.ReplaceShipWide);
                }
                else
                {
                    bool useMirroring = (Main.SymmetryAccess && MyAPIGateway.CubeBuilder.UseSymmetry);
                    NetworkLibHandler.PacketPaint.Send(targetGrid, targetBlock.Position, finalMaterial, useMirroring);
                }
            }
        }

        void HandleTool_ColorPickFromPlayer(bool trigger, IMyPlayer targetPlayer)
        {
            var pi = Palette.GetPlayerInfo(targetPlayer.SteamUserId);

            if(pi == null)
            {
                Log.Error($"{GetType().Name} :: PlayerInfo for {targetPlayer.DisplayName} ({targetPlayer.SteamUserId.ToString()}) not found!", Log.PRINT_MESSAGE);
                return;
            }

            AimedPlayer = targetPlayer;
            AimedPlayersPaint = pi.GetPaintMaterial();

            if(trigger)
            {
                Palette.ColorPickMode = false;
                Palette.GrabPaletteFromPaint(AimedPlayersPaint, changeApply: true);
                return;
            }

            if(!ToolPreviewPaint.HasValue || !ToolPreviewPaint.Value.PaintEquals(AimedPlayersPaint))
            {
                ToolPreviewPaint = AimedPlayersPaint;

                if(Settings.extraSounds)
                    HUDSounds.PlayItem();
            }

            SelectionGUI.SetGUIStatus(0, "Click to get engineer's selected color.");
            SelectionGUI.SetGUIStatus(1, null);
            return;
        }

        void GetTarget(IMyCharacter character, out IMyCubeGrid targetGrid, out IMySlimBlock targetBlock, out IMyPlayer targetPlayer, out Vector3D aimPoint)
        {
            targetGrid = null;
            targetBlock = null;
            targetPlayer = null;

            var aiming = Utils.IsAimingDownSights(character);
            var head = character.GetHeadMatrix(false, true);
            var rayDir = head.Forward;
            var rayFrom = head.Translation;

            if(aiming)
                rayFrom += rayDir * PAINT_AIM_START_OFFSET;

            var rayTo = head.Translation + rayDir * PAINT_DISTANCE;
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
                var blockPos = targetGrid.RayCastBlocks(rayFrom, rayTo);

                if(blockPos.HasValue)
                    targetBlock = targetGrid.GetCubeBlock(blockPos.Value);

                return;
            }

            hits.Clear();
            detections.Clear();
            rayOverlapResults.Clear();

            // HACK copied and converted from MyDrillSensorRayCast.ReadEntitiesInRange()
            #region Welder-like block selection
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
            #endregion Welder-like block selection

            targetGrid = closest.Entity as IMyCubeGrid;

            if(targetGrid == null)
                return;

            var localHitPos = Vector3D.Transform(closest.DetectionPoint, targetGrid.WorldMatrixNormalizedInv);
            Vector3I blockGridPos;
            targetGrid.FixTargetCube(out blockGridPos, localHitPos / targetGrid.GridSize);
            targetBlock = targetGrid.GetCubeBlock(blockGridPos);
        }

        bool GetTargetCharacter(Vector3D rayFrom, Vector3D rayDir, double rayLength, IMyCharacter character, ref IMyPlayer targetPlayer)
        {
            var players = Main.Caches.Players.Get();
            MyAPIGateway.Players.GetPlayers(players);

            var ray = new RayD(rayFrom, rayDir);

            foreach(var p in players)
            {
                var c = p.Character;

                if(c == null || c == character)
                    continue;

                var sphere = Utils.GetCharacterSelectionSphere(c);

                if(Vector3D.DistanceSquared(rayFrom, sphere.Center) > (rayLength * rayLength))
                    continue;

                var dist = sphere.Intersects(ray);

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

            if(Palette.ColorPickMode)
                return false;

            if(!paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
                return false;

            if(!Utils.AllowedToPaintGrid(block.CubeGrid, MyAPIGateway.Session.Player.IdentityId))
                return false;

            if(!Utils.SafeZoneCanPaint(block, MyAPIGateway.Multiplayer.MyId))
                return false;

            var blockMaterial = new BlockMaterial(block);
            bool materialEquals = paintMaterial.PaintEquals(blockMaterial);

            if(Palette.ReplaceMode)
                return !materialEquals;

            if(!Main.InstantPaintAccess)
            {
                var def = (MyCubeBlockDefinition)block.BlockDefinition;
                bool built = (block.BuildLevelRatio >= def.CriticalIntegrityRatio);

                if(!built || block.CurrentDamage > (block.MaxIntegrity / 10.0f))
                {
                    return false;
                }
            }

            return !materialEquals;
        }

        bool ValidateMainBlock(IMySlimBlock block, PaintMaterial paintMaterial, BlockMaterial blockMaterial, bool trigger)
        {
            if(!paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
            {
                var assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE));

                Notifications.Show(0, "No paint or skin enabled.", MyFontEnum.Red);
                Notifications.Show(1, $"Press [{assigned}] to toggle color or combined with [Shift] to toggle skin.", MyFontEnum.Debug);

                SelectionGUI.SetGUIStatus(0, "No paint or skin enabled.", "red");
                SelectionGUI.SetGUIStatus(1, null);
                return false;
            }

            if(!Utils.AllowedToPaintGrid(block.CubeGrid, MyAPIGateway.Session.Player.IdentityId))
            {
                if(trigger)
                {
                    HUDSounds.PlayUnable();
                    Notifications.Show(0, "Can't paint enemy ships.", MyFontEnum.Red, 2000);
                }

                SelectionGUI.SetGUIStatus(0, "Not allied ship.", "red");
                SelectionGUI.SetGUIStatus(1, null);
                return false;
            }

            if(!Utils.SafeZoneCanPaint(block, MyAPIGateway.Multiplayer.MyId))
            {
                if(trigger)
                {
                    HUDSounds.PlayUnable();
                    Notifications.Show(0, "Can't paint in this safe zone.", MyFontEnum.Red, 2000);
                }

                SelectionGUI.SetGUIStatus(0, "Protected by safe zone", "red");
                SelectionGUI.SetGUIStatus(1, null);
                return false;
            }

            bool materialEquals = paintMaterial.PaintEquals(blockMaterial);

            if(Palette.ReplaceMode)
            {
                AimedState = (materialEquals ? SelectionState.Invalid : SelectionState.Valid);

                var assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY));

                if(AimedState == SelectionState.Invalid)
                    SelectionGUI.SetGUIStatus(0, "Already this material.", "red");
                else
                    SelectionGUI.SetGUIStatus(0, "Click to replace material.", "lime");

                SelectionGUI.SetGUIStatus(1, $"[{assigned}] {(Palette.ReplaceShipWide ? "<color=yellow>" : "")}Replace mode: {(Palette.ReplaceShipWide ? "Ship-wide" : "Grid")}");

                return (AimedState == SelectionState.Valid);
            }

            if(!Main.InstantPaintAccess)
            {
                var def = (MyCubeBlockDefinition)block.BlockDefinition;
                bool built = (block.BuildLevelRatio >= def.CriticalIntegrityRatio);

                if(!built || block.CurrentDamage > (block.MaxIntegrity / 10.0f))
                {
                    AimedState = SelectionState.Invalid;

                    if(trigger)
                    {
                        HUDSounds.PlayUnable();
                        Notifications.Show(0, "Unfinished blocks can't be painted!", MyFontEnum.Red);
                    }

                    SelectionGUI.SetGUIStatus(0, (!built ? "Block not built" : "Block damaged"), "red");
                    SelectionGUI.SetGUIStatus(1, null);
                    return false;
                }
            }

            var grid = (MyCubeGrid)block.CubeGrid;
            bool symmetry = Main.SymmetryAccess && MyCubeBuilder.Static.UseSymmetry && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue);
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
                        SelectionGUI.SetGUIStatus(0, "Block(s) color match.", "lime");
                    else
                        SelectionGUI.SetGUIStatus(0, "Colors match.", "lime");

                    SelectionGUI.SetGUIStatus(1, SelectionGUI.SymmetryStatusText);
                    return false;
                }
            }

            if(!trigger)
            {
                if(symmetry && !symmetrySameColor)
                    SelectionGUI.SetGUIStatus(0, "Click to update symmetry paint.");
                else if(Main.InstantPaintAccess)
                    SelectionGUI.SetGUIStatus(0, "Click to paint.");
                else
                    SelectionGUI.SetGUIStatus(0, "Hold click to paint.");

                SelectionGUI.SetGUIStatus(1, SelectionGUI.SymmetryStatusText);
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
                var slim = grid.GetCubeBlock(mirrorPosition.Value);

                if(slim != null)
                    return paintMaterial.PaintEquals(slim);
            }

            return true;
        }

        void HandleTool_ColorPickModeFromBlock(PaintMaterial paintMaterial, BlockMaterial blockMaterial, bool trigger)
        {
            if(trigger)
            {
                Palette.ColorPickMode = false;
                Palette.GrabPaletteFromPaint(new PaintMaterial(blockMaterial.ColorMask, blockMaterial.Skin));
                return;
            }

            if(!ToolPreviewPaint.HasValue || !ToolPreviewPaint.Value.PaintEquals(blockMaterial))
            {
                ToolPreviewPaint = new PaintMaterial(blockMaterial.ColorMask, blockMaterial.Skin);

                if(Settings.extraSounds)
                    HUDSounds.PlayItem();
            }

            if(paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
                SelectionGUI.SetGUIStatus(0, "Click to get this color.", "lime");
            else if(!paintMaterial.ColorMask.HasValue && paintMaterial.Skin.HasValue)
                SelectionGUI.SetGUIStatus(0, "Click to select this skin.", "lime");
            else
                SelectionGUI.SetGUIStatus(0, "Click to get this material.", "lime");

            SelectionGUI.SetGUIStatus(1, null);
        }

        PaintMaterial HandleTool_PaintProcess(PaintMaterial paintMaterial, BlockMaterial blockMaterial, float paintSpeed, string blockName)
        {
            if(Palette.ReplaceMode)
            {
                // notification for this is done in the ReceivedPacket method to avoid re-iterating blocks

                return paintMaterial;
            }

            if(!paintMaterial.ColorMask.HasValue && !paintMaterial.Skin.HasValue)
            {
                return paintMaterial;
            }

            if(Main.InstantPaintAccess)
            {
                SelectionGUI.SetGUIStatus(0, "Painted!", "lime");
                SelectionGUI.SetGUIStatus(1, SelectionGUI.SymmetryStatusText);
                return paintMaterial;
            }

            if(!paintMaterial.ColorMask.HasValue && paintMaterial.Skin.HasValue)
            {
                SelectionGUI.SetGUIStatus(0, "Skinned!", "lime");
                SelectionGUI.SetGUIStatus(1, SelectionGUI.SymmetryStatusText);
                return paintMaterial;
            }

            var paintColorMask = (paintMaterial.ColorMask.HasValue ? paintMaterial.ColorMask.Value : blockMaterial.ColorMask);
            var blockColorMask = blockMaterial.ColorMask;

            // If hue is within reason change saturation and value directly.
            if(Math.Abs(blockColorMask.X - paintColorMask.X) < PAINT_HUE_TOLERANCE)
            {
                paintSpeed *= PAINT_SPEED * PAINT_UPDATE_TICKS;
                paintSpeed *= MyAPIGateway.Session.WelderSpeedMultiplier;

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

                    SelectionGUI.SetGUIStatus(0, "Painting done!", "lime");

                    if(Settings.extraSounds)
                        HUDSounds.PlayColor();
                }
                else
                {
                    var percent = Utils.ColorPercent(blockColorMask, paintColorMask);

                    SelectionGUI.SetGUIStatus(0, $"Painting {percent.ToString()}%...");
                }
            }
            else // if hue is too far off, first "remove" the paint.
            {
                var defaultColorMask = Palette.DefaultColorMask;

                paintSpeed *= DEPAINT_SPEED * PAINT_UPDATE_TICKS;
                paintSpeed *= MyAPIGateway.Session.GrinderSpeedMultiplier;

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
                        SelectionGUI.SetGUIStatus(0, "Removing paint done!");
                    }
                    else
                    {
                        SelectionGUI.SetGUIStatus(0, "Removing paint 100%...");
                    }
                }
                else
                {
                    var percent = Utils.ColorPercent(blockColorMask, defaultColorMask);

                    SelectionGUI.SetGUIStatus(0, $"Removing paint {percent.ToString()}%...");
                }
            }

            return new PaintMaterial(blockColorMask, paintMaterial.Skin);
        }
        #endregion Tool update & targeting

        void HandleTool_Holstered()
        {
            if(Palette.ColorPickMode)
            {
                Palette.ColorPickMode = false;
                Notifications.Show(0, "Color picking cancelled.", MyFontEnum.Debug, 2000);
                HUDSounds.PlayUnable();
            }

            if(Palette.ReplaceMode)
            {
                Palette.ReplaceMode = false;
                Notifications.Show(0, "Replace color mode turned off.", MyFontEnum.Debug, 2000);
            }

            LocalTool = null;
            SymmetryInputAvailable = false;
            AimedBlock = null;
            AimedPlayer = null;
            AimedState = SelectionState.Invalid;
        }
    }
}
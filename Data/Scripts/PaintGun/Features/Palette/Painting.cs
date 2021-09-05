using System.Collections.Generic;
using Digi.ComponentLib;
using Digi.PaintGun.Utilities;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun.Features.Palette
{
    public class Painting : ModComponent
    {
        struct CheckData
        {
            public readonly MyStringHash SkinId;
            public readonly int ReadAtTick;

            public CheckData(MyStringHash skinId)
            {
                SkinId = skinId;
                ReadAtTick = PaintGunMod.Instance.Tick + Constants.TICKS_PER_SECOND * 2; // must be constant because of how the queue works
            }
        }

        readonly Dictionary<IMySlimBlock, CheckData> CheckSkinned = new Dictionary<IMySlimBlock, CheckData>();
        readonly List<IMySlimBlock> RemoveKeys = new List<IMySlimBlock>();

        readonly HashSet<IMyCubeGrid> ConnectedGrids = new HashSet<IMyCubeGrid>();

        public Painting(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }

        protected override void UpdateAfterSim(int tick)
        {
            if(tick % 30 != 0)
                return;

            foreach(KeyValuePair<IMySlimBlock, CheckData> kv in CheckSkinned)
            {
                CheckData data = kv.Value;
                if(data.ReadAtTick > tick)
                    continue;

                IMySlimBlock slim = kv.Key;
                if(slim.SkinSubtypeId != data.SkinId)
                {
                    SkinInfo skinInfo = Main.Palette.GetSkinInfo(data.SkinId);
                    string name = skinInfo?.Name ?? $"Unknown:{data.SkinId.String}";
                    Main.Notifications.Show(3, $"[{name}] skin not applied, likely not owned. Consider hiding it in PaintGun's config (Chat then F2).", MyFontEnum.Red, 5000);
                }

                RemoveKeys.Add(slim);
            }

            foreach(IMySlimBlock key in RemoveKeys)
            {
                CheckSkinned.Remove(key);
            }

            RemoveKeys.Clear();

            if(CheckSkinned.Count <= 0)
            {
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);
            }
        }

        public void PaintBlockClient(IMyCubeGrid grid, Vector3I gridPosition, PaintMaterial paint)
        {
            grid.SkinBlocks(gridPosition, gridPosition, paint.ColorMask, paint.Skin?.String);

            // queue a check if skin was applied to alert player
            IMySlimBlock block = grid.GetCubeBlock(gridPosition);
            if(paint.Skin.HasValue)
            {
                CheckSkinned[block] = new CheckData(paint.Skin.Value);
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);
            }
            else
            {
                CheckSkinned.Remove(block);
            }
        }

        public void ReplaceColorInGridClient(IMyCubeGrid selectedGrid, BlockMaterial oldPaint, PaintMaterial paint, bool includeSubgrids)
        {
            //long timeStart = Stopwatch.GetTimestamp();

            ConnectedGrids.Clear();

            if(includeSubgrids)
                MyAPIGateway.GridGroups.GetGroup(selectedGrid, GridLinkTypeEnum.Mechanical, ConnectedGrids);
            else
                ConnectedGrids.Add(selectedGrid);

            bool queueCheckSkin = true;

            //int total = 0;
            int affected = 0;

            foreach(IMyCubeGrid grid in ConnectedGrids)
            {
                MyCubeGrid internalGrid = (MyCubeGrid)grid;
                foreach(IMySlimBlock block in internalGrid.CubeBlocks)
                {
                    BlockMaterial blockMaterial = new BlockMaterial(block);

                    if(paint.ColorMask.HasValue && !Utils.ColorMaskEquals(blockMaterial.ColorMask, oldPaint.ColorMask))
                        continue;

                    if(paint.Skin.HasValue && blockMaterial.Skin != oldPaint.Skin)
                        continue;

                    grid.SkinBlocks(block.Position, block.Position, paint.ColorMask, paint.Skin?.String);

                    if(queueCheckSkin)
                    {
                        queueCheckSkin = false;

                        if(paint.Skin.HasValue)
                            CheckSkinned[block] = new CheckData(paint.Skin.Value);
                        else
                            CheckSkinned.Remove(block);
                    }

                    affected++;
                }

                //total += grid.CubeBlocks.Count;
            }

            if(CheckSkinned.Count > 0)
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);

            //long timeEnd = Stopwatch.GetTimestamp();

            //if(originalSenderSteamId == MyAPIGateway.Multiplayer.MyId)
            {
                Main.Notifications.Show(2, $"Replaced color for {affected.ToString()} blocks.", MyFontEnum.Debug, 5000);

                //double seconds = (timeEnd - timeStart) / (double)Stopwatch.Frequency;

                //if(affected == total)
                //    Main.Notifications.Show(2, $"Replaced color for all {affected.ToString()} blocks in {(seconds * 1000).ToString("0.######")}ms", MyFontEnum.White, 5000);
                //else
                //    Main.Notifications.Show(2, $"Replaced color for {affected.ToString()} of {total.ToString()} blocks in {(seconds * 1000).ToString("0.######")}ms", MyFontEnum.White, 5000);
            }

            ConnectedGrids.Clear();
        }

        #region Symmetry
        public void PaintBlockSymmetryClient(IMyCubeGrid grid, Vector3I gridPosition, PaintMaterial paint, MirrorPlanes mirrorPlanes, OddAxis odd)
        {
            PaintBlockClient(grid, gridPosition, paint);

            bool oddX = (odd & OddAxis.X) == OddAxis.X;
            bool oddY = (odd & OddAxis.Y) == OddAxis.Y;
            bool oddZ = (odd & OddAxis.Z) == OddAxis.Z;

            List<Vector3I> alreadyMirrored = Main.Caches.AlreadyMirrored;
            alreadyMirrored.Clear();

            Vector3I? mirrorX = MirrorPaint(grid, 0, mirrorPlanes, oddX, gridPosition, paint, alreadyMirrored); // X
            Vector3I? mirrorY = MirrorPaint(grid, 1, mirrorPlanes, oddY, gridPosition, paint, alreadyMirrored); // Y
            Vector3I? mirrorZ = MirrorPaint(grid, 2, mirrorPlanes, oddZ, gridPosition, paint, alreadyMirrored); // Z
            Vector3I? mirrorYZ = null;

            if(mirrorX.HasValue && mirrorPlanes.Y > int.MinValue) // XY
                MirrorPaint(grid, 1, mirrorPlanes, oddY, mirrorX.Value, paint, alreadyMirrored);

            if(mirrorX.HasValue && mirrorPlanes.Z > int.MinValue) // XZ
                MirrorPaint(grid, 2, mirrorPlanes, oddZ, mirrorX.Value, paint, alreadyMirrored);

            if(mirrorY.HasValue && mirrorPlanes.Z > int.MinValue) // YZ
                mirrorYZ = MirrorPaint(grid, 2, mirrorPlanes, oddZ, mirrorY.Value, paint, alreadyMirrored);

            if(mirrorPlanes.X > int.MinValue && mirrorYZ.HasValue) // XYZ
                MirrorPaint(grid, 0, mirrorPlanes, oddX, mirrorYZ.Value, paint, alreadyMirrored);
        }

        Vector3I? MirrorPaint(IMyCubeGrid grid, int axis, MirrorPlanes mirrorPlanes, bool odd, Vector3I originalPosition, PaintMaterial paint, List<Vector3I> alreadyMirrored)
        {
            Vector3I? mirrorPosition = null;

            switch(axis)
            {
                case 0:
                    if(mirrorPlanes.X.HasValue)
                        mirrorPosition = originalPosition + new Vector3I(((mirrorPlanes.X.Value - originalPosition.X) * 2) - (odd ? 1 : 0), 0, 0);
                    break;

                case 1:
                    if(mirrorPlanes.Y.HasValue)
                        mirrorPosition = originalPosition + new Vector3I(0, ((mirrorPlanes.Y.Value - originalPosition.Y) * 2) - (odd ? 1 : 0), 0);
                    break;

                case 2:
                    if(mirrorPlanes.Z.HasValue)
                        mirrorPosition = originalPosition + new Vector3I(0, 0, ((mirrorPlanes.Z.Value - originalPosition.Z) * 2) + (odd ? 1 : 0)); // reversed on odd
                    break;
            }

            if(mirrorPosition.HasValue && originalPosition != mirrorPosition.Value && !alreadyMirrored.Contains(mirrorPosition.Value) && grid.CubeExists(mirrorPosition.Value))
            {
                alreadyMirrored.Add(mirrorPosition.Value);
                PaintBlockClient(grid, mirrorPosition.Value, paint);
            }

            return mirrorPosition;
        }
        #endregion Symmetry
    }
}
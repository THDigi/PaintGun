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
                ReadAtTick = PaintGunMod.Instance.Tick + Constants.TICKS_PER_SECOND * 2;
            }
        }

        readonly Dictionary<IMySlimBlock, CheckData> CheckSkinned = new Dictionary<IMySlimBlock, CheckData>();
        readonly List<IMySlimBlock> RemoveCheckKeys = new List<IMySlimBlock>();

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

            // see if skin was applied and warn accordingly.
            foreach(KeyValuePair<IMySlimBlock, CheckData> kv in CheckSkinned)
            {
                IMySlimBlock slim = kv.Key;
                CheckData data = kv.Value;

                if(data.ReadAtTick > tick)
                    continue;

                if(slim.SkinSubtypeId != data.SkinId)
                {
                    SkinInfo skinInfo = Main.Palette.GetSkinInfo(data.SkinId);
                    string name = skinInfo?.Name ?? $"Unknown:{data.SkinId.String}";
                    Main.Notifications.Show(3, $"[{name}] skin not applied, likely not owned. Consider hiding it in PaintGun's config (Chat then F2).", MyFontEnum.Red, 5000);
                }

                RemoveCheckKeys.Add(slim);
            }

            foreach(IMySlimBlock key in RemoveCheckKeys)
            {
                CheckSkinned.Remove(key);
            }

            RemoveCheckKeys.Clear();

            if(CheckSkinned.Count <= 0)
            {
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);
            }
        }

        public void ToolPaintBlock(IMyCubeGrid grid, Vector3I gridPosition, PaintMaterial paint, bool useMirroring)
        {
            if(paint.Skin.HasValue)
            {
                // vanilla DLC-locked skins should use API, mod-added should use the packet to force skin change.
                SkinInfo skin = Main.Palette.GetSkinInfo(paint.Skin.Value);
                if(!skin.AlwaysOwned)
                {
                    if(useMirroring && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue))
                    {
                        MirrorData mirrorData = new MirrorData(grid);
                        PaintBlockSymmetry(true, grid, gridPosition, paint, mirrorData, MyAPIGateway.Multiplayer.MyId);
                        // no ammo consumption, it's creative usage anyway
                    }
                    else
                    {
                        PaintBlock(true, grid, gridPosition, paint, MyAPIGateway.Multiplayer.MyId);
                        Main.NetworkLibHandler.PacketConsumeAmmo.Send();
                    }

                    return;
                }
            }

            // for mod-added skins:
            Main.NetworkLibHandler.PacketPaint.Send(grid, gridPosition, paint, useMirroring);
        }

        public void ToolReplacePaint(IMyCubeGrid grid, BlockMaterial oldPaint, PaintMaterial paint, bool includeSubgrids)
        {
            if(paint.Skin.HasValue)
            {
                // vanilla DLC-locked skins should use API, mod-added should use the packet to force skin change.
                SkinInfo skin = Main.Palette.GetSkinInfo(paint.Skin.Value);
                if(!skin.AlwaysOwned)
                {
                    ReplaceColorInGrid(true, grid, oldPaint, paint, includeSubgrids, MyAPIGateway.Multiplayer.MyId);
                    return;
                }
            }

            // for mod-added skins:
            Main.NetworkLibHandler.PacketReplacePaint.Send(grid, oldPaint, paint, includeSubgrids);
        }

        /// <summary>
        /// <param name="sync"></param> arg determines if it sends the paint request using the API, and automatically checks skin ownership. Must be false for mod-added skins.
        /// </summary>
        public void PaintBlock(bool sync, IMyCubeGrid grid, Vector3I gridPosition, PaintMaterial paint, ulong originalSenderSteamId)
        {
            IMySlimBlock slim = grid.GetCubeBlock(gridPosition);

            if(sync)
            {
                grid.SkinBlocks(gridPosition, gridPosition, paint.ColorMask, paint.Skin?.String);

                if(paint.Skin.HasValue)
                {
                    // check if skin was applied to alert player
                    CheckSkinned[slim] = new CheckData(paint.Skin.Value); // add or replace

                    SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);
                }
            }
            else
            {
                // NOTE getting a MySlimBlock and sending it straight to arguments avoids getting prohibited errors.
                MyCubeGrid gridInternal = (MyCubeGrid)grid;
                gridInternal.ChangeColorAndSkin(gridInternal.GetCubeBlock(gridPosition), paint.ColorMask, paint.Skin);

                if(paint.Skin.HasValue)
                {
                    CheckSkinned.Remove(slim); // prevent alerting if skin gets changed into an always-owned one
                }
            }
        }

        /// <summary>
        /// <param name="sync"></param> arg determines if it sends the paint request using the API, and automatically checks skin ownership. Must be false for mod-added skins.
        /// </summary>
        public void PaintBlockSymmetry(bool sync, IMyCubeGrid grid, Vector3I gridPosition, PaintMaterial paint, MirrorData mirrorData, ulong originalSenderSteamId)
        {
            PaintBlock(sync, grid, gridPosition, paint, originalSenderSteamId);

            List<Vector3I> alreadyMirrored = Main.Caches.AlreadyMirrored;
            alreadyMirrored.Clear();

            Vector3I? mirrorX = MirrorPaint(sync, grid, 0, mirrorData, mirrorData.OddX, gridPosition, paint, alreadyMirrored, originalSenderSteamId); // X
            Vector3I? mirrorY = MirrorPaint(sync, grid, 1, mirrorData, mirrorData.OddY, gridPosition, paint, alreadyMirrored, originalSenderSteamId); // Y
            Vector3I? mirrorZ = MirrorPaint(sync, grid, 2, mirrorData, mirrorData.OddZ, gridPosition, paint, alreadyMirrored, originalSenderSteamId); // Z
            Vector3I? mirrorYZ = null;

            if(mirrorX.HasValue && mirrorData.Y.HasValue) // XY
                MirrorPaint(sync, grid, 1, mirrorData, mirrorData.OddY, mirrorX.Value, paint, alreadyMirrored, originalSenderSteamId);

            if(mirrorX.HasValue && mirrorData.Z.HasValue) // XZ
                MirrorPaint(sync, grid, 2, mirrorData, mirrorData.OddZ, mirrorX.Value, paint, alreadyMirrored, originalSenderSteamId);

            if(mirrorY.HasValue && mirrorData.Z.HasValue) // YZ
                mirrorYZ = MirrorPaint(sync, grid, 2, mirrorData, mirrorData.OddZ, mirrorY.Value, paint, alreadyMirrored, originalSenderSteamId);

            if(mirrorData.X.HasValue && mirrorYZ.HasValue) // XYZ
                MirrorPaint(sync, grid, 0, mirrorData, mirrorData.OddX, mirrorYZ.Value, paint, alreadyMirrored, originalSenderSteamId);
        }

        Vector3I? MirrorPaint(bool sync, IMyCubeGrid grid, int axis, MirrorData mirrorPlanes, bool odd, Vector3I originalPosition, PaintMaterial paint, List<Vector3I> alreadyMirrored, ulong originalSenderSteamId)
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
                PaintBlock(sync, grid, mirrorPosition.Value, paint, originalSenderSteamId);
            }

            return mirrorPosition;
        }

        /// <summary>
        /// <param name="sync"></param> arg determines if it sends the paint request using the API, and automatically checks skin ownership. Must be false for mod-added skins.
        /// </summary>
        public void ReplaceColorInGrid(bool sync, IMyCubeGrid selectedGrid, BlockMaterial oldPaint, PaintMaterial paint, bool includeSubgrids, ulong originalSenderSteamId)
        {
            //long timeStart = Stopwatch.GetTimestamp();

            ConnectedGrids.Clear();

            if(includeSubgrids)
                MyAPIGateway.GridGroups.GetGroup(selectedGrid, GridLinkTypeEnum.Mechanical, ConnectedGrids);
            else
                ConnectedGrids.Add(selectedGrid);

            bool queueCheck = paint.Skin.HasValue; // only care if new paint affects skin

            //int total = 0;
            int affected = 0;

            foreach(IMyCubeGrid grid in ConnectedGrids)
            {
                MyCubeGrid internalGrid = (MyCubeGrid)grid;

                // avoiding GetCubeBlock() lookup by feeding MySlimBlock directly
                // must remain `var` because it's uses a prohibited type in the generic.
                var enumerator = internalGrid.CubeBlocks.GetEnumerator();
                try
                {
                    while(enumerator.MoveNext())
                    {
                        IMySlimBlock block = enumerator.Current;
                        BlockMaterial blockMaterial = new BlockMaterial(block);

                        if(paint.Skin.HasValue && blockMaterial.Skin != oldPaint.Skin)
                            continue;

                        if(paint.ColorMask.HasValue && !Utils.ColorMaskEquals(blockMaterial.ColorMask, oldPaint.ColorMask))
                            continue;

                        if(sync)
                        {
                            grid.SkinBlocks(block.Position, block.Position, paint.ColorMask, paint.Skin?.String);

                            if(queueCheck)
                            {
                                queueCheck = false; // only check first block
                                CheckSkinned[block] = new CheckData(paint.Skin.Value); // replace, in case they swap out skins quickly
                            }
                        }
                        else
                        {
                            internalGrid.ChangeColorAndSkin(enumerator.Current, paint.ColorMask, paint.Skin);

                            if(queueCheck)
                            {
                                queueCheck = false; // only check first block
                                CheckSkinned.Remove(block);
                            }
                        }

                        affected++;
                    }
                }
                finally
                {
                    enumerator.Dispose();
                }

                //total += grid.CubeBlocks.Count;
            }

            if(CheckSkinned.Count > 0)
            {
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);
            }

            //long timeEnd = Stopwatch.GetTimestamp();

            if(originalSenderSteamId == MyAPIGateway.Multiplayer.MyId)
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
    }
}
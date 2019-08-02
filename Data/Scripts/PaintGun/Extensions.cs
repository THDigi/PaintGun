using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace Digi.PaintGun
{
    public static class Extensions
    {
        #region Recursive ship finder
        public static void GetShipSubgrids(this MyCubeGrid grid, HashSet<MyCubeGrid> grids)
        {
            grids.Add(grid);
            GetSubgridsRecursive(grid, grids);
        }

        private static void GetSubgridsRecursive(MyCubeGrid grid, HashSet<MyCubeGrid> grids)
        {
            foreach(var block in grid.GetFatBlocks())
            {
                var g = GetAttachedGrid(block);

                if(g != null && !grids.Contains(g))
                {
                    grids.Add(g);
                    GetSubgridsRecursive(g, grids);
                }
            }
        }

        private static MyCubeGrid GetAttachedGrid(MyCubeBlock block)
        {
            var mechanicalBlock = block as IMyMechanicalConnectionBlock; // includes piston, rotor and suspension bases

            if(mechanicalBlock != null)
                return mechanicalBlock.TopGrid as MyCubeGrid;

            var attachableBlock = block as IMyAttachableTopBlock; // includes rotor tops, piston tops and wheels

            if(attachableBlock != null)
                return attachableBlock.Base?.CubeGrid as MyCubeGrid;

            return null;
        }
        #endregion
    }
}
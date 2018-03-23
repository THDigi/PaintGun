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

        // HACK copied from Sandbox.Game.Entities.MyCubeGrid because it's private
        public static bool ColorGridOrBlockRequestValidation(this IMyCubeGrid grid, long player)
        {
            if(player == 0L || grid.BigOwners.Count == 0)
                return true;

            foreach(long owner in grid.BigOwners)
            {
                var relation = GetRelationsBetweenPlayers(owner, player);

                if(relation == MyRelationsBetweenPlayers.Allies || relation == MyRelationsBetweenPlayers.Self)
                    return true;
            }

            return false;
        }

        // HACK copied from Sandbox.Game.World.MyPlayer because it's not exposed
        private static MyRelationsBetweenPlayers GetRelationsBetweenPlayers(long id1, long id2)
        {
            if(id1 == id2)
                return MyRelationsBetweenPlayers.Self;

            if(id1 == 0L || id2 == 0L)
                return MyRelationsBetweenPlayers.Neutral;

            IMyFaction f1 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(id1);
            IMyFaction f2 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(id2);

            if(f1 == f2)
                return MyRelationsBetweenPlayers.Allies;

            if(f1 == null || f2 == null)
                return MyRelationsBetweenPlayers.Enemies;

            if(MyAPIGateway.Session.Factions.GetRelationBetweenFactions(f1.FactionId, f2.FactionId) == MyRelationsBetweenFactions.Neutral)
                return MyRelationsBetweenPlayers.Neutral;

            return MyRelationsBetweenPlayers.Enemies;
        }
    }
}
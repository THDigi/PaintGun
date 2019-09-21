using System;
using System.Collections.Generic;
using Digi.ComponentLib;
using Digi.PaintGun.Utilities;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Features.SkinOwnershipTest
{
    public class SkinTestServer : ModComponent
    {
        public const string ENT_NAME_PREFIX = "PaintGun_SkinOwnershipTest_";
        public const int TEMP_GRID_EXPIRE = Constants.TICKS_PER_SECOND * 60;
        public const int GRID_CHECK_FREQUENCY = Constants.TICKS_PER_SECOND * 3;

        int waitUntilTick;
        MyDefinitionId? anyBlockDefId = null;
        Dictionary<ulong, GridInfo> tempGrids = new Dictionary<ulong, GridInfo>();
        List<ulong> removeKeys = new List<ulong>();

        public SkinTestServer(PaintGunMod main) : base(main)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception("Why is OwnershipTestServer initialized for MP clients?");
        }

        protected override void RegisterComponent()
        {
            NetworkLibHandler.PacketOwnershipTestResults.OwnedSkinIndexes = new List<int>(Palette.BlockSkins.Count - 1); // index 0 is not being sent.
        }

        protected override void UnregisterComponent()
        {
            tempGrids?.Clear();
        }

        // Step 1 - <SkinTestPlayer.TestForLocalPlayer()> client tells server to spawn a hidden grid for them

        #region Step 2 - Server spawns a hidden grid for specified steamId
        internal void SpawnGrid(ulong steamId)
        {
            var player = Utils.GetPlayerBySteamId(steamId);

            if(player == null)
            {
                Log.Error($"{GetType().Name}.SpawnGrid(): steamId={steamId.ToString()} does not exist!");
                return;
            }

            if(!GetAnyBlockDef())
                return;

            GridInfo gridInfo;
            if(tempGrids.TryGetValue(steamId, out gridInfo))
            {
                tempGrids.Remove(steamId);
                tempGrids.Add(steamId, new GridInfo(gridInfo.Grid, Main.Tick + TEMP_GRID_EXPIRE));

                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);

                gridInfo.Grid.Teleport(MatrixD.CreateWorld(player.GetPosition()));

                if(Constants.OWNERSHIP_TEST_LOGGING)
                    Log.Info($"{GetType().Name}.SpawnGrid() :: already spawned, updating expiry and teleporting to player...");

                return;
            }

            // spawns grid and calls the specified callback when it fully spawns
            MyCubeGrid grid;
            new PaintTestGridSpawner(player, anyBlockDefId.Value, out grid, GridSpawned);

            if(Constants.OWNERSHIP_TEST_LOGGING)
                Log.Info($"{GetType().Name}.SpawnGrid() :: spawning...");
        }

        private void GridSpawned(IMyCubeGrid grid, ulong steamId)
        {
            tempGrids.Add(steamId, new GridInfo(grid, Main.Tick + TEMP_GRID_EXPIRE));

            SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);

            if(Constants.OWNERSHIP_TEST_LOGGING)
                Log.Info($"{GetType().Name}.GridSpawned() :: spawned at {Utils.PrintVector(grid.GetPosition())} and monitoring for paint changes...");
        }
        #endregion Step 2 - Server spawns a hidden grid for specified steamId

        // Step 3 - <SkinTestPlayer.EntityAdded()> - client detects grid spawn then paints and skins it

        #region Step 4 - Server checks grids for paint changes, then checks if skin was applied and tells client the results
        protected override void UpdateAfterSim(int tick)
        {
            if(tick < waitUntilTick)
                return;

            if(tempGrids.Count == 0)
            {
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);
                return;
            }

            //if(Constants.OWNERSHIP_TEST_EXTRA_LOGGING)
            //    Log.Info($"{GetType().Name}.Update() :: got {tempGrids.Count} temporary grids, checking...");

            waitUntilTick = tick + GRID_CHECK_FREQUENCY;

            var blockSkins = Palette.BlockSkins;
            var packetSkinIndexes = NetworkLibHandler.PacketOwnershipTestResults.OwnedSkinIndexes;

            foreach(var kv in tempGrids)
            {
                var steamId = kv.Key;
                var gridInfo = kv.Value;

                // In case that client never paints the grid for some reason, delete it after a while.
                if(gridInfo.ExpiresAtTick <= tick)
                {
                    gridInfo.Grid.Close();
                    removeKeys.Add(steamId);

                    if(Constants.OWNERSHIP_TEST_LOGGING)
                        Log.Info($"{GetType().Name}.Update() - temp grid for {steamId.ToString()} got removed as client script didn't paint it within {(TEMP_GRID_EXPIRE / (float)Constants.TICKS_PER_SECOND).ToString("0.##")} seconds.");
                }
                else
                {
                    packetSkinIndexes.Clear();

                    bool ignore = false;

                    // ignore skin 0 as it's always owned
                    for(int i = 1; i < blockSkins.Count; ++i)
                    {
                        var skin = blockSkins[i];
                        var pos = new Vector3I(i, 0, 0);
                        var block = gridInfo.Grid.GetCubeBlock(pos) as IMySlimBlock;

                        if(block == null)
                        {
                            if(Constants.OWNERSHIP_TEST_LOGGING)
                                Log.Info($"{GetType().Name}.Update() :: grid for {steamId.ToString()} has no blocks, yet...");

                            ignore = true;
                            break;
                        }

                        // block not yet painted, will recheck next cycle
                        if(!Vector3.IsZero(block.ColorMaskHSV, 0.01f))
                        {
                            if(Constants.OWNERSHIP_TEST_LOGGING)
                                Log.Info($"{GetType().Name}.Update() :: grid for {steamId.ToString()} was not painted, yet...");

                            ignore = true;
                            break;
                        }

                        if(block.SkinSubtypeId == skin.SubtypeId)
                            packetSkinIndexes.Add(skin.Index); // skin was applied therefore is owned
                    }

                    if(!ignore)
                    {
                        if(Constants.OWNERSHIP_TEST_LOGGING)
                            Log.Info($"{GetType().Name}.Update() :: grid for {Utils.PrintPlayerName(steamId)} was found painted, sending results...");

                        // grid's done its job, get rid of it
                        gridInfo.Grid.Close();
                        removeKeys.Add(steamId);

                        var pi = Palette.GetOrAddPlayerInfo(steamId);
                        pi.OwnedSkinIndexes = new List<int>(blockSkins.Count);
                        pi.OwnedSkinIndexes.AddRange(packetSkinIndexes);

                        // also add mod-added skins as they are always owned
                        foreach(var skin in blockSkins)
                        {
                            if(skin.Mod != null)
                                pi.OwnedSkinIndexes.Add(skin.Index);
                        }

                        if(Constants.OWNERSHIP_TEST_LOGGING)
                            Log.Info($"... sending skin indexes (count={pi.OwnedSkinIndexes.Count.ToString()}) = {string.Join(", ", pi.OwnedSkinIndexes)}");

                        // tell player their owned skins
                        NetworkLibHandler.PacketOwnershipTestResults.Send(steamId);
                    }
                }
            }

            if(removeKeys.Count > 0)
            {
                foreach(var key in removeKeys)
                {
                    tempGrids.Remove(key);
                }

                if(tempGrids.Count == 0)
                    SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);

                removeKeys.Clear();
            }
        }
        #endregion Step 4 - Server checks grids for paint changes, then checks if skin was applied and tells client the results

        bool GetAnyBlockDef()
        {
            if(anyBlockDefId.HasValue)
                return true;

            foreach(var def in MyDefinitionManager.Static.GetAllDefinitions())
            {
                var blockDef = def as MyCubeBlockDefinition;

                if(blockDef != null && blockDef.Enabled && blockDef.Public && blockDef.CubeSize == MyCubeSize.Small && blockDef.Id.TypeId == typeof(MyObjectBuilder_CubeBlock) && blockDef.IsStandAlone && blockDef.HasPhysics && blockDef.Size == Vector3I.One)
                {
                    anyBlockDefId = blockDef.Id;
                    break;
                }
            }

            if(!anyBlockDefId.HasValue)
            {
                Log.Error($"{GetType().Name} - Couldn't find any decorative block!");
                return false;
            }

            return true;
        }
    }
}
using System;
using System.Collections.Generic;
using Digi.ComponentLib;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun.Features.SkinOwnershipTest
{
    public class SkinTestServer : ModComponent
    {
        public const string ENT_NAME_PREFIX = "PaintGun_SkinOwnershipTest_";
        public const int TEMP_GRID_EXPIRE = Constants.TICKS_PER_SECOND * 60;
        public const int GRID_CHECK_FREQUENCY = Constants.TICKS_PER_SECOND * 3;
        public readonly MyDefinitionId SpawnBlockDefId = new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "PaintGun_TempBlock");

        bool firstSkinAttempt = true;
        int waitUntilTick;
        Dictionary<ulong, GridInfo> tempGrids = new Dictionary<ulong, GridInfo>();
        List<ulong> removeKeys = new List<ulong>();

        public SkinTestServer(PaintGunMod main) : base(main)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception("Why is OwnershipTestServer initialized for MP clients?");

            MyCubeBlockDefinition def;
            if(!MyDefinitionManager.Static.TryGetCubeBlockDefinition(SpawnBlockDefId, out def))
                throw new Exception($"Couldn't find required block: {SpawnBlockDefId.ToString()} - force Steam to redownload the mod.");
        }

        protected override void RegisterComponent()
        {
            Main.NetworkLibHandler.PacketOwnershipTestResults.OwnedSkinIndexes = new List<int>(Main.Palette.BlockSkins.Count - 1); // index 0 is not being sent.
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

            if(firstSkinAttempt)
            {
                firstSkinAttempt = false;
                MyLog.Default.WriteLineAndConsole("### PaintGun: First skin-test grid spawned, if this printed before 'Loaded X Steam Inventory item definitions' then it'll probably fail the test.");
            }

            GridInfo gridInfo;
            if(tempGrids.TryGetValue(steamId, out gridInfo))
            {
                gridInfo = new GridInfo(gridInfo.Grid, Main.Tick + TEMP_GRID_EXPIRE);
                tempGrids[steamId] = gridInfo;

                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);

                gridInfo.Grid.Teleport(MatrixD.CreateWorld(player.GetPosition()));

                if(Constants.OWNERSHIP_TEST_LOGGING)
                    Log.Info($"{GetType().Name}.SpawnGrid() :: already spawned, updating expiry and teleporting to player...");

                return;
            }

            // spawns grid and calls the specified callback when it fully spawns
            MyCubeGrid grid;
            new PaintTestGridSpawner(player, SpawnBlockDefId, out grid, GridSpawned);

            if(Constants.OWNERSHIP_TEST_LOGGING)
                Log.Info($"{GetType().Name}.SpawnGrid() :: spawning for {Utils.PrintPlayerName(steamId)}, gridid={grid.EntityId.ToString()}...");
        }

        private void GridSpawned(IMyCubeGrid grid, ulong steamId)
        {
            GridInfo gridInfo;
            if(tempGrids.TryGetValue(steamId, out gridInfo))
            {
                if(gridInfo.Grid != grid)
                {
                    Log.Error($"{GetType().Name}.GridSpawned() :: got 2 different grids for {Utils.PrintPlayerName(steamId)}: just spawned: {grid.EntityId.ToString()}; was already in dictionary: {gridInfo.Grid.EntityId.ToString()} < deleting this one.", null);
                    gridInfo.Grid.Close();
                }
                else
                {
                    // same grid spawned twice? curious...
                }
            }

            tempGrids[steamId] = new GridInfo(grid, Main.Tick + TEMP_GRID_EXPIRE);

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

            var blockSkins = Main.Palette.BlockSkins;
            var packetSkinIndexes = Main.NetworkLibHandler.PacketOwnershipTestResults.OwnedSkinIndexes;

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
                        Log.Info($"{GetType().Name}.Update() - temp grid for {Utils.PrintPlayerName(steamId)} got removed as client script didn't paint it within {(TEMP_GRID_EXPIRE / (float)Constants.TICKS_PER_SECOND).ToString("0.##")} seconds.");
                }
                else
                {
                    packetSkinIndexes.Clear();

                    bool ignore = false;

                    // skip skin 0 as it's always owned
                    for(int i = 1; i < blockSkins.Count; ++i)
                    {
                        SkinInfo skin = blockSkins[i];
                        Vector3I pos = new Vector3I(i, 0, 0);
                        IMySlimBlock block = gridInfo.Grid.GetCubeBlock(pos);

                        if(block == null)
                        {
                            if(Constants.OWNERSHIP_TEST_LOGGING)
                            {
                                var internalGrid = (MyCubeGrid)gridInfo.Grid;
                                Log.Info($"{GetType().Name}.Update() :: grid for {Utils.PrintPlayerName(steamId)} has no blocks yet?" +
                                         $"\nskin query={skin.SubtypeId.ToString()} ({skin.Index.ToString()}); position={pos.ToString()}; blocks={internalGrid.BlocksCount.ToString()}");
                            }

                            ignore = true;
                            break;
                        }

                        // block not colored and not skinned, therefore recheck next cycle
                        // NOTE: must also check skin because some skins override the color (e.g. Silver)
                        if(block.SkinSubtypeId == MyStringHash.NullOrEmpty && !Vector3.IsZero(block.ColorMaskHSV, 0.01f))
                        {
                            if(Constants.OWNERSHIP_TEST_LOGGING)
                            {
                                Log.Info($"{GetType().Name}.Update() :: grid for {Utils.PrintPlayerName(steamId)} was not painted yet?" +
                                         $"\nskin query={skin.SubtypeId.ToString()} ({skin.Index.ToString()}); position={block.Position.ToString()}; color={block.ColorMaskHSV.ToString()}; skin={block.SkinSubtypeId.ToString()}");
                            }

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

                        var pi = Main.Palette.GetOrAddPlayerInfo(steamId);
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
                        Main.NetworkLibHandler.PacketOwnershipTestResults.Send(steamId);
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
    }
}
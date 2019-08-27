using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.PaintGun.SkinOwnershipTester
{
    public class OwnershipTestServer
    {
        public bool NeedsUpdate = false;

        private PaintGunMod mod;
        private uint waitUntilTick;
        private PacketData packetResult = null;
        private MyDefinitionId? firstBlockDefId = null;
        private readonly Dictionary<ulong, GridInfo> tempGrids = new Dictionary<ulong, GridInfo>();
        private readonly List<ulong> removeKeys = new List<ulong>();

        public const string ENT_NAME_PREFIX = "PaintGun_SkinOwnershipTest_";
        public const uint TEMP_GRID_EXPIRE = 60 * 30;
        public const uint GRID_CHECK_FREQUENCY = 60 * 1;

        private struct GridInfo
        {
            public readonly IMyCubeGrid Grid;
            public readonly uint ExpiresAtTick;

            public GridInfo(IMyCubeGrid grid, uint expiresAtTick)
            {
                Grid = grid;
                ExpiresAtTick = expiresAtTick;
            }
        }

        public OwnershipTestServer(PaintGunMod mod)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception("Why is OwnershipTestServer initialized for non-server?");

            this.mod = mod;

            packetResult = new PacketData()
            {
                Type = PacketAction.SKINTEST_RESULT,
                OwnedSkins = new bool[mod.BlockSkins.Count],
            };
        }

        public void Close()
        {
            tempGrids?.Clear();
        }

        public void ReceivedPacket(PacketData packet)
        {
            if(packet.Type == PacketAction.SKINTEST_REQUEST)
            {
                SpawnGrid(packet.SteamId);
                return;
            }
        }

        // Step 1 - <OwnershipTestPlayer.TestForLocalPlayer()> client tells server to spawn a hidden grid for them

        #region Step 2 - Server spawns a hidden grid for specified steamId
        private void SpawnGrid(ulong steamId)
        {
            var player = mod.GetPlayerBySteamId(steamId);

            if(player == null)
            {
                Log.Error($"{GetType().Name}.SpawnGrind({steamId}): Player steamId does not exist!");
                return;
            }

            #region Find first decorative public block
            if(!firstBlockDefId.HasValue)
            {
                foreach(var def in MyDefinitionManager.Static.GetAllDefinitions())
                {
                    var blockDef = def as MyCubeBlockDefinition;

                    if(blockDef != null && blockDef.Enabled && blockDef.Public && blockDef.CubeSize == MyCubeSize.Small && blockDef.Id.TypeId == typeof(MyObjectBuilder_CubeBlock) && blockDef.IsStandAlone && blockDef.HasPhysics && blockDef.Size == Vector3I.One)
                    {
                        firstBlockDefId = blockDef.Id;
                        break;
                    }
                }

                if(!firstBlockDefId.HasValue)
                {
                    Log.Error($"{GetType().Name}.SpawnGrid({steamId}) - Couldn't find any decorative block!");
                    return;
                }
            }
            #endregion

            GridInfo gridInfo;
            if(tempGrids.TryGetValue(steamId, out gridInfo))
            {
                tempGrids.Remove(steamId);
                tempGrids.Add(steamId, new GridInfo(gridInfo.Grid, mod.tick + TEMP_GRID_EXPIRE));
                NeedsUpdate = true;
                return;
            }

            // spawns grid and calls the specified callback when it fully spawns
            MyCubeGrid grid;
            new PaintTestGridSpawner(player, firstBlockDefId.Value, out grid, GridSpawned);

            if(PaintGunMod.DEBUG)
                Log.Info($"[DEBUG] {GetType().Name}.SpawnGrid() :: spawning...");
        }

        private void GridSpawned(IMyCubeGrid grid, ulong steamId)
        {
            tempGrids.Add(steamId, new GridInfo(grid, mod.tick + TEMP_GRID_EXPIRE));

            NeedsUpdate = true;

            if(PaintGunMod.DEBUG)
                Log.Info($"[DEBUG] {GetType().Name}.GridSpawned() :: spawned and monitoring for paint changes...");
        }
        #endregion

        // Step 3 - <OwnershipTestPlayer.EntityAdded()> - client detects grid spawn then paints and skins it

        #region Step 4 - Server checks grids for paint changes, then checks if skin was applied and tells client the results
        public void Update(uint tick)
        {
            if(tick < waitUntilTick)
                return;

            waitUntilTick = tick + GRID_CHECK_FREQUENCY;

            if(tempGrids.Count == 0)
            {
                NeedsUpdate = false;
                return;
            }

            var blockSkins = mod.BlockSkins;

            foreach(var kv in tempGrids)
            {
                var steamId = kv.Key;
                var gridInfo = kv.Value;

                // In case that client never paints the grid for some reason, delete it after a while.
                if(gridInfo.ExpiresAtTick <= tick)
                {
                    gridInfo.Grid.Close();
                    removeKeys.Add(steamId);

                    Log.Info($"{GetType().Name} - temp grid for {steamId} got removed as client script didn't paint it within {TEMP_GRID_EXPIRE / 60f:0.##} seconds.");
                }
                else
                {
                    if(!gridInfo.Grid.InScene)
                        continue;

                    bool skip = false;

                    // ignore skin 0 as it's always owned
                    for(int i = 1; i < blockSkins.Count; ++i)
                    {
                        var skin = blockSkins[i];
                        var pos = new Vector3I(i, 0, 0);
                        var block = gridInfo.Grid.GetCubeBlock(pos) as IMySlimBlock;

                        if(block == null)
                        {
                            if(PaintGunMod.DEBUG)
                                Log.Info($"[DEBUG] {GetType().Name}.Update() :: grid for {steamId} has no blocks yet...");

                            skip = true;
                            break;
                        }

                        // block not yet painted, will recheck next cycle
                        if(!Vector3.IsZero(block.ColorMaskHSV, 0.01f))
                        {
                            if(PaintGunMod.DEBUG)
                                Log.Info($"[DEBUG] {GetType().Name}.Update() :: grid for {steamId} was not painted yet...");

                            skip = true;
                            break;
                        }

                        packetResult.OwnedSkins[i] = (block.SkinSubtypeId == skin.SubtypeId);
                    }

                    if(!skip)
                    {
                        gridInfo.Grid.Close();
                        removeKeys.Add(steamId);

                        if(tempGrids.Count == 0)
                            NeedsUpdate = false;

                        var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetResult);

                        if(PaintGunMod.DEBUG)
                            Log.Info($"[DEBUG] {GetType().Name}.Update() :: rawData={rawData.Length}:{string.Join(",", rawData)}");

                        MyAPIGateway.Multiplayer.SendMessageTo(PaintGunMod.PACKET, rawData, steamId, true);
                    }
                }
            }

            if(removeKeys.Count > 0)
            {
                foreach(var key in removeKeys)
                {
                    tempGrids.Remove(key);
                }

                removeKeys.Clear();
            }
        }
        #endregion
    }
}
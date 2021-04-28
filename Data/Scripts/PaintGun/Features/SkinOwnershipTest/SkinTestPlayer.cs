using System;
using System.Collections.Generic;
using System.Text;
using Digi.ComponentLib;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Features.SkinOwnershipTest
{
    public class SkinTestPlayer : ModComponent
    {
        public const string STATUS_PREFIX = "SkinOwnershipTest: ";
        public StringBuilder Status = new StringBuilder(128);
        public bool TestInProgress { get; private set; }

        int firstTestAtTick = 0;
        int waitUntilTick = 0;
        int cooldownReTest = 0;
        int testCount = 0;
        MyCubeGrid hiddenGrid;

        const int FIRST_TEST_DELAY = Constants.TICKS_PER_SECOND * 10; // how long to wait after player fully spawned before even starting the first test, hopefully steam inventory loaded by this time
        const int MAX_TEST_TRIES = 3;
        const int RE_TEST_COOLDOWN = SkinTestServer.TEMP_GRID_EXPIRE - (Constants.TICKS_PER_SECOND * 5);
        const int DETECT_PAINT_DELAY = Constants.TICKS_PER_SECOND * 2; // how long to wait until painting the grid after it was streamed
        const int DETECT_PAINT_DELAY_SERVER = Constants.TICKS_PER_SECOND * 1; // same as above but for server/SP

        public SkinTestPlayer(PaintGunMod main) : base(main)
        {
            Status.Clear().Append("Not started");

            if(Main.IsDedicatedServer)
                throw new Exception("Why is OwnershipTestPlayer initialized for DS?!");
        }

        protected override void RegisterComponent()
        {
            UpdateMethods = UpdateFlags.UPDATE_AFTER_SIM;

            MyAPIGateway.Entities.OnEntityAdd += EntityAdded;
        }

        protected override void UnregisterComponent()
        {
            hiddenGrid = null;
            MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;
        }

        #region Step 1 - Client asks server to spawn a grid
        void TestForLocalPlayer()
        {
            TestInProgress = true;
            testCount++;
            cooldownReTest = RE_TEST_COOLDOWN;
            Log.Info($"{GetType().Name}.Update() :: attempting test number {testCount.ToString()}...");

            Status.Clear().Append(STATUS_PREFIX).Append("Starting test #").Append(testCount).Append("...");

            SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);
            Main.NetworkLibHandler.PacketOwnershipTestRequest.Send();
        }
        #endregion Step 1 - Client asks server to spawn a grid

        // Step 2 - <SkinTestServer.SpawnGrid()> server spawns a grid

        #region Step 3 - Client paints the blocks
        private void EntityAdded(IMyEntity ent)
        {
            try
            {
                var grid = ent as IMyCubeGrid;

                if(grid == null || string.IsNullOrEmpty(grid.Name) || !grid.Name.StartsWith(SkinTestServer.ENT_NAME_PREFIX))
                    return;

                //if(Constants.OWNERSHIP_TEST_EXTRA_LOGGING)
                //    Log.Info($"{GetType().Name}.EntityAdded() :: found a temporary grid: {grid.Name}");

                grid.Render.Visible = false;
                grid.Render.CastShadows = false;

                if(grid.Physics != null)
                {
                    grid.Physics.Close();
                    grid.Physics = null;
                }

                // fatblocks need more specific hiding...
                var internalGrid = (MyCubeGrid)grid;
                foreach(var block in internalGrid.GetFatBlocks())
                {
                    block.Render.Visible = false;
                    block.Render.CastShadows = false;
                }

                var steamId = ulong.Parse(grid.Name.Substring(SkinTestServer.ENT_NAME_PREFIX.Length));

                if(steamId != MyAPIGateway.Multiplayer.MyId)
                    return;

                if(Constants.OWNERSHIP_TEST_LOGGING)
                    Log.Info($"{GetType().Name}.EntityAdded() :: found grid for local player... inScene={grid.InScene.ToString()}; phys={(grid.Physics == null ? "null" : grid.Physics.Enabled.ToString())}");

                // paint&skin after a delay
                hiddenGrid = (MyCubeGrid)grid;

                if(MyAPIGateway.Multiplayer.IsServer)
                    waitUntilTick = Main.Tick + DETECT_PAINT_DELAY_SERVER;
                else
                    waitUntilTick = Main.Tick + DETECT_PAINT_DELAY;

                Status.Clear().Append(STATUS_PREFIX).Append("Found grid to paint, waiting ").Append((waitUntilTick - Main.Tick) / Constants.TICKS_PER_SECOND).Append("s...");
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        protected override void UpdateAfterSim(int tick)
        {
            if(cooldownReTest > 0 && --cooldownReTest == 0)
            {
                if(testCount >= MAX_TEST_TRIES)
                {
                    TestInProgress = false;
                    SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);

                    Log.Error($"Ownership test failed after {MAX_TEST_TRIES.ToString()} tries, please reconnect. Bugreport if persists.", Log.PRINT_MESSAGE);

                    Status.Clear().Append(STATUS_PREFIX).Append("Failed after ").Append(MAX_TEST_TRIES).Append(" tries!");

                    Main.NetworkLibHandler.PacketWarningMessage.Send(0, $"Ownership test failed after {MAX_TEST_TRIES.ToString()} tries. Report with server mod log and ask client to submit theirs aswell.");
                    return;
                }
                else if(TestInProgress)
                {
                    TestForLocalPlayer();
                }
            }

            if(!TestInProgress)
            {
                if(firstTestAtTick > 0)
                {
                    if(tick >= firstTestAtTick)
                    {
                        TestForLocalPlayer();
                    }
                    else
                    {
                        int seconds = (int)Math.Round((firstTestAtTick - tick) / (float)Constants.TICKS_PER_SECOND, 0);
                        Status.Clear().Append(STATUS_PREFIX).Append("Starting test in ").Append(seconds).Append("s...");
                    }
                }

                // wait until player has a character so the grid doesn't get spawned who knows where
                if(testCount == 0 && firstTestAtTick == 0 && MyAPIGateway.Session?.Player?.Character != null)
                {
                    firstTestAtTick = tick + FIRST_TEST_DELAY;
                }

                return;
            }

            if(waitUntilTick != 0 && tick >= waitUntilTick)
            {
                waitUntilTick = 0;

                var blockSkins = Main.Palette.BlockSkins;

                for(int i = 1; i < blockSkins.Count; ++i)
                {
                    // Change color too which will signal the server that it was skinned.
                    var color = new Vector3(0, 0, 0);
                    var pos = new Vector3I(i, 0, 0);
                    hiddenGrid.SkinBlocks(pos, pos, color, blockSkins[i].SubtypeId, playSound: false, validateOwnership: true);
                }

                Status.Clear().Append(STATUS_PREFIX).Append("Grid painted! Waiting for results...");

                if(Constants.OWNERSHIP_TEST_LOGGING)
                    Log.Info($"{GetType().Name}.Update() :: grid painted");
            }
        }
        #endregion Step 3 - Client paints the blocks

        // Step 4 - <SkinTestServer.GridPainted()> grid checks blocks for paint change and then skins

        #region Step 5 - received results from server
        internal void GotResults(List<int> ownedSkinIndexes)
        {
            var palette = Main.Palette;

            if(palette == null)
                throw new NullReferenceException("palette = null");

            var blockSkins = palette.BlockSkins;

            if(blockSkins == null)
                throw new NullReferenceException("blockSkins is null");

            if(ownedSkinIndexes == null)
                throw new NullReferenceException("ownedSkinIndexes is null, does that mean no skins are owned?");

            if(Constants.OWNERSHIP_TEST_LOGGING)
                Log.Info($"{GetType().Name}.GotResults() :: got results! owned={ownedSkinIndexes.Count.ToString()}/{(blockSkins.Count - 1).ToString()}; ids={string.Join(", ", ownedSkinIndexes)}");

            foreach(var index in ownedSkinIndexes)
            {
                if(index == 0)
                    continue; // skip default skin if that gets sent for whatever reason

                if(index < 0 || index >= blockSkins.Count)
                {
                    Log.Error($"{GetType().Name}.GotResults() :: index={index.ToString()} out of bounds! min=0, max={(blockSkins.Count - 1).ToString()}", Log.PRINT_MESSAGE);
                    continue;
                }

                blockSkins[index].LocallyOwned = true;
            }

            palette.ComputeOwnedSkins();

            Status.Clear().Append(STATUS_PREFIX).Append("Got results! ").Append(palette.OwnedSkinsCount).Append(" of ").Append(palette.BlockSkins.Count).Append(" skins owned.");

            // change selection if for some reason it was selected
            var selectedSkin = palette.GetSkinInfo(palette.LocalInfo.SelectedSkinIndex);
            if(!selectedSkin.LocallyOwned)
            {
                Main.Notifications.Show(3, $"Skin [{selectedSkin.Name}] not owned, switching to default.", MyFontEnum.Red, 3000);
                palette.LocalInfo.SelectedSkinIndex = 0;
            }

            TestInProgress = false;
            SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);
        }
        #endregion Step 5 - received results from server
    }
}
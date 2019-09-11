using System;
using System.Collections.Generic;
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
        bool testing = false;
        int waitUntilTick = 0;
        int cooldownReTest = RE_TEST_COOLDOWN;
        int testCount = 0;
        MyCubeGrid hiddenGrid;

        const int MAX_TEST_TRIES = 3;
        const int RE_TEST_COOLDOWN = SkinTestServer.TEMP_GRID_EXPIRE - (Constants.TICKS_PER_SECOND * 5);
        const int DETECT_PAINT_DELAY = Constants.TICKS_PER_SECOND * 1; // how long to wait until painting the grid after it was streamed

        public SkinTestPlayer(PaintGunMod main) : base(main)
        {
            if(Main.IsDedicatedServer)
                throw new Exception("Why is OwnershipTestPlayer initialized for DS?!");
        }

        protected override void RegisterComponent()
        {
            Main.CheckPlayerField.PlayerReady += PlayerReady;
            MyAPIGateway.Entities.OnEntityAdd += EntityAdded;
        }

        protected override void UnregisterComponent()
        {
            hiddenGrid = null;
            Main.CheckPlayerField.PlayerReady -= PlayerReady;
            MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;
        }

        #region Step 1 - Client asks server to spawn a grid
        void PlayerReady()
        {
            TestForLocalPlayer();
        }

        void TestForLocalPlayer()
        {
            SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);
            testing = true;
            testCount++;
            NetworkLibHandler.PacketOwnershipTestRequest.Send();
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

                if(grid.Physics != null)
                {
                    grid.Physics.Close();
                    grid.Physics = null;
                }

                var steamId = ulong.Parse(grid.Name.Substring(SkinTestServer.ENT_NAME_PREFIX.Length));

                if(steamId != MyAPIGateway.Multiplayer.MyId)
                    return;

                if(Constants.OWNERSHIP_TEST_EXTRA_LOGGING)
                    Log.Info($"{GetType().Name}.EntityAdded() :: found grid for local player... inScene={grid.InScene}; phys={(grid.Physics == null ? "null" : grid.Physics.Enabled.ToString())}");

                // paint&skin after a delay
                hiddenGrid = (MyCubeGrid)grid;
                waitUntilTick = Main.Tick + DETECT_PAINT_DELAY;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        protected override void UpdateAfterSim(int tick)
        {
            if(!testing)
                throw new Exception("why's this still updating?!");

            if(cooldownReTest > 0 && --cooldownReTest == 0)
            {
                if(testCount >= MAX_TEST_TRIES)
                {
                    Log.Error($"Ownership test failed after {MAX_TEST_TRIES} tries, please reconnect. Bugreport if persists.", Log.PRINT_MSG);
                    SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);
                    testing = false;
                    return;
                }

                Log.Info($"{GetType().Name}.Update() :: attempting test again...");
                TestForLocalPlayer();

                cooldownReTest = RE_TEST_COOLDOWN;
            }

            if(waitUntilTick != 0 && tick >= waitUntilTick)
            {
                waitUntilTick = 0;

                var blockSkins = Palette.BlockSkins;

                for(int i = 1; i < blockSkins.Count; ++i)
                {
                    // Change color too which will signal the server that it was skinned.
                    var color = new Vector3(0, 0, 0);
                    var pos = new Vector3I(i, 0, 0);
                    hiddenGrid.SkinBlocks(pos, pos, color, blockSkins[i].SubtypeId, playSound: false, validateOwnership: true);
                }

                if(Constants.OWNERSHIP_TEST_EXTRA_LOGGING)
                    Log.Info($"{GetType().Name}.Update() :: grid painted");
            }
        }
        #endregion Step 3 - Client paints the blocks

        // Step 4 - <SkinTestServer.GridPainted()> grid checks blocks for paint change and then skins

        #region Step 5 - received results from server
        internal void GotResults(List<int> ownedSkinIndexes)
        {
            var palette = Main.Palette;
            var blockSkins = palette.BlockSkins;
            Palette.OwnedSkins = 0;

            if(Constants.OWNERSHIP_TEST_EXTRA_LOGGING)
                Log.Info($"{GetType().Name}.GotResults() :: got results! owned={ownedSkinIndexes.Count}/{palette.BlockSkins.Count - 1}; ids={string.Join(", ", ownedSkinIndexes)}");

            foreach(var index in ownedSkinIndexes)
            {
                if(index == 0)
                    continue;

                blockSkins[index].LocallyOwned = true;
                palette.OwnedSkins++;
            }

            var selectedSkin = palette.GetSkinInfo(palette.LocalInfo.SelectedSkinIndex);

            if(!selectedSkin.LocallyOwned)
            {
                Notifications.Show(3, $"Skin [{selectedSkin.Name}] not owned, switching to default.", MyFontEnum.Red, 3000);
                palette.LocalInfo.SelectedSkinIndex = 0;
            }

            testing = false;
            SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);
        }
        #endregion Step 5 - received results from server
    }
}
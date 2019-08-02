using System;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Digi.PaintGun.SkinOwnershipTester
{
    public class OwnershipTestPlayer
    {
        public bool NeedsUpdate = false;

        private PaintGunMod mod;
        private bool testing = false;
        private uint waitUntilTick = 0;
        private uint cooldownReTest = OwnershipTestServer.TEMP_GRID_EXPIRE;
        private MyCubeGrid hiddenGrid;

        public OwnershipTestPlayer(PaintGunMod mod)
        {
            this.mod = mod;

            if(mod.isDS)
                throw new Exception("Why is OwnershipTestPlayer initialized for DS?!");

            MyAPIGateway.Entities.OnEntityAdd += EntityAdded;
        }

        public void Close()
        {
            hiddenGrid = null;
            MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;
        }

        public void ReceivedPacket(PacketData packet)
        {
            if(packet.Type == PacketAction.SKINTEST_RESULT)
            {
                GotResults(packet.OwnedSkins);
            }
        }

        #region Step 1 - Client asks server to spawn a grid
        public void TestForLocalPlayer()
        {
            NeedsUpdate = true;
            testing = true;

            var packet = new PacketData()
            {
                SteamId = MyAPIGateway.Multiplayer.MyId,
                Type = PacketAction.SKINTEST_REQUEST,
            };

            var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

            if(PaintGunMod.DEBUG)
                Log.Info($"{GetType().Name}.TestForLocalPlayer({packet.SteamId}) :: bytes={bytes.Length}:{string.Join(",", bytes)}");

            MyAPIGateway.Multiplayer.SendMessageToServer(PaintGunMod.PACKET, bytes, true);
        }
        #endregion

        // Step 2 - <OwnershipTestServer.SpawnGrid()> server spawns a grid

        #region Step 3 - Client paints the blocks 
        private void EntityAdded(IMyEntity ent)
        {
            try
            {
                var grid = ent as IMyCubeGrid;

                if(grid == null || string.IsNullOrEmpty(grid.Name) || !grid.Name.StartsWith(OwnershipTestServer.ENT_NAME_PREFIX))
                    return;

                if(PaintGunMod.DEBUG)
                    Log.Info($"[DEBUG] {GetType().Name}.EntityAdded() :: found temporary grid: {grid.Name}");

                grid.Render.Visible = false;

                var steamId = ulong.Parse(grid.Name.Substring(OwnershipTestServer.ENT_NAME_PREFIX.Length));

                if(steamId != MyAPIGateway.Multiplayer.MyId)
                    return;

                if(PaintGunMod.DEBUG)
                    Log.Info($"[DEBUG] {GetType().Name}.EntityAdded() :: found grid for local player... inScene={grid.InScene}; phys={(grid.Physics == null ? "null" : grid.Physics.Enabled.ToString())}");

                if(grid.Physics != null)
                {
                    grid.Physics.Close();
                    grid.Physics = null;
                }

                // paint&skin after a delay
                hiddenGrid = (MyCubeGrid)grid;
                waitUntilTick = mod.tick + (60 * 1);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void Update(uint tick)
        {
            if(testing && cooldownReTest > 0 && --cooldownReTest == 0)
            {
                Log.Info($"{GetType().Name}.Update() :: attempting test one more time...");
                TestForLocalPlayer();
            }

            if(waitUntilTick != 0 && tick >= waitUntilTick)
            {
                NeedsUpdate = false;
                waitUntilTick = 0;

                var blockSkins = mod.BlockSkins;

                for(int i = 1; i < blockSkins.Count; ++i)
                {
                    // Chance color too which will signal the server that it was skinned.
                    var pos = new Vector3I(i, 0, 0);
                    hiddenGrid.SkinBlocks(pos, pos, Vector3.Zero, blockSkins[i].SubtypeId, playSound: false, validateOwnership: true);
                }

                if(PaintGunMod.DEBUG)
                    Log.Info($"[DEBUG] {GetType().Name}.Update() :: grid painted");
            }
        }
        #endregion

        // Step 4 - <OwnershipTestServer.GridPainted()> grid checks blocks for paint change and then skins

        #region Step 5 - received results from server
        private void GotResults(bool[] ownedSkins)
        {
            var blockSkins = mod.BlockSkins;

            for(int i = 1; i < blockSkins.Count; ++i)
            {
                var skin = blockSkins[i];
                skin.LocallyOwned = ownedSkins[i];
            }

            var selectedSkin = blockSkins[mod.localColorData.SelectedSkinIndex];

            if(!selectedSkin.LocallyOwned)
            {
                MyAPIGateway.Utilities.ShowNotification($"Skin {selectedSkin.Name} not owned, switching to default.", 4000, MyFontEnum.White);
                mod.localColorData.SelectedSkinIndex = 0;
            }

            testing = false;
            cooldownReTest = 0;
        }
        #endregion
    }
}

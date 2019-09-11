using System;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Digi.PaintGun.Features.SkinOwnershipTest
{
    public struct PaintTestGridSpawner
    {
        private readonly ulong steamId;
        private readonly Action<IMyCubeGrid, ulong> callback;

        public PaintTestGridSpawner(IMyPlayer player, MyDefinitionId blockDefId, out MyCubeGrid grid, Action<IMyCubeGrid, ulong> callback = null)
        {
            steamId = player.SteamUserId;
            this.callback = callback;

            var spawnPos = player.GetPosition(); // + Vector3D.Up * (MyAPIGateway.Session.SessionSettings.SyncDistance * 0.25);

            var gridObj = new MyObjectBuilder_CubeGrid()
            {
                CreatePhysics = false,
                GridSizeEnum = MyCubeSize.Small,
                PositionAndOrientation = new MyPositionAndOrientation(spawnPos, Vector3.Forward, Vector3.Up),
                PersistentFlags = MyPersistentEntityFlags2.InScene,
                IsStatic = true,
                Editable = true,
                DestructibleBlocks = true,
                IsRespawnGrid = false,
                Name = SkinTestServer.ENT_NAME_PREFIX + steamId,
            };

            var blockSkins = PaintGunMod.Instance.Palette.BlockSkins;

            // ignore skin 0 as it's always owned
            for(int i = 1; i < blockSkins.Count; ++i)
            {
                var blockObj = (MyObjectBuilder_CubeBlock)MyObjectBuilderSerializer.CreateNewObject(blockDefId);

                blockObj.BuiltBy = player.IdentityId;
                blockObj.Owner = player.IdentityId;
                blockObj.BuildPercent = 1f;
                blockObj.IntegrityPercent = 1f;
                blockObj.Min = new SerializableVector3I(i, 0, 0);
                blockObj.ColorMaskHSV = new SerializableVector3(1, 1, 1);
                blockObj.BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Forward, Base6Directions.Direction.Up);

                gridObj.CubeBlocks.Add(blockObj);
            }

            MyAPIGateway.Entities.RemapObjectBuilder(gridObj);

            grid = (MyCubeGrid)MyAPIGateway.Entities.CreateFromObjectBuilderParallel(gridObj, true, EntitySpawned);
            //grid = (MyCubeGrid)MyAPIGateway.Entities.CreateFromObjectBuilderParallel(gridObj, true);

            if(grid == null)
            {
                Log.Error($"SkinOwnershipTester.SpawnGrind({steamId}): spawned grid turned out null");
                return;
            }

            //grid.AddedToScene += EntityAddedToScene;

            grid.IsPreview = false; // needs to be synchronized
            grid.Save = false;
            grid.Render.Visible = false;
        }

        //private void EntityAddedToScene(MyEntity ent)
        //{
        //    var grid = (IMyCubeGrid)ent;
        //    callback?.Invoke(grid, steamId);
        //}

        private void EntitySpawned(IMyEntity ent)
        {
            var grid = (IMyCubeGrid)ent;
            callback?.Invoke(grid, steamId);
        }
    }
}
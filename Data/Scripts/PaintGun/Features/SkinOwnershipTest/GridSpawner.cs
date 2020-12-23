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

            var spawnPos = player.GetPosition();

            if(Vector3.IsZero(spawnPos, 0.01f))
            {
                var controlled = player.Controller?.ControlledEntity?.Entity;

                if(controlled != null)
                    spawnPos = controlled.GetPosition();
                else
                    Log.Error($"{GetType().Name} :: {player.DisplayName} ({player.SteamUserId.ToString()}) has GetPosition() zero and couldn't get controlled entity to get position.");
            }

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

            // Name needed to be set after RemapObjectBuilder() because it overwrites Name.
            gridObj.Name = SkinTestServer.ENT_NAME_PREFIX + steamId.ToString();

            // FIXME: Struct instance method being used for delegate creation, this will result in a boxing instruction
            grid = (MyCubeGrid)MyAPIGateway.Entities.CreateFromObjectBuilderParallel(gridObj, true, EntitySpawned);
            //grid = (MyCubeGrid)MyAPIGateway.Entities.CreateFromObjectBuilderParallel(gridObj, true);

            if(grid == null)
            {
                Log.Error($"SkinOwnershipTester.SpawnGrind({steamId.ToString()}): spawned grid turned out null");
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

            // fatblocks need more specific hiding...
            var internalGrid = (MyCubeGrid)grid;
            foreach(var block in internalGrid.GetFatBlocks())
            {
                block.Render.Visible = false;
            }
        }
    }
}
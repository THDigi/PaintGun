using Digi.NetworkLib;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketPaint : PacketBase
    {
        [ProtoMember(1)]
        private long GridEntId;

        [ProtoMember(2)]
        private Vector3I GridPosition;

        [ProtoMember(3)]
        private SerializedPaintMaterial Paint;

        [ProtoMember(4)]
        private MirrorPlanes MirrorPlanes;

        [ProtoMember(5)]
        private OddAxis OddAxis;

        [ProtoMember(100)]
        private bool Reverted;

        public PacketPaint() { } // Empty constructor required for deserialization

        public void Send(IMyCubeGrid grid, Vector3I gridPosition, PaintMaterial paint, bool useMirroring)
        {
            Reverted = false;
            GridEntId = grid.EntityId;
            GridPosition = gridPosition;
            Paint = new SerializedPaintMaterial(paint);
            MirrorPlanes = default(MirrorPlanes);
            OddAxis = OddAxis.NONE;

            if(useMirroring && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue))
            {
                MirrorPlanes = new MirrorPlanes(grid);

                if(grid.XSymmetryOdd)
                    OddAxis |= OddAxis.X;
                if(grid.YSymmetryOdd)
                    OddAxis |= OddAxis.Y;
                if(grid.ZSymmetryOdd)
                    OddAxis |= OddAxis.Z;
            }

            Network.SendToServer(this);

            // do the action locally as well
            DoAction(grid);
        }

        public override void Received(ref bool relay)
        {
            var grid = Utils.GetEntityOrError<MyCubeGrid>(this, GridEntId, Constants.NETWORK_DESYNC_ERROR_LOGGING);
            if(grid == null)
                return;

            // ensure server side if safezone permissions are respected
            if(Main.IsServer && !Utils.SafeZoneCanPaint(grid.GetCubeBlock(GridPosition), SteamId))
            {
                if(Constants.NETWORK_DESYNC_ERROR_LOGGING)
                {
                    var block = (IMySlimBlock)grid.GetCubeBlock(GridPosition);
                    Log.Error($"{GetType().Name} :: Can't paint inside no-build safe zone! Sender={SteamId.ToString()}; Grid={grid} ({grid.EntityId.ToString()}); block={block.BlockDefinition.Id.ToString()} ({block.Position.ToString()})", Log.PRINT_MESSAGE);
                }
                return;
            }

            var identity = MyAPIGateway.Players.TryGetIdentityId(SteamId);

            if(!Utils.AllowedToPaintGrid(grid, identity))
            {
                if(Constants.NETWORK_DESYNC_ERROR_LOGGING)
                    Log.Error($"{GetType().Name} :: Can't paint non-allied grids! Sender={SteamId.ToString()}; Grid={grid} ({grid.EntityId.ToString()})", Log.PRINT_MESSAGE);
                return;
            }

            if(!grid.CubeExists(GridPosition))
            {
                if(Constants.NETWORK_DESYNC_ERROR_LOGGING)
                    Log.Error($"{GetType().Name} :: Can't paint inexistent blocks! Sender={SteamId.ToString()}; Grid={grid} ({grid.EntityId.ToString()}) at GridPosition={GridPosition.ToString()}", Log.PRINT_MESSAGE);
                return;
            }

            if(Reverted)
            {
                relay = false;
                DoAction(grid);
                return;
            }

            // sent by server to itself, ignore action but do the relaying
            if(Main.IsServer && SteamId == MyAPIGateway.Multiplayer.MyId)
            {
                relay = true;
                return;
            }

            if(DoAction(grid))
                relay = true;
        }

        bool DoAction(IMyCubeGrid grid)
        {
            if(!Utils.ValidateSkinOwnership(SteamId, Paint))
            {
                Paint = new SerializedPaintMaterial(Paint.ColorMaskPacked, null);
            }

            var paint = new PaintMaterial(Paint);

            if(MirrorPlanes.HasMirroring)
                Main.Painting.PaintBlockSymmetry(grid, GridPosition, paint, MirrorPlanes, OddAxis, SteamId);
            else
                Main.Painting.PaintBlock(grid, GridPosition, paint, SteamId);

            if(Main.IsServer && !Main.IgnoreAmmoConsumption) // ammo consumption, only needed server side
            {
                var inv = Utils.GetCharacterInventoryOrError(this, Utils.GetCharacterOrError(this, Utils.GetPlayerOrError(this, SteamId)));

                if(inv != null)
                    inv.RemoveItemsOfType(1, Main.Constants.PAINT_MAG_ITEM, false);
            }

            return true;
        }
    }
}
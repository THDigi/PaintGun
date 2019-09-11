using Digi.NetworkLib;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract]
    public class PacketReplacePaint : PacketBase
    {
        [ProtoMember(1)]
        long GridEntId;

        [ProtoMember(2)]
        SerializedBlockMaterial OldPaint;

        [ProtoMember(3)]
        SerializedPaintMaterial NewPaint;

        [ProtoMember(4)]
        bool IncludeSubgrids;

        public PacketReplacePaint() { } // Empty constructor required for deserialization

        public void Send(IMyCubeGrid grid, BlockMaterial oldPaint, PaintMaterial newPaint, bool includeSubgrids)
        {
            GridEntId = grid.EntityId;
            OldPaint = new SerializedBlockMaterial(oldPaint);
            NewPaint = new SerializedPaintMaterial(newPaint);
            IncludeSubgrids = includeSubgrids;

            Network.SendToServer(this);

            // do the action locally as well
            Main.Painting.ReplaceColorInGrid(grid, oldPaint, newPaint, includeSubgrids, SteamId);
        }

        public override void Received(ref bool relay)
        {
            // TODO: check access when creative tools is properly exposed for server side checking

            if(!Utils.ValidateSkinOwnership(SteamId, NewPaint))
            {
                NewPaint = new SerializedPaintMaterial(NewPaint.ColorMaskPacked, null);
            }

            var grid = Utils.GetEntityOrError<MyCubeGrid>(this, GridEntId, Constants.NETWORK_EXTRA_LOGGING);
            if(grid == null)
                return;

            // ensure server side if safezone permissions are respected
            if(MyAPIGateway.Session.IsServer && !Utils.SafeZoneCanPaint(grid, SteamId))
                return;

            var identity = MyAPIGateway.Players.TryGetIdentityId(SteamId);

            if(!Utils.AllowedToPaintGrid(grid, identity))
            {
                if(Constants.NETWORK_EXTRA_LOGGING)
                    Log.Error($"Can't paint unallied grids; packet={this}", Log.PRINT_MESSAGE);
                return;
            }

            // sent by server to itself, ignore action but do the relaying
            if(MyAPIGateway.Multiplayer.IsServer && SteamId == MyAPIGateway.Multiplayer.MyId)
            {
                relay = true;
                return;
            }

            var newPaint = new PaintMaterial(NewPaint);
            var oldPaint = new BlockMaterial(OldPaint);

            Main.Painting.ReplaceColorInGrid(grid, oldPaint, newPaint, IncludeSubgrids, SteamId);
            relay = true;
        }
    }
}
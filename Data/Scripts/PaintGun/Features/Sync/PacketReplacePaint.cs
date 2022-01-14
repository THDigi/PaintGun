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

            // do the action for local client too
            if(!MyAPIGateway.Session.IsServer)
            {
                Main.Painting.ReplaceColorInGrid(false, grid, oldPaint, newPaint, includeSubgrids, OriginalSenderSteamId);
            }
        }

        public override void Received(ref RelayMode relay)
        {
            // TODO: check access when creative tools is properly exposed for server side checking

            //if(!Utils.ValidateSkinOwnership(SteamId, NewPaint))
            //{
            //    Main.NetworkLibHandler.PacketWarningMessage.Send(SteamId, $"Failed to replace skin server side, skin {Utils.PrintSkinName(NewPaint.SkinIndex)} not owned.");
            //    NewPaint = new SerializedPaintMaterial(NewPaint.ColorMaskPacked, null);
            //}

            MyCubeGrid grid = Utils.GetEntityOrError<MyCubeGrid>(this, GridEntId, Constants.NETWORK_DESYNC_ERROR_LOGGING);
            if(grid == null)
            {
                if(Main.IsServer)
                    Main.NetworkLibHandler.PacketWarningMessage.Send(OriginalSenderSteamId, "Failed to replace paint server side, grid no longer exists.");

                return;
            }

            if(Main.IsServer)
            {
                // ensure server side if safezone permissions are respected
                if(!Utils.SafeZoneCanPaint(grid, OriginalSenderSteamId))
                {
                    Main.NetworkLibHandler.PacketWarningMessage.Send(OriginalSenderSteamId, "Failed to replace paint server side, denied by safe zone.");
                    return;
                }

                long identity = MyAPIGateway.Players.TryGetIdentityId(OriginalSenderSteamId);
                if(!Utils.AllowedToPaintGrid(grid, identity))
                {
                    Main.NetworkLibHandler.PacketWarningMessage.Send(OriginalSenderSteamId, "Failed to replace paint server side, ship not allied.");
                    return;
                }
            }

            PaintMaterial newPaint = new PaintMaterial(NewPaint);
            BlockMaterial oldPaint = new BlockMaterial(OldPaint);

            Main.Painting.ReplaceColorInGrid(false, grid, oldPaint, newPaint, IncludeSubgrids, OriginalSenderSteamId);
            relay = RelayMode.RelayOriginal;
        }
    }
}
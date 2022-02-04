using Digi.NetworkLib;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

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
            // no way to check if creative tools is enabled for sender but it's enough to check their access level.
            if(Main.IsServer && MyAPIGateway.Session.GetUserPromoteLevel(OriginalSenderSteamId) < MyPromoteLevel.SpaceMaster)
            {
                MyLog.Default.WriteLineAndConsole($"{PaintGunMod.MOD_NAME} Warning: Player {Utils.PrintPlayerName(OriginalSenderSteamId)} tried to use replace paint while not being at least SpaceMaster promote level.");
                Main.NetworkLibHandler.PacketWarningMessage.Send(OriginalSenderSteamId, "Failed to replace paint server side, access denied.");
                return;
            }

            bool modified = false;
            if(!Main.Palette.ValidateSkinOwnership(NewPaint.Skin, OriginalSenderSteamId))
            {
                NewPaint = new SerializedPaintMaterial(NewPaint.ColorMaskPacked, null);
                modified = true;
            }

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

            if(Main.IsServer)
                relay = modified ? RelayMode.RelayWithChanges : RelayMode.RelayOriginal;
        }
    }
}
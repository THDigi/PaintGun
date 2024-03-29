﻿using Digi.NetworkLib;
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
        private Vector3I BlockPosition;

        [ProtoMember(3)]
        private SerializedPaintMaterial Paint;

        [ProtoMember(4)]
        private MirrorData MirrorData;

        public PacketPaint() { } // Empty constructor required for deserialization

        public void Send(IMyCubeGrid grid, Vector3I blockPosition, PaintMaterial paint, bool useMirroring)
        {
            GridEntId = grid.EntityId;
            BlockPosition = blockPosition;
            Paint = new SerializedPaintMaterial(paint);
            MirrorData = (useMirroring ? new MirrorData(grid) : default(MirrorData));

            Network.SendToServer(this);

            // do the action for local client too
            if(!MyAPIGateway.Session.IsServer)
            {
                DoAction(grid);
            }
        }

        public override void Received(ref RelayMode relay)
        {
            if(Main.IsServer && Main.AccessRequiresAmmo(OriginalSenderSteamId)) // ammo consumption, only needed server side
            {
                IMyInventory inv = Utils.GetCharacterInventoryOrError(this, Utils.GetCharacterOrError(this, Utils.GetPlayerOrError(this, OriginalSenderSteamId)));
                if(inv != null)
                    inv.RemoveItemsOfType(1, Main.Constants.PAINT_MAG_ITEM, false);
            }

            MyCubeGrid grid = Utils.GetEntityOrError<MyCubeGrid>(this, GridEntId, Constants.NETWORK_DESYNC_ERROR_LOGGING);
            if(grid == null)
                return;

            if(Main.IsServer)
            {
                IMySlimBlock block = (IMySlimBlock)grid.GetCubeBlock(BlockPosition);

                if(block == null)
                {
                    if(Constants.NETWORK_DESYNC_ERROR_LOGGING)
                    {
                        Log.Error($"{GetType().Name} :: Can't paint inexistent blocks! Sender={OriginalSenderSteamId.ToString()}; Grid={grid} ({grid.EntityId.ToString()}) at GridPosition={BlockPosition.ToString()}", Log.PRINT_MESSAGE);
                    }

                    Main.NetworkLibHandler.PacketWarningMessage.Send(OriginalSenderSteamId, "Failed to paint server side, block no longer exists.");
                    return;
                }

                long identity = MyAPIGateway.Players.TryGetIdentityId(OriginalSenderSteamId);
                if(!Utils.AllowedToPaintGrid(grid, identity))
                {
                    if(Constants.NETWORK_DESYNC_ERROR_LOGGING)
                    {
                        Log.Error($"{GetType().Name} :: Can't paint non-allied grids! Sender={OriginalSenderSteamId.ToString()}; Grid={grid} ({grid.EntityId.ToString()})", Log.PRINT_MESSAGE);
                    }

                    Main.NetworkLibHandler.PacketWarningMessage.Send(OriginalSenderSteamId, "Failed to paint server side, ship not allied.");
                    return;
                }

                // ensure server side if safezone permissions are respected
                if(!Utils.SafeZoneCanPaint(block, OriginalSenderSteamId))
                {
                    if(Constants.NETWORK_DESYNC_ERROR_LOGGING)
                    {
                        Log.Error($"{GetType().Name} :: Can't paint inside no-build safe zone! Sender={OriginalSenderSteamId.ToString()}; Grid={grid} ({grid.EntityId.ToString()}); block={block.BlockDefinition.Id.ToString()} ({block.Min.ToString()})", Log.PRINT_MESSAGE);
                    }

                    Main.NetworkLibHandler.PacketWarningMessage.Send(OriginalSenderSteamId, "Failed to paint server side, denied by safe zone.");
                    return;
                }
            }

            if(DoAction(grid))
                relay = RelayMode.RelayWithChanges;
        }

        bool DoAction(IMyCubeGrid grid)
        {
            if(!Main.Palette.ValidateSkinOwnership(Paint.Skin, OriginalSenderSteamId))
            {
                //Paint = new SerializedPaintMaterial(Paint.ColorMaskPacked, null);
                return false;
            }

            PaintMaterial paint = new PaintMaterial(Paint);

            if(MirrorData.HasMirroring)
                Main.Painting.PaintBlockSymmetry(false, grid, BlockPosition, paint, MirrorData, OriginalSenderSteamId);
            else
                Main.Painting.PaintBlock(false, grid, BlockPosition, paint, OriginalSenderSteamId);

            return true;
        }
    }
}
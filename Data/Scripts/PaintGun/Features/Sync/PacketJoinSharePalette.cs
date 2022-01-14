using System;
using System.Collections.Generic;
using System.ComponentModel;
using Digi.NetworkLib;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketJoinSharePalette : PacketBase
    {
        [ProtoMember(1)]
        int SelectedColorSlot;

        [ProtoMember(2)]
        MyStringHash SelectedSkin;

        [ProtoMember(3)]
        bool ApplyColor;

        [ProtoMember(4)]
        bool ApplySkin;

        [ProtoMember(5)]
        bool ColorPickMode;

        [ProtoMember(6)]
        uint[] PackedColorMasks;

        [ProtoMember(7)]
        bool Reply;

        [ProtoMember(20)]
        ulong PaletteOwnerSteamId;

        public PacketJoinSharePalette() { } // Empty constructor required for deserialization

        public void Send(PlayerInfo pi, ulong? sendTo = null)
        {
            PaletteOwnerSteamId = pi.SteamId;
            SelectedColorSlot = pi.SelectedColorSlot;
            SelectedSkin = pi.SelectedSkin;
            ApplyColor = pi.ApplyColor;
            ApplySkin = pi.ApplySkin;
            ColorPickMode = pi.ColorPickMode;

            if(PackedColorMasks == null)
                PackedColorMasks = new uint[Constants.COLOR_PALETTE_SIZE];

            IReadOnlyList<Vector3> colors = pi.ColorsMasks;

            if(colors.Count != PackedColorMasks.Length)
            {
                Log.Error($"PacketJoinSharePalette.Send(), player {Utils.PrintPlayerName(PaletteOwnerSteamId)} has unexpected palette size={colors.Count.ToString()}");
                // continue execution
            }

            for(int i = 0; i < Math.Min(colors.Count, PackedColorMasks.Length); i++)
            {
                PackedColorMasks[i] = colors[i].PackHSVToUint();
            }

            if(sendTo.HasValue)
            {
                if(Constants.NETWORK_ACTION_LOGGING)
                    Log.Info($"{GetType().Name} :: sending {Utils.PrintPlayerName(PaletteOwnerSteamId)}'s palette to={sendTo.ToString()}.");

                Reply = false;
                Network.SendToPlayer(this, sendTo.Value);
            }
            else
            {
                if(Constants.NETWORK_ACTION_LOGGING)
                    Log.Info($"{GetType().Name} :: broadcasting {Utils.PrintPlayerName(PaletteOwnerSteamId)}'s palette.");

                Reply = true;
                Network.SendToServer(this);
            }
        }

        public override void Received(ref RelayMode relay)
        {
            relay = (Reply ? RelayMode.RelayOriginal : RelayMode.NoRelay);

            if(PaletteOwnerSteamId != MyAPIGateway.Multiplayer.MyId)
            {
                if(Constants.NETWORK_ACTION_LOGGING)
                    Log.Info($"{GetType().Name} :: received {Utils.PrintPlayerName(PaletteOwnerSteamId)}'s palette; Reply={Reply.ToString()}");

                IMyPlayer player = Utils.GetPlayerBySteamId(PaletteOwnerSteamId);
                if(player == null)
                    return;

                // apply palette info
                PlayerInfo pi = Main.Palette.GetOrAddPlayerInfo(PaletteOwnerSteamId);
                pi.SelectedColorSlot = SelectedColorSlot;
                pi.SelectedSkin = SelectedSkin;
                pi.ApplyColor = ApplyColor;
                pi.ApplySkin = ApplySkin;
                pi.ColorPickMode = ColorPickMode;
                pi.SetColors(PackedColorMasks);
            }

            if(Reply && MyAPIGateway.Multiplayer.IsServer)
            {
                if(Constants.NETWORK_ACTION_LOGGING)
                    Log.Info($"+ sending all players' palettes back to original sender.");

                foreach(PlayerInfo pi in Main.Palette.PlayerInfo.Values)
                {
                    if(pi.SteamId == OriginalSenderSteamId)
                        continue; // don't send their own palette back to them

                    Main.NetworkLibHandler.PacketJoinSharePalette.Send(pi, OriginalSenderSteamId);
                }
            }
        }
    }
}
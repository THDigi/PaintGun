using System;
using System.Collections.Generic;
using System.ComponentModel;
using Digi.NetworkLib;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketJoinSharePalette : PacketBase
    {
        [ProtoMember(1)]
        int SelectedColorIndex;

        [ProtoMember(2)]
        int SelectedSkinIndex;

        [ProtoMember(3)]
        bool ApplyColor;

        [ProtoMember(4)]
        bool ApplySkin;

        [ProtoMember(5)]
        bool ColorPickMode;

        [ProtoMember(6)]
        uint[] PackedColorMasks;

        [ProtoMember(7)]
        [DefaultValue(true)]
        bool Reply = true;

        public PacketJoinSharePalette() { } // Empty constructor required for deserialization

        public void Send(PlayerInfo pi, ulong overrideSteamId, ulong sendTo = 0)
        {
            SteamId = overrideSteamId;
            SelectedColorIndex = pi.SelectedColorIndex;
            SelectedSkinIndex = pi.SelectedSkinIndex;
            ApplyColor = pi.ApplyColor;
            ApplySkin = pi.ApplySkin;
            ColorPickMode = pi.ColorPickMode;

            if(PackedColorMasks == null)
                PackedColorMasks = new uint[Constants.COLOR_PALETTE_SIZE];

            IReadOnlyList<Vector3> colors = pi.ColorsMasks;

            if(colors.Count != PackedColorMasks.Length)
            {
                Log.Error($"PacketJoinSharePalette.Send(), player {Utils.PrintPlayerName(pi.SteamId)} has unexpected palette size={colors.Count.ToString()}");
                // continue execution
            }

            for(int i = 0; i < Math.Min(colors.Count, PackedColorMasks.Length); i++)
            {
                PackedColorMasks[i] = colors[i].PackHSVToUint();
            }

            if(sendTo != 0)
            {
                if(Constants.NETWORK_ACTION_LOGGING)
                    Log.Info($"{GetType().Name} :: sending {Utils.PrintPlayerName(SteamId)}'s palette to={sendTo.ToString()}.");

                Reply = false;
                Network.SendToPlayer(this, sendTo);
            }
            else
            {
                if(Constants.NETWORK_ACTION_LOGGING)
                    Log.Info($"{GetType().Name} :: broadcasting {Utils.PrintPlayerName(SteamId)}'s palette.");

                Reply = true;
                Network.SendToServer(this);
            }
        }

        public override void Received(ref bool relay)
        {
            relay = Reply;

            if(SteamId != MyAPIGateway.Multiplayer.MyId)
            {
                if(Constants.NETWORK_ACTION_LOGGING)
                    Log.Info($"{GetType().Name} :: received {Utils.PrintPlayerName(SteamId)}'s palette; Reply={Reply.ToString()}");

                IMyPlayer player = Utils.GetPlayerBySteamId(SteamId);
                if(player == null)
                    return;

                // apply palette info
                PlayerInfo pi = Main.Palette.GetOrAddPlayerInfo(SteamId);
                pi.SelectedColorIndex = SelectedColorIndex;
                pi.SelectedSkinIndex = SelectedSkinIndex;
                pi.ApplyColor = ApplyColor;
                pi.ApplySkin = ApplySkin;
                pi.ColorPickMode = ColorPickMode;
                pi.SetColors(PackedColorMasks);
            }

            if(Reply && MyAPIGateway.Multiplayer.IsServer)
            {
                if(Constants.NETWORK_ACTION_LOGGING)
                    Log.Info($"+ sending all players' palettes back to original sender.");

                foreach(KeyValuePair<ulong, PlayerInfo> kv in Main.Palette.PlayerInfo)
                {
                    if(kv.Key == SteamId)
                        continue; // don't send their own palette back to them

                    Main.NetworkLibHandler.PacketJoinSharePalette.Send(kv.Value, kv.Key, SteamId);
                }
            }
        }
    }
}
using System.Collections.Generic;
using System.ComponentModel;
using Digi.NetworkLib;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using Sandbox.ModAPI;
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
        List<uint> PackedColorMasks;

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
                PackedColorMasks = new List<uint>(Constants.COLOR_PALETTE_SIZE);
            else
                PackedColorMasks.Clear();

            foreach(var colorMask in pi.ColorsMasks)
            {
                PackedColorMasks.Add(colorMask.PackHSVToUint());
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

                var player = Utils.GetPlayerBySteamId(SteamId);

                if(player == null)
                    return;

                // apply palette info
                var pi = Main.Palette.GetOrAddPlayerInfo(SteamId);
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

                foreach(var kv in Main.Palette.PlayerInfo)
                {
                    if(kv.Key == SteamId)
                        continue; // don't send their own palette back to them

                    Main.NetworkLibHandler.PacketJoinSharePalette.Send(kv.Value, kv.Key, SteamId);
                }
            }
        }
    }
}
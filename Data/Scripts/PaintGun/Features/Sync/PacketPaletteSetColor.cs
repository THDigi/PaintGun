using Digi.NetworkLib;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using VRageMath;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketPaletteSetColor : PacketBase
    {
        [ProtoMember(1)]
        byte ColorIndex;

        [ProtoMember(2)]
        uint ColorMaskPacked;

        public PacketPaletteSetColor() { } // Empty constructor required for deserialization

        public void Send(int index, Vector3 colorMask)
        {
            ColorIndex = (byte)index;
            ColorMaskPacked = ColorExtensions.PackHSVToUint(colorMask);

            if(Constants.NETWORK_ACTION_LOGGING)
                Log.Info($"{GetType().Name} :: Sending pallete slot update; slot={ColorIndex.ToString()}; color={ColorExtensions.UnpackHSVFromUint(ColorMaskPacked).ToString()}");

            Network.SendToServer(this);
        }

        public override void Received(ref RelayMode relay)
        {
            relay = RelayMode.RelayOriginal;

            if(Constants.NETWORK_ACTION_LOGGING)
                Log.Info($"{GetType().Name} :: Received palette slot update; player={Utils.PrintPlayerName(OriginalSenderSteamId)}, slot={ColorIndex.ToString()}; color={ColorExtensions.UnpackHSVFromUint(ColorMaskPacked).ToString()}");

            PlayerInfo pi = Main.Palette.GetOrAddPlayerInfo(OriginalSenderSteamId);
            pi.SetColorAt(ColorIndex, ColorExtensions.UnpackHSVFromUint(ColorMaskPacked));
        }
    }
}
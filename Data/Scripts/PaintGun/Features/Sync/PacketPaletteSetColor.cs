using Digi.NetworkLib;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using VRageMath;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketPaletteSetColor : PacketBase
    {
        [ProtoMember(1)]
        int ColorIndex;

        [ProtoMember(2)]
        uint ColorMaskPacked;

        public PacketPaletteSetColor() { } // Empty constructor required for deserialization

        public void Send(int index, Vector3 colorMask)
        {
            ColorIndex = index;
            ColorMaskPacked = colorMask.PackHSVToUint();

            if(Constants.NETWORK_ACTION_LOGGING)
                Log.Info($"{GetType().Name} :: Sending pallete slot update; slot={ColorIndex.ToString()}; color={ColorExtensions.UnpackHSVFromUint(ColorMaskPacked).ToString()}");

            Network.SendToServer(this);
        }

        public override void Received(ref bool relay)
        {
            relay = true;

            if(Constants.NETWORK_ACTION_LOGGING)
                Log.Info($"{GetType().Name} :: Received palette slot update; player={Utils.PrintPlayerName(SteamId)}, slot={ColorIndex.ToString()}; color={ColorExtensions.UnpackHSVFromUint(ColorMaskPacked).ToString()}");

            var pi = Main.Palette.GetOrAddPlayerInfo(SteamId);

            pi.SetColorAt(ColorIndex, ColorMaskPacked);
        }
    }
}
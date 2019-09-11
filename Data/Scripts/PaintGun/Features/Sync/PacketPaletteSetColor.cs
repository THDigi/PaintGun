using Digi.NetworkLib;
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

            Network.SendToServer(this);
        }

        public override void Received(ref bool relay)
        {
            relay = true;

            var pi = Main.Palette.GetOrAddPlayerInfo(SteamId);

            pi.SetColorAt(ColorIndex, ColorMaskPacked);
        }
    }
}
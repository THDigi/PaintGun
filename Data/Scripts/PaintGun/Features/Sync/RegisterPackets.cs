using Digi.PaintGun;
using Digi.PaintGun.Features.Sync;
using ProtoBuf;

namespace Digi.NetworkLib
{
    [ProtoInclude(10, typeof(PacketPaint))]
    [ProtoInclude(11, typeof(PacketReplacePaint))]

    [ProtoInclude(20, typeof(PacketPaletteUpdate))]
    [ProtoInclude(21, typeof(PacketPaletteSetColor))]
    [ProtoInclude(22, typeof(PacketJoinSharePalette))]

    [ProtoInclude(30, typeof(PacketToolSpraying))]

    [ProtoInclude(250, typeof(PacketOwnershipTestRequest))]
    [ProtoInclude(251, typeof(PacketOwnershipTestResults))]
    public abstract partial class PacketBase
    {
        protected PaintGunMod Main => PaintGunMod.Instance;
        protected Network Network => Main.NetworkLibHandler.Lib;
    }
}
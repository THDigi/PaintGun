using Digi.NetworkLib;
using Digi.PaintGun.Features.Sync;

namespace Digi.PaintGun.Systems
{
    public class NetworkLibHandler : ModComponent
    {
        public PacketPaint PacketPaint;
        public PacketReplacePaint PacketReplacePaint;

        public PacketPaletteUpdate PacketPaletteUpdate;
        public PacketPaletteSetColor PacketPaletteSetColor;
        public PacketJoinSharePalette PacketJoinSharePalette;

        public PacketToolSpraying PacketToolSpraying;
        public PacketWarningMessage PacketWarningMessage;

        public PacketOwnershipTestRequest PacketOwnershipTestRequest;
        public PacketOwnershipTestResults PacketOwnershipTestResults;

        public Network Lib;

        public NetworkLibHandler(PaintGunMod main) : base(main)
        {
            Lib = new Network(Constants.NETWORK_CHANNEL);

            // needed here because they call an API method on creation
            PacketPaint = new PacketPaint();
            PacketReplacePaint = new PacketReplacePaint();

            PacketPaletteUpdate = new PacketPaletteUpdate();
            PacketPaletteSetColor = new PacketPaletteSetColor();
            PacketJoinSharePalette = new PacketJoinSharePalette();

            PacketToolSpraying = new PacketToolSpraying();
            PacketWarningMessage = new PacketWarningMessage();

            PacketOwnershipTestRequest = new PacketOwnershipTestRequest();
            PacketOwnershipTestResults = new PacketOwnershipTestResults();
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
            Lib.Dispose();
            Lib = null;
        }
    }
}

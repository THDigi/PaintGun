using Digi.NetworkLib;
using Digi.PaintGun.Utilities;

namespace Digi.PaintGun.Features.Sync
{
    public class NetworkLibHandler : ModComponent
    {
        public PacketPaint PacketPaint;
        public PacketReplacePaint PacketReplacePaint;
        public PacketConsumeAmmo PacketConsumeAmmo;

        public PacketPaletteUpdate PacketPaletteUpdate;
        public PacketPaletteSetColor PacketPaletteSetColor;
        public PacketJoinSharePalette PacketJoinSharePalette;

        public PacketToolSpraying PacketToolSpraying;
        public PacketWarningMessage PacketWarningMessage;

        public Network Lib;

        public NetworkLibHandler(PaintGunMod main) : base(main)
        {
            Lib = new Network(Constants.NETWORK_CHANNEL, PaintGunMod.MOD_NAME, true, (e) => Log.Error(e, Log.PRINT_MESSAGE));
            Lib.ExceptionHandler = (e) => Log.Error(e);
            Lib.ReceiveExceptionHandler = (sender, bytes) => Log.Error($"Additional info: sender={Utils.PrintPlayerName(sender)}; bytes={string.Join(",", bytes)}");

            // needed here because they call an API method on creation
            PacketPaint = new PacketPaint();
            PacketReplacePaint = new PacketReplacePaint();
            PacketConsumeAmmo = new PacketConsumeAmmo();

            PacketPaletteUpdate = new PacketPaletteUpdate();
            PacketPaletteSetColor = new PacketPaletteSetColor();
            PacketJoinSharePalette = new PacketJoinSharePalette();

            PacketToolSpraying = new PacketToolSpraying();
            PacketWarningMessage = new PacketWarningMessage();
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
            Lib?.Dispose();
            Lib = null;
        }
    }
}

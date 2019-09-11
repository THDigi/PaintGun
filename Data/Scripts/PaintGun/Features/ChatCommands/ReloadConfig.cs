using System.Text;
using Digi.PaintGun.Utilities;
using VRage.Game;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace Digi.PaintGun.Features.ChatCommands
{
    public class ReloadConfig : CommandHandlerBase
    {
        public ReloadConfig() : base("reload")
        {
        }

        public override void Execute(MyCommandLine parser)
        {
            if(Main.Settings.Load())
                Utils.ShowColoredChatMessage(PaintGunMod.MOD_NAME, "Reloaded and re-saved config.", MyFontEnum.Green);
            else
                Utils.ShowColoredChatMessage(PaintGunMod.MOD_NAME, "Config created with the current settings.", MyFontEnum.Green);

            Main.Settings.Save();
            return;
        }

        public override void PrintHelp(StringBuilder sb)
        {
            sb.Append(ChatCommands.MAIN_COMMAND).Append(" reload").Append('\n');
            sb.Append("  Reloads the config file.").Append('\n');
            sb.Append('\n');
        }
    }
}
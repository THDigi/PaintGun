using System.Text;
using Digi.PaintGun.Utilities;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace Digi.PaintGun.Features.ChatCommands
{
    public class ColorPicker : CommandHandlerBase
    {
        public ColorPicker() : base("pick")
        {
        }

        public override void Execute(MyCommandLine parser)
        {
            if(Main.LocalToolHandler.LocalTool == null)
            {
                Utils.ShowColoredChatMessage(PaintGunMod.MOD_NAME, "You need to hold the paint gun for this to work.", MyFontEnum.Red);
            }
            else
            {
                if(Main.Palette.ReplaceMode)
                {
                    Main.Palette.ReplaceMode = false;
                    Main.Notifications.Show(3, "Replace color mode turned off.", MyFontEnum.White, 2000);
                }

                Main.Palette.ColorPickMode = true;
            }
        }

        public override void PrintHelp(StringBuilder text)
        {
            foreach(var alias in Aliases)
            {
                text.Append(ChatCommands.MAIN_COMMAND).Append(' ').Append(alias).Append('\n');
            }

            var assignedLG = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.LANDING_GEAR));

            text.Append("  Activate color picker mode (hotkey: Shift+").Append(assignedLG).Append(')').Append('\n');
            text.Append('\n');
        }
    }
}
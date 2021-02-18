using System.Text;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace Digi.PaintGun.Features.ChatCommands
{
    public class Help : CommandHandlerBase
    {
        StringBuilder sb;

        public Help() : base("", "help")
        {
        }

        public override void Execute(MyCommandLine parser = null)
        {
            var ch = PaintGunMod.Instance.ChatCommands;

            if(sb == null)
                sb = new StringBuilder((ch.CommandHandlers.Count * 128) + 512);

            var assignedLG = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.LANDING_GEAR));
            var assignedSecondaryClick = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.SECONDARY_TOOL_ACTION));
            var assignedCubeSize = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE));
            var assignedColorBlock = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE));
            var assignedColorPrev = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.SWITCH_LEFT));
            var assignedColorNext = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.SWITCH_RIGHT));

            sb.Append("##### Chat Commands #####").Append('\n');
            sb.Append('\n');

            foreach(var handler in ch.CommandHandlers)
            {
                handler.PrintHelp(sb);
            }

            sb.Append("##### Hotkeys #####").Append('\n');
            sb.Append('\n');
            sb.Append(PaintGunMod.Instance.Settings.requireCtrlForColorCycle ? "Ctrl+" : "").Append("MouseScroll or ").Append(assignedColorPrev).Append("/").Append(assignedColorNext).Append('\n');
            sb.Append("  Change selected color slot.").Append('\n');
            sb.Append('\n');
            sb.Append("Shift+MouseScroll or Shift+").Append(assignedColorPrev).Append("/Shift+").Append(assignedColorNext).Append('\n');
            sb.Append("  Change selected skin.").Append('\n');
            sb.Append('\n');
            sb.Append(assignedColorBlock).Append('\n');
            sb.Append("  Toggle if color is applied.").Append('\n');
            sb.Append('\n');
            sb.Append("Shift+").Append(assignedColorBlock).Append('\n');
            sb.Append("  Toggle if skin is applied.").Append('\n');
            sb.Append('\n');
            sb.Append(assignedSecondaryClick).Append('\n');
            sb.Append("  Deep paint mode, allows painting under blocks if you're close enough.").Append('\n');
            sb.Append('\n');
            sb.Append("Shift+").Append(assignedSecondaryClick).Append('\n');
            sb.Append("  Replaces selected color with aimed block/player's color.").Append('\n');
            sb.Append('\n');
            sb.Append("Shift+").Append(assignedLG).Append('\n');
            sb.Append("  Toggle color picker mode.").Append('\n');
            sb.Append('\n');
            sb.Append("Shift+").Append(assignedCubeSize).Append('\n');
            sb.Append("  (Creative or SM) Toggle replace color mode.").Append('\n');
            sb.Append('\n');
            sb.Append("##### Config path #####").Append('\n');
            sb.Append('\n');
            sb.Append(@"%appdata%\SpaceEngineers\Storage").Append('\n');
            sb.Append(@"    \").Append(MyAPIGateway.Utilities.GamePaths.ModScopeName).Append(@"\").Append(Settings.FileName).Append('\n');

            MyAPIGateway.Utilities.ShowMissionScreen(PaintGunMod.MOD_NAME + " help", null, null, sb.ToString(), null, "Close");
        }

        public override void PrintHelp(StringBuilder sb)
        {
            sb.Append(ChatCommands.MAIN_COMMAND).Append("").Append('\n');
            sb.Append(ChatCommands.MAIN_COMMAND).Append(" help").Append('\n');
            sb.Append("  Shows this window.").Append('\n');
            sb.Append('\n');
        }
    }
}
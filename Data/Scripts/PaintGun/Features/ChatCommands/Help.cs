﻿using System.Text;
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
            string assignedLG = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.LANDING_GEAR));
            string assignedSecondaryClick = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.SECONDARY_TOOL_ACTION));
            string assignedCubeSize = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE));
            string assignedColorBlock = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.CUBE_COLOR_CHANGE));
            string assignedColorPrev = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.SWITCH_LEFT));
            string assignedColorNext = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.SWITCH_RIGHT));

            ChatCommands ch = PaintGunMod.Instance.ChatCommands;
            if(sb == null)
                sb = new StringBuilder((ch.CommandHandlers.Count * 128) + 512);

            const string SegmentPrefix = "  "; //  menu;  windows
            const string SegmentSuffix = ""; // " —————————";

            sb.Append(SegmentPrefix).Append("Config path").Append(SegmentSuffix).Append('\n');
            sb.Append('\n');
            sb.Append(@"%appdata%\SpaceEngineers\Storage").Append('\n');
            sb.Append(@"    \").Append(MyAPIGateway.Utilities.GamePaths.ModScopeName).Append(@"\").Append(Settings.FileName).Append('\n');
            sb.Append('\n');

            sb.Append(SegmentPrefix).Append("Chat Commands").Append(SegmentSuffix).Append('\n');
            sb.Append('\n');

            foreach(CommandHandlerBase handler in ch.CommandHandlers)
            {
                handler.PrintHelp(sb);
            }

            Constants constants = PaintGunMod.Instance.Constants;

            sb.Append(SegmentPrefix).Append("Hotkeys").Append(SegmentSuffix).Append('\n');
            sb.Append('\n');
            sb.Append(PaintGunMod.Instance.Settings.requireCtrlForColorCycle ? "Ctrl+" : "").Append("MouseScroll or ").Append(assignedColorPrev).Append("/").Append(assignedColorNext).Append(" or ").Append(constants.GamepadBindName_CycleColors).Append('\n');
            sb.Append("  Change selected color slot.").Append('\n');
            sb.Append('\n');
            sb.Append("Shift+MouseScroll or Shift+").Append(assignedColorPrev).Append("/Shift+").Append(assignedColorNext).Append(" or ").Append(constants.GamepadBindName_CycleSkins).Append('\n');
            sb.Append("  Change selected skin.").Append('\n');
            sb.Append('\n');
            sb.Append(assignedColorBlock).Append('\n');
            sb.Append("  Toggle if color is applied.").Append('\n');
            sb.Append('\n');
            sb.Append("Shift+").Append(assignedColorBlock).Append('\n');
            sb.Append("  Toggle if skin is applied.").Append('\n');
            sb.Append('\n');
            sb.Append(assignedSecondaryClick).Append(" or ").Append(constants.GamepadBindName_DeepPaintMode).Append('\n');
            sb.Append("  Deep paint mode, allows painting under blocks if you're close enough.").Append('\n');
            sb.Append('\n');
            sb.Append("Shift+").Append(assignedSecondaryClick).Append('\n');
            sb.Append("  Replaces selected color with aimed block/player's color.").Append('\n');
            sb.Append('\n');
            sb.Append("Shift+").Append(assignedLG).Append('\n');
            sb.Append("  Toggle color picker mode.").Append('\n');
            sb.Append('\n');
            sb.Append("Shift+").Append(assignedCubeSize).Append('\n');
            sb.Append("  Toggle replace color mode");
            if(Main.ServerSettings.ReplacePaintSurvival)
                sb.Append(" (this server allows it in survival)");
            else
                sb.Append(" (creative/SM only)");
            sb.Append('\n');

            sb.Append('\n');

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
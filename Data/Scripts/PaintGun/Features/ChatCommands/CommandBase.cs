using System.Text;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace Digi.PaintGun.Features.ChatCommands
{
    public abstract class CommandHandlerBase
    {
        public readonly string[] Aliases;

        protected PaintGunMod Main => PaintGunMod.Instance;

        public CommandHandlerBase(params string[] commands)
        {
            Aliases = commands;
        }

        public abstract void Execute(MyCommandLine parser);

        public abstract void PrintHelp(StringBuilder sb);
    }
}
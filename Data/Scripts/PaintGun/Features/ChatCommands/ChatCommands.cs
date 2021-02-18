using System;
using System.Collections.Generic;
using Digi.PaintGun.Utilities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace Digi.PaintGun.Features.ChatCommands
{
    public class ChatCommands : ModComponent
    {
        internal const string MAIN_COMMAND = "/pg";

        public IReadOnlyList<CommandHandlerBase> CommandHandlers => commandHandlers;

        public Help HelpCommand;

        List<CommandHandlerBase> commandHandlers = new List<CommandHandlerBase>();
        Dictionary<string, CommandHandlerBase> aliasToCommandHandler = new Dictionary<string, CommandHandlerBase>();
        MyCommandLine argParser = new MyCommandLine();

        public ChatCommands(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
            HelpCommand = new Help();
            AddCommand(HelpCommand);
            AddCommand(new ColorPicker());
            AddCommand(new SetColor());
            AddCommand(new ReloadConfig());

            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
        }

        protected override void UnregisterComponent()
        {
            if(!IsRegistered)
                return;

            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
        }

        void AddCommand(CommandHandlerBase handler)
        {
            commandHandlers.Add(handler);

            foreach(var alias in handler.Aliases)
            {
                aliasToCommandHandler.Add(alias, handler);
            }
        }

        void MessageEntered(string msg, ref bool send)
        {
            try
            {
                if(!msg.StartsWith(MAIN_COMMAND))
                    return;

                if(!argParser.TryParse(msg))
                {
                    Utils.ShowColoredChatMessage(PaintGunMod.MOD_NAME, $"Couldn't parse command \"{msg}\" for some reason, please report!", MyFontEnum.Red);
                    Log.Error($"Couldn't parse command for some reason. Text entered: \"{msg}\"");
                    return;
                }

                send = false;

                var alias = argParser.Argument(1) ?? string.Empty;
                CommandHandlerBase handler;

                if(aliasToCommandHandler.TryGetValue(alias, out handler))
                {
                    handler.Execute(argParser);
                }
                else
                {
                    Utils.ShowColoredChatMessage(PaintGunMod.MOD_NAME, $"Unknown command: {MAIN_COMMAND} {alias}", MyFontEnum.Red);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}
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
        public const string MAIN_COMMAND = "/pg";

        public readonly List<CommandHandlerBase> CommandHandlers = new List<CommandHandlerBase>(4);
        public readonly Dictionary<string, CommandHandlerBase> AliasToCommandHandler = new Dictionary<string, CommandHandlerBase>(4);

        public Help HelpCommand;

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
            CommandHandlers.Add(handler);

            foreach(string alias in handler.Aliases)
            {
                AliasToCommandHandler.Add(alias, handler);
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

                string alias = argParser.Argument(1) ?? string.Empty;
                CommandHandlerBase handler;

                if(AliasToCommandHandler.TryGetValue(alias, out handler))
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
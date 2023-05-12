using System;
using System.IO;
using Sandbox.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;

namespace Digi.PaintGun.Features
{
    public class ServerSettings : ModComponent
    {
        // do not rename
        public bool RequireAmmo { get; private set; } = true;
        public float PaintSpeedMultiplier { get; private set; } = 1f;
        public bool ReplacePaintSurvival { get; private set; } = false;

        void LoadSettings(MyIni iniParser)
        {
            RequireAmmo = iniParser.Get(IniSection, nameof(RequireAmmo)).ToBoolean(RequireAmmo);
            PaintSpeedMultiplier = iniParser.Get(IniSection, nameof(PaintSpeedMultiplier)).ToSingle(PaintSpeedMultiplier);
            ReplacePaintSurvival = iniParser.Get(IniSection, nameof(ReplacePaintSurvival)).ToBoolean(ReplacePaintSurvival);
        }

        void SaveSettings(MyIni iniParser, bool comments)
        {
            iniParser.AddSection(IniSection);
            if(comments)
                iniParser.SetSectionComment(IniSection, "Server only reads these settings when it starts.");

            SetVal(iniParser, nameof(RequireAmmo), RequireAmmo);

            SetVal(iniParser, nameof(PaintSpeedMultiplier), PaintSpeedMultiplier,
                comments ? "World welder&grinder multipliers still affect this on top of this multiplier.\nCan set to 0 to instantly paint." : null);

            SetVal(iniParser, nameof(ReplacePaintSurvival), ReplacePaintSurvival,
                comments ? "Note: in replace mode blocks are instantly painted and tool does not use ammo." : null);
        }

        const string VariableId = "PaintGunMod_ServerSettings"; // IMPORTANT: must be unique as it gets written in a shared space (sandbox.sbc)
        const string FileName = "ServerSettings.ini"; // the file that gets saved to world storage under your mod's folder
        const string IniSection = "ServerSettings";

        public ServerSettings(PaintGunMod main) : base(main)
        {
            if(MyAPIGateway.Session.IsServer)
                LoadOnHost();
            else
                LoadOnClient();
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }

        void LoadOnHost()
        {
            MyIni iniParser = new MyIni();

            // load file if exists then save it regardless so that it can be sanitized and updated

            if(MyAPIGateway.Utilities.FileExistsInWorldStorage(FileName, typeof(ServerSettings)))
            {
                using(TextReader file = MyAPIGateway.Utilities.ReadFileInWorldStorage(FileName, typeof(ServerSettings)))
                {
                    string text = file.ReadToEnd();

                    if(!string.IsNullOrWhiteSpace(text))
                    {
                        if(MyIni.HasSection(text, IniSection))
                        {
                            MyIniParseResult result;
                            if(!iniParser.TryParse(text, out result))
                            {
                                string fullPath = Path.Combine(MyAPIGateway.Session.CurrentPath, "Storage", MyAPIGateway.Utilities.GamePaths.ModScopeName, FileName);
                                throw new Exception($"Config error: {result.ToString()}\nDelete config file if you wish to reset to defaults: {fullPath}");
                            }

                            LoadSettings(iniParser);
                        }
                        else
                        {
                            Log.Error($"Config file didn't contain the {IniSection} section, ignoring.");
                        }
                    }
                    else
                    {
                        Log.Error("Config file was empty, ignoring.");
                    }
                }
            }

            iniParser.Clear();
            SaveSettings(iniParser, comments: false);
            string settingsText = iniParser.ToString();

            MyAPIGateway.Utilities.SetVariable<string>(VariableId, settingsText);

            Log.Info($"Loaded server settings:\n{settingsText}");
            MyLog.Default.WriteLineAndConsole($"PaintGunMod - Loaded server settings:\n{settingsText}");

            iniParser.Clear();
            SaveSettings(iniParser, comments: true);

            string settingsWithComments = iniParser.ToString();

            using(TextWriter file = MyAPIGateway.Utilities.WriteFileInWorldStorage(FileName, typeof(ServerSettings)))
            {
                file.Write(settingsWithComments);
            }
        }

        void LoadOnClient()
        {
            string settingsText;
            if(!MyAPIGateway.Utilities.GetVariable<string>(VariableId, out settingsText))
                throw new Exception("No config found in sandbox.sbc!");

            MyIni iniParser = new MyIni();
            MyIniParseResult result;
            if(!iniParser.TryParse(settingsText, out result))
                throw new Exception($"Config error: {result.ToString()}");

            LoadSettings(iniParser);

            Log.Info($"Loaded server settings:\n{settingsText}");
            MyLog.Default.WriteLineAndConsole($"PaintGunMod - Loaded server settings:\n{settingsText}");
        }

        static void SetVal<T>(MyIni iniParser, string key, T value, string comment = null)
        {
            iniParser.Set(IniSection, key, value?.ToString());
            iniParser.SetComment(IniSection, key, comment);
        }
    }
}
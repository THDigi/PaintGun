using System;
using System.Text;
using System.IO;
using Sandbox.ModAPI;
using VRageMath;

namespace Digi.PaintGun
{
    public class Settings
    {
        private const string FILE = "paintgun.cfg";
        private const int CFG_VERSION = 2;
        private const int CFG_VERSION_NEWHUDDEFAULTS = 1;
        private const int CFG_VERSION_HUDBKOPACITYDEFAULTS = 2;

        public bool extraSounds = true;
        public bool sprayParticles = true;
        public float spraySoundVolume = 0.8f;
        public bool selectColorZigZag = false;
        public bool hidePaletteWithHUD = true;
        public Vector2D paletteScreenPos = paletteScreenPosDefault;
        public float paletteScale = paletteScaleDefault;
        public float paletteBackgroundOpacity = -1;

        public static readonly Vector2D paletteScreenPosDefault = new Vector2D(0.4345, -0.691);
        public static readonly float paletteScaleDefault = 0.5f;

        public ControlCombination pickColor1 = ControlCombination.CreateFrom("shift c.landinggear");
        public ControlCombination pickColor2 = ControlCombination.CreateFrom("g.lb g.rb");

        public ControlCombination replaceMode1 = ControlCombination.CreateFrom("shift c.cubesizemode");
        public ControlCombination replaceMode2 = null;

        private static char[] CHARS = new char[] { '=' };

        public bool firstLoad = false;

        public Settings()
        {
            // load the settings if they exist
            if(!Load())
            {
                firstLoad = true; // config didn't exist, assume it's the first time the mod is loaded
            }

            Save(); // refresh config in case of any missing or extra settings
        }

        public bool Load()
        {
            try
            {
                if(MyAPIGateway.Utilities.FileExistsInLocalStorage(FILE, typeof(Settings)))
                {
                    var file = MyAPIGateway.Utilities.ReadFileInLocalStorage(FILE, typeof(Settings));
                    ReadSettings(file);
                    file.Close();
                    return true;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
        }

        private void ReadSettings(TextReader file)
        {
            try
            {
                string line;
                string[] args;
                int i;
                bool b;
                float f;
                int prevConfigVersion = 0;

                while((line = file.ReadLine()) != null)
                {
                    if(line.Length == 0)
                        continue;

                    i = line.IndexOf("//", StringComparison.Ordinal);

                    if(i > -1)
                        line = (i == 0 ? "" : line.Substring(0, i));

                    if(line.Length == 0)
                        continue;

                    args = line.Split(CHARS, 2);

                    if(args.Length != 2)
                    {
                        Log.Error("Unknown " + FILE + " line: " + line + "\nMaybe is missing the '=' ?");
                        continue;
                    }

                    args[0] = args[0].Trim().ToLower();
                    args[1] = args[1].Trim().ToLower();

                    switch(args[0])
                    {
                        case "configversion":
                            if(int.TryParse(args[1], out i))
                                prevConfigVersion = i;
                            else
                                Log.Error("Invalid " + args[0] + " value: " + args[1]);
                            continue;
                        case "extrasounds":
                            if(bool.TryParse(args[1], out b))
                                extraSounds = b;
                            else
                                Log.Error("Invalid " + args[0] + " value: " + args[1]);
                            continue;
                        case "sprayparticles":
                            if(bool.TryParse(args[1], out b))
                                sprayParticles = b;
                            else
                                Log.Error("Invalid " + args[0] + " value: " + args[1]);
                            continue;
                        case "spraysoundvolume":
                            if(float.TryParse(args[1], out f))
                                spraySoundVolume = MathHelper.Clamp(f, 0, 1);
                            else
                                Log.Error("Invalid " + args[0] + " value: " + args[1]);
                            continue;
                        case "selectcolorzigzag":
                            if(bool.TryParse(args[1], out b))
                                selectColorZigZag = b;
                            else
                                Log.Error("Invalid " + args[0] + " value: " + args[1]);
                            continue;
                        case "hidepalettewithhud":
                            if(bool.TryParse(args[1], out b))
                                hidePaletteWithHUD = b;
                            else
                                Log.Error("Invalid " + args[0] + " value: " + args[1]);
                            continue;
                        case "palettescreenpos":
                            var vars = args[1].Split(',');
                            double x, y;
                            if(vars.Length == 2 && double.TryParse(vars[0].Trim(), out x) && double.TryParse(vars[1].Trim(), out y))
                            {
                                if(prevConfigVersion < CFG_VERSION_NEWHUDDEFAULTS && x == 0.29d && y == -0.73d) // reset to default if config is older and had default setting
                                    paletteScreenPos = paletteScreenPosDefault;
                                else
                                    paletteScreenPos = new Vector2D(x, y);
                            }
                            else
                                Log.Error("Invalid " + args[0] + " value: " + args[1]);
                            continue;
                        case "palettescale":
                            if(float.TryParse(args[1], out f))
                            {
                                if(prevConfigVersion < CFG_VERSION_NEWHUDDEFAULTS && f == 1f) // reset to default if config is older and had default setting
                                    paletteScale = paletteScaleDefault;
                                else
                                    paletteScale = MathHelper.Clamp(f, -100, 100);
                            }
                            else
                                Log.Error("Invalid " + args[0] + " value: " + args[1]);
                            continue;
                        case "palettebackgroundopacity":
                            if(prevConfigVersion < CFG_VERSION_HUDBKOPACITYDEFAULTS || args[1].Equals("hud"))
                                paletteBackgroundOpacity = -1;
                            else if(float.TryParse(args[1], out f))
                                paletteBackgroundOpacity = MathHelper.Clamp(f, 0, 1);
                            else
                                Log.Error("Invalid " + args[0] + " value: " + args[1]);
                            continue;
                        case "pickcolorinput1":
                        case "pickcolorinput2":
                        case "replacemodeinput1":
                        case "replacemodeinput2":
                            if(args[1].Length == 0)
                                continue;
                            var obj = ControlCombination.CreateFrom(args[1]);
                            if(obj != null)
                            {
                                if(args[0].StartsWith("pick", StringComparison.Ordinal))
                                {
                                    if(args[0].EndsWith("1", StringComparison.Ordinal))
                                        pickColor1 = obj;
                                    else
                                        pickColor2 = obj;
                                }
                                else
                                {
                                    if(args[0].EndsWith("1", StringComparison.Ordinal))
                                        replaceMode1 = obj;
                                    else
                                        replaceMode2 = obj;
                                }
                            }
                            else
                                Log.Error("Invalid " + args[0] + " value: " + args[1]);
                            continue;
                    }
                }

                Log.Info("Loaded settings:\n" + GetSettingsString(false));
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void Save()
        {
            try
            {
                var file = MyAPIGateway.Utilities.WriteFileInLocalStorage(FILE, typeof(Settings));
                file.Write(GetSettingsString(true));
                file.Flush();
                file.Close();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public string GetSettingsString(bool comments)
        {
            var str = new StringBuilder();

            if(comments)
            {
                str.Append("ConfigVersion=").Append(CFG_VERSION).AppendLine(comments ? " // Do not edit. This is used by the mod to reset settings when their defaults change without changing them if they're custom." : "");
                str.AppendLine("// Paint Gun mod config; this file gets automatically overwritten after being loaded so don't leave custom comments.");
                str.AppendLine("// You can reload this while the game is running by typing in chat: /pg reload");
                str.AppendLine("// Lines starting with // are comments. All values are case insensitive unless otherwise specified.");
                str.AppendLine();
            }

            str.Append("ExtraSounds=").Append(extraSounds).AppendLine(comments ? " // toggle sounds: when aiming at a different color in color pick mode, when finishing painting in survival and when cycling colors. Default: true" : "");
            str.Append("SprayParticles=").Append(sprayParticles).AppendLine(comments ? " // toggles the spray particles. Default: true" : "");
            str.Append("SpraySoundVolume=").Append(spraySoundVolume).AppendLine(comments ? " // paint gun spraying sound volume. Default: 0.8" : "");
            str.Append("SelectColorZigZag=").Append(selectColorZigZag).AppendLine(comments ? " // type of scrolling through colors in the palette, false is each row at a time, true is in zig-zag. Default: false" : "");
            str.Append("HidePaletteWithHUD=").Append(hidePaletteWithHUD).AppendLine(comments ? " // wether to hide the color palette along with the HUD. Set to false to always show the color palette regardless if HUD is visible or not. Default: true" : "");
            str.Append("PaletteScreenPos=").Append(Math.Round(paletteScreenPos.X, 5)).Append(", ").Append(Math.Round(paletteScreenPos.Y, 5)).AppendLine(comments ? " // color palette screen position in X and Y coordinates where 0,0 is the screen center. Positive values are right and up and negative ones are opposite of that. Default: 0.4345, -0.691" : "");
            str.Append("PaletteScale=").Append(Math.Round(paletteScale, 5)).AppendLine(comments ? " // color palette overall scale. Default: 0.5" : "");
            str.Append("PaletteBackgroundOpacity=").Append(paletteBackgroundOpacity < 0 ? "HUD" : Math.Round(paletteBackgroundOpacity, 5).ToString()).AppendLine(comments ? " // Palette's background opacity percent scale (0 to 1 value) or set to HUD to use the game's HUD opacity. Default: HUD" : "");

            if(comments)
            {
                str.AppendLine();
                str.AppendLine("// Key/mouse/gamepad combination to trigger '/pg pick' command.");
                str.AppendLine("// Separate multiple keys/buttons/controls with spaces. For gamepad add " + InputHandler.GAMEPAD_PREFIX + " prefix, for mouse add " + InputHandler.MOUSE_PREFIX + " prefix and for game controls add " + InputHandler.CONTROL_PREFIX + " prefix.");
                str.AppendLine("// All keys, mouse buttons, gamepad buttons/axes and control names are at the bottom of this file.");
            }
            str.Append("PickColorInput1=").Append(pickColor1 == null ? "" : pickColor1.GetStringCombination()).AppendLine(comments ? " // Default: shift c.landinggear" : "");
            str.Append("PickColorInput2=").Append(pickColor2 == null ? "" : pickColor2.GetStringCombination()).AppendLine(comments ? " // Default: g.lb g.rb" : "");

            if(comments)
            {
                str.AppendLine();
                str.AppendLine("// Key/mouse/gamepad combination to toggle the replace color mode, which only works in creative.");
                str.AppendLine("// Same input rules as above.");
            }
            str.Append("ReplaceModeInput1=").Append(replaceMode1 == null ? "" : replaceMode1.GetStringCombination()).AppendLine(comments ? " // Default: shift c.cubesizemode" : "");
            str.Append("ReplaceModeInput2=").Append(replaceMode2 == null ? "" : replaceMode2.GetStringCombination()).AppendLine(comments ? " // Default: " : "");

            if(comments)
            {
                str.AppendLine();
                str.AppendLine("// List of inputs, generated from game data.");

                const int NEWLINE_EVERY_CHARACTERS = 130;

                int characters = 0;
                str.Append("// Key names: ").AppendLine().Append("//     ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.MOUSE_PREFIX, StringComparison.Ordinal)
                       || kv.Key.StartsWith(InputHandler.GAMEPAD_PREFIX, StringComparison.Ordinal)
                       || kv.Key.StartsWith(InputHandler.CONTROL_PREFIX, StringComparison.Ordinal))
                        continue;

                    int prevLen = str.Length;

                    str.Append(kv.Key).Append(", ");

                    characters += (str.Length - prevLen);
                    if(characters >= NEWLINE_EVERY_CHARACTERS)
                    {
                        str.AppendLine().Append("//     ");
                        characters = 0;
                    }
                }
                str.AppendLine();

                characters = 0;
                str.Append("// Mouse button names: ").AppendLine().Append("//     ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.MOUSE_PREFIX, StringComparison.Ordinal))
                    {
                        int prevLen = str.Length;

                        str.Append(kv.Key).Append(", ");

                        characters += (str.Length - prevLen);
                        if(characters >= NEWLINE_EVERY_CHARACTERS)
                        {
                            str.AppendLine().Append("//     ");
                            characters = 0;
                        }
                    }
                }
                str.AppendLine();

                characters = 0;
                str.Append("// Gamepad button/axes names: ").AppendLine().Append("//     ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.GAMEPAD_PREFIX, StringComparison.Ordinal))
                    {
                        int prevLen = str.Length;

                        str.Append(kv.Key).Append(", ");

                        characters += (str.Length - prevLen);
                        if(characters >= NEWLINE_EVERY_CHARACTERS)
                        {
                            str.AppendLine().Append("//     ");
                            characters = 0;
                        }
                    }
                }
                str.AppendLine();

                characters = 0;
                str.Append("// Control names: ").AppendLine().Append("//     ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.CONTROL_PREFIX, StringComparison.Ordinal))
                    {
                        int prevLen = str.Length;

                        str.Append(kv.Key).Append(", ");

                        characters += (str.Length - prevLen);
                        if(characters >= NEWLINE_EVERY_CHARACTERS)
                        {
                            str.AppendLine().Append("//     ");
                            characters = 0;
                        }
                    }
                }
                str.AppendLine();
            }

            return str.ToString();
        }

        public void Close()
        {
        }
    }
}
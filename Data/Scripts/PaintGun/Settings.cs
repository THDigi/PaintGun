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

        public ControlCombination colorPickMode1;
        public ControlCombination colorPickMode2;

        public ControlCombination instantColorPick1;
        public ControlCombination instantColorPick2;

        public ControlCombination replaceColorMode1;
        public ControlCombination replaceColorMode2;

        public ControlCombination default_colorPickMode1;
        public ControlCombination default_colorPickMode2;

        public ControlCombination default_instantColorPick1;
        public ControlCombination default_instantColorPick2;

        public ControlCombination default_replaceColorMode1;
        public ControlCombination default_replaceColorMode2;

        private char[] CHARS = new char[] { '=' };

        public bool firstLoad = false;

        public Settings()
        {
            // assign defaults
            default_colorPickMode1 = ControlCombination.CreateFrom("shift c.landinggear", true);
            default_colorPickMode2 = ControlCombination.CreateFrom("g.lb g.rb", true);

            default_instantColorPick1 = ControlCombination.CreateFrom("shift c.secondaryaction", true);
            default_instantColorPick2 = null;

            default_replaceColorMode1 = ControlCombination.CreateFrom("shift c.cubesizemode", true);
            default_replaceColorMode2 = null;

            // load the settings if they exist
            if(!Load())
            {
                firstLoad = true; // config didn't exist, assume it's the first time the mod is loaded
            }

            Save(); // refresh config in case of any missing or extra settings
        }

        public bool Load()
        {
            ResetToDefaults();

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

        private void ResetToDefaults()
        {
            colorPickMode1 = default_colorPickMode1;
            colorPickMode2 = default_colorPickMode2;

            instantColorPick1 = default_instantColorPick1;
            instantColorPick2 = default_instantColorPick2;

            replaceColorMode1 = default_replaceColorMode1;
            replaceColorMode2 = default_replaceColorMode2;
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

                    var key = args[0].Trim().ToLower();
                    var value = args[1];

                    switch(key)
                    {
                        case "configversion":
                            if(int.TryParse(value, out i))
                                prevConfigVersion = i;
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "extrasounds":
                            if(bool.TryParse(value, out b))
                                extraSounds = b;
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "sprayparticles":
                            if(bool.TryParse(value, out b))
                                sprayParticles = b;
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "spraysoundvolume":
                            if(float.TryParse(value, out f))
                                spraySoundVolume = MathHelper.Clamp(f, 0, 1);
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "selectcolorzigzag":
                            if(bool.TryParse(value, out b))
                                selectColorZigZag = b;
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "hidepalettewithhud":
                            if(bool.TryParse(value, out b))
                                hidePaletteWithHUD = b;
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "palettescreenpos":
                            var vars = value.Split(',');
                            double x, y;
                            if(vars.Length == 2 && double.TryParse(vars[0], out x) && double.TryParse(vars[1], out y))
                            {
                                if(prevConfigVersion < CFG_VERSION_NEWHUDDEFAULTS && x == 0.29d && y == -0.73d) // reset to default if config is older and had default setting
                                    paletteScreenPos = paletteScreenPosDefault;
                                else
                                    paletteScreenPos = new Vector2D(x, y);
                            }
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "palettescale":
                            if(float.TryParse(value, out f))
                            {
                                if(prevConfigVersion < CFG_VERSION_NEWHUDDEFAULTS && f == 1f) // reset to default if config is older and had default setting
                                    paletteScale = paletteScaleDefault;
                                else
                                    paletteScale = MathHelper.Clamp(f, -100, 100);
                            }
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "palettebackgroundopacity":
                            if(prevConfigVersion < CFG_VERSION_HUDBKOPACITYDEFAULTS || value.Trim().Equals("hud", StringComparison.CurrentCultureIgnoreCase))
                                paletteBackgroundOpacity = -1;
                            else if(float.TryParse(value, out f))
                                paletteBackgroundOpacity = MathHelper.Clamp(f, 0, 1);
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "pickcolorinput1": // backwards compatibility
                        case "pickcolorinput2": // backwards compatibility
                        case "pickcolormode-input1":
                        case "pickcolormode-input2":
                            {
                                var obj = ControlCombination.CreateFrom(value, true);
                                if(value.Length == 0 || obj != null)
                                {
                                    if(key.EndsWith("1"))
                                        colorPickMode1 = obj;
                                    else
                                        colorPickMode2 = obj;
                                }
                                else
                                    Log.Error("Invalid " + key + " value: " + value);
                                continue;
                            }
                        case "instantpickcolor-input1":
                        case "instantpickcolor-input2":
                            {
                                var obj = ControlCombination.CreateFrom(value, true);
                                if(value.Length == 0 || obj != null)
                                {
                                    if(key.EndsWith("1"))
                                        instantColorPick1 = obj;
                                    else
                                        instantColorPick2 = obj;
                                }
                                else
                                    Log.Error("Invalid " + key + " value: " + value);
                                continue;
                            }
                        case "replacecolormode-input1":
                        case "replacecolormode-input2":
                        case "replacemodeinput1": // backwards compatibility
                        case "replacemodeinput2": // backwards compatibility
                            {
                                var obj = ControlCombination.CreateFrom(value, true);
                                if(value.Length == 0 || obj != null)
                                {
                                    if(key.EndsWith("1"))
                                        replaceColorMode1 = obj;
                                    else
                                        replaceColorMode2 = obj;
                                }
                                else
                                    Log.Error("Invalid " + key + " value: " + value);
                                continue;
                            }
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
            str.Append("HidePaletteWithHUD=").Append(hidePaletteWithHUD).AppendLine(comments ? " // wether to hide the color palette and aim info along with the HUD. Set to false to always show regardless of the HUD being visible or not. Default: true" : "");
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
            str.Append("PickColorMode-input1=").Append(colorPickMode1?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_colorPickMode1?.GetStringCombination() ?? "") : "");
            str.Append("PickColorMode-input2=").Append(colorPickMode2?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_colorPickMode2?.GetStringCombination() ?? "") : "");

            if(comments)
            {
                str.AppendLine();
                str.AppendLine("// Key/mouse/gamepad combination to instantly get the aimed block/player's color into the selected slot.");
                str.AppendLine("// Same input rules as above.");
            }
            str.Append("InstantPickColor-input1=").Append(instantColorPick1?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_instantColorPick1?.GetStringCombination() ?? "") : "");
            str.Append("InstantPickColor-input2=").Append(instantColorPick2?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_instantColorPick2?.GetStringCombination() ?? "") : "");

            if(comments)
            {
                str.AppendLine();
                str.AppendLine("// Key/mouse/gamepad combination to toggle the replace color mode, which only works in creative.");
                str.AppendLine("// Same input rules as above.");
            }
            str.Append("ReplaceColorMode-input1=").Append(replaceColorMode1?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_replaceColorMode1?.GetStringCombination() ?? "") : "");
            str.Append("ReplaceColorMode-input2=").Append(replaceColorMode2?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_replaceColorMode2?.GetStringCombination() ?? "") : "");

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
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun.Features
{
    public class Settings : ModComponent
    {
        public event Action SettingsLoaded;

        private const string FILE = "paintgun.cfg";
        private const int CFG_VERSION = 3;
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
        public Vector2D aimInfoScreenPos = aimInfoScreenPosDefault;
        public float aimInfoScale = aimInfoScaleDefault;
        public float aimInfoBackgroundOpacity = -1;
        public bool requireCtrlForColorCycle = false;
        public HashSet<string> hideSkinsFromPalette = new HashSet<string>();
        public ControlCombination colorPickMode1;
        public ControlCombination colorPickMode2;
        public ControlCombination instantColorPick1;
        public ControlCombination instantColorPick2;
        public ControlCombination replaceColorMode1;
        public ControlCombination replaceColorMode2;

        public static readonly Vector2D paletteScreenPosDefault = new Vector2D(0.4345, -0.691);
        public static readonly float paletteScaleDefault = 0.5f;
        public static readonly Vector2D aimInfoScreenPosDefault = new Vector2D(0.642f, -0.22f);
        public static readonly float aimInfoScaleDefault = 1f;
        public ControlCombination default_colorPickMode1;
        public ControlCombination default_colorPickMode2;
        public ControlCombination default_instantColorPick1;
        public ControlCombination default_instantColorPick2;
        public ControlCombination default_replaceColorMode1;
        public ControlCombination default_replaceColorMode2;

        private void ResetToDefaults()
        {
            extraSounds = true;
            sprayParticles = true;
            spraySoundVolume = 0.8f;
            selectColorZigZag = false;
            hidePaletteWithHUD = true;

            paletteScreenPos = paletteScreenPosDefault;
            paletteScale = paletteScaleDefault;
            paletteBackgroundOpacity = -1;

            aimInfoScreenPos = aimInfoScreenPosDefault;
            aimInfoScale = aimInfoScaleDefault;
            aimInfoBackgroundOpacity = -1;
            requireCtrlForColorCycle = false;

            colorPickMode1 = default_colorPickMode1;
            colorPickMode2 = default_colorPickMode2;

            instantColorPick1 = default_instantColorPick1;
            instantColorPick2 = default_instantColorPick2;

            replaceColorMode1 = default_replaceColorMode1;
            replaceColorMode2 = default_replaceColorMode2;
        }

        private char[] CHARS = { '=' };
        private char[] SEPARATOR = { ',' };

        public bool firstLoad = false;

        StringBuilder sb = new StringBuilder(1024 * 8);

        public Settings(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
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

        protected override void UnregisterComponent()
        {
        }

        public bool Load()
        {
            TextReader file = null;
            bool success = false;

            try
            {
                ResetToDefaults();

                if(MyAPIGateway.Utilities.FileExistsInLocalStorage(FILE, typeof(Settings)))
                {
                    file = MyAPIGateway.Utilities.ReadFileInLocalStorage(FILE, typeof(Settings));
                    ReadSettings(file);

                    SettingsLoaded?.Invoke();
                    success = true;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
            finally
            {
                file?.Dispose();
            }

            UpdateToolDescription();
            Palette.ComputeShownSkins();

            return success;
        }

        private void UpdateToolDescription()
        {
            try
            {
                var defId = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), "PhysicalPaintGun");
                var itemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(defId);

                if(itemDef == null)
                    throw new Exception($"Can't find '{defId.ToString()}' hand item definition!");

                if(string.IsNullOrEmpty(itemDef.DescriptionText))
                    return;

                if(requireCtrlForColorCycle)
                {
                    if(itemDef.DescriptionEnum.HasValue)
                        itemDef.DescriptionEnum = MyStringId.GetOrCompute(itemDef.DescriptionEnum.Value.String.Replace("[Scroll]", "[Ctrl+Scroll]"));
                    else if(!string.IsNullOrEmpty(itemDef.DescriptionString))
                        itemDef.DescriptionString = itemDef.DescriptionString.Replace("[Scroll]", "[Ctrl+Scroll]");
                }
                else
                {
                    if(itemDef.DescriptionEnum.HasValue)
                        itemDef.DescriptionEnum = MyStringId.GetOrCompute(itemDef.DescriptionEnum.Value.String.Replace("[Ctrl+Scroll]", "[Scroll]"));
                    else if(!string.IsNullOrEmpty(itemDef.DescriptionString))
                        itemDef.DescriptionString = itemDef.DescriptionString.Replace("[Ctrl+Scroll]", "[Scroll]");
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
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
                        case "aiminfoscreenpos":
                            var vars = value.Split(SEPARATOR);
                            double x, y;
                            if(vars.Length == 2 && double.TryParse(vars[0], out x) && double.TryParse(vars[1], out y))
                            {
                                var vec = new Vector2D(x, y);

                                if(key == "aiminfoscreenpos")
                                {
                                    aimInfoScreenPos = vec;
                                }
                                else
                                {
                                    if(prevConfigVersion < CFG_VERSION_NEWHUDDEFAULTS && x == 0.29d && y == -0.73d) // reset to default if config is older and had default setting
                                        paletteScreenPos = paletteScreenPosDefault;
                                    else
                                        paletteScreenPos = vec;
                                }
                            }
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "palettescale":
                        case "aiminfoscale":
                            if(float.TryParse(value, out f))
                            {
                                f = MathHelper.Clamp(f, -100, 100);

                                if(key == "aiminfoscale")
                                {
                                    aimInfoScale = f;
                                }
                                else
                                {
                                    if(prevConfigVersion < CFG_VERSION_NEWHUDDEFAULTS && f == 1f) // reset to default if config is older and had default setting
                                        paletteScale = paletteScaleDefault;
                                    else
                                        paletteScale = f;
                                }
                            }
                            else
                                Log.Error("Invalid " + key + " value: " + value);
                            continue;
                        case "palettebackgroundopacity":
                        case "aiminfobackgroundopacity":
                        {
                            if(value.Trim().Equals("hud", StringComparison.CurrentCultureIgnoreCase))
                            {
                                f = -1;
                            }
                            else if(float.TryParse(value, out f))
                            {
                                f = MathHelper.Clamp(f, 0, 1);
                            }
                            else
                            {
                                Log.Error("Invalid " + key + " value: " + value);
                                continue;
                            }

                            if(key == "aiminfoscale")
                            {
                                aimInfoScale = f;
                            }
                            else
                            {
                                if(prevConfigVersion < CFG_VERSION_HUDBKOPACITYDEFAULTS)
                                    paletteBackgroundOpacity = -1;
                                else
                                    paletteBackgroundOpacity = f;
                            }
                            continue;
                        }
                        case "requirectrlforcolorcycle":
                            if(bool.TryParse(value, out b))
                                requireCtrlForColorCycle = b;
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
                        case "hideskinsfrompalette":
                        {
                            string[] values = value.Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
                            hideSkinsFromPalette.Clear();
                            foreach(var val in values)
                            {
                                hideSkinsFromPalette.Add(val.Trim());
                            }
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
            TextWriter file = null;

            try
            {
                file = MyAPIGateway.Utilities.WriteFileInLocalStorage(FILE, typeof(Settings));
                file.Write(GetSettingsString(true));
                file.Flush();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
            finally
            {
                file?.Dispose();
            }
        }

        public string GetSettingsString(bool comments)
        {
            sb.Clear();

            if(comments)
            {
                sb.Append("ConfigVersion=").Append(CFG_VERSION).AppendLine(comments ? " // Do not edit. This is used by the mod to reset settings when their defaults change without changing them if they're custom." : "");
                sb.AppendLine("// Paint Gun mod config; this file gets automatically overwritten after being loaded so don't leave custom comments.");
                sb.AppendLine("// You can reload this while the game is running by typing in chat: /pg reload");
                sb.AppendLine("// Lines starting with // are comments. All values are case insensitive unless otherwise specified.");
                sb.AppendLine();
            }

            sb.Append("ExtraSounds=").Append(extraSounds).AppendLine(comments ? " // toggle sounds: when aiming at a different color in color pick mode, when finishing painting in survival and when cycling colors. Default: true" : "");
            sb.Append("SprayParticles=").Append(sprayParticles).AppendLine(comments ? " // toggles the spray particles. Default: true" : "");
            sb.Append("SpraySoundVolume=").Append(spraySoundVolume).AppendLine(comments ? " // paint gun spraying sound volume. Default: 0.8" : "");
            sb.Append("SelectColorZigZag=").Append(selectColorZigZag).AppendLine(comments ? " // type of scrolling through colors in the palette, false is each row at a time, true is in zig-zag. Default: false" : "");
            sb.Append("HidePaletteWithHUD=").Append(hidePaletteWithHUD).AppendLine(comments ? " // wether to hide the color palette and aim info along with the HUD. Set to false to always show regardless of the HUD being visible or not. Default: true" : "");

            sb.Append("PaletteScreenPos=").Append(Math.Round(paletteScreenPos.X, 5)).Append(", ").Append(Math.Round(paletteScreenPos.Y, 5)).AppendLine(comments ? $" // color palette screen position in X and Y coordinates where 0,0 is the screen center. Positive values are right and up and negative ones are opposite of that. Default: {paletteScreenPos.X.ToString("0.#####")}, {paletteScreenPos.Y.ToString("0.#####")}" : "");
            sb.Append("PaletteScale=").Append(Math.Round(paletteScale, 5)).AppendLine(comments ? $" // color palette overall scale. Default: {paletteScaleDefault.ToString("0.#####")}" : "");
            sb.Append("PaletteBackgroundOpacity=").Append(paletteBackgroundOpacity < 0 ? "HUD" : Math.Round(paletteBackgroundOpacity, 5).ToString()).AppendLine(comments ? " // palette's background opacity percent scalar (0 to 1 value) or set to HUD to use the game's HUD opacity. Default: HUD" : "");

            sb.Append("AimInfoScreenPos=").Append(Math.Round(aimInfoScreenPos.X, 5)).Append(", ").Append(Math.Round(aimInfoScreenPos.Y, 5)).AppendLine(comments ? $" // aim info's screen position in X and Y coordinates where 0,0 is the screen center. Positive values are right and up and negative ones are opposite of that. Default: {aimInfoScreenPos.X.ToString("0.#####")}, {aimInfoScreenPos.Y.ToString("0.#####")}" : "");
            // TODO: make it work with scale?
            //str.Append("AimInfoScale=").Append(Math.Round(aimInfoScale, 5)).AppendLine(comments ? $" // aiming info box overall scale. Default: {aimInfoScaleDefault:0.#####}" : "");
            sb.Append("AimInfoBackgroundOpacity=").Append(aimInfoBackgroundOpacity < 0 ? "HUD" : Math.Round(aimInfoBackgroundOpacity, 5).ToString()).AppendLine(comments ? " // aim info's background opacity percent scalar (0 to 1 value) or set to HUD to use the game's HUD opacity. Default: HUD" : "");

            sb.Append("RequireCtrlForColorCycle=").Append(requireCtrlForColorCycle).AppendLine(comments ? " // Whether color cycling requires ctrl+scroll (true) or just scroll (false). Skin cycling (shift+scroll) is unaffected. Default: false" : "");

            if(comments)
            {
                sb.AppendLine();
                sb.AppendLine("// This allows you to hide certain skin IDs from the HUD palette, separated by comma and case-sensitive!");
                sb.Append("// All detected skins: ");

                int num = 99999; // start with a newline
                var skins = Palette.BlockSkins;
                for(int i = 1; i < skins.Count; ++i) // skipping index 0 intentionally
                {
                    if(++num > 7)
                    {
                        num = 0;
                        sb.AppendLine().Append("//     ");
                    }

                    var skin = skins[i];
                    sb.Append(skin.SubtypeId.String).Append(", ");
                }

                sb.Length -= 2; // remove last comma
                sb.AppendLine();
            }

            sb.Append("HideSkinsFromPalette=").Append(string.Join(", ", hideSkinsFromPalette)).AppendLine();

            if(comments)
            {
                sb.AppendLine();
                sb.AppendLine("// Key/mouse/gamepad combination to trigger '/pg pick' command.");
                sb.AppendLine("// Separate multiple keys/buttons/controls with spaces. For gamepad add " + InputHandler.GAMEPAD_PREFIX + " prefix, for mouse add " + InputHandler.MOUSE_PREFIX + " prefix and for game controls add " + InputHandler.CONTROL_PREFIX + " prefix.");
                sb.AppendLine("// All keys, mouse buttons, gamepad buttons/axes and control names are at the bottom of this file.");
            }
            sb.Append("PickColorMode-input1=").Append(colorPickMode1?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_colorPickMode1?.GetStringCombination() ?? "") : "");
            sb.Append("PickColorMode-input2=").Append(colorPickMode2?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_colorPickMode2?.GetStringCombination() ?? "") : "");

            if(comments)
            {
                sb.AppendLine();
                sb.AppendLine("// Key/mouse/gamepad combination to instantly get the aimed block/player's color into the selected slot.");
                sb.AppendLine("// Same input rules as above.");
            }
            sb.Append("InstantPickColor-input1=").Append(instantColorPick1?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_instantColorPick1?.GetStringCombination() ?? "") : "");
            sb.Append("InstantPickColor-input2=").Append(instantColorPick2?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_instantColorPick2?.GetStringCombination() ?? "") : "");

            if(comments)
            {
                sb.AppendLine();
                sb.AppendLine("// Key/mouse/gamepad combination to toggle the replace color mode, which only works in creative.");
                sb.AppendLine("// Same input rules as above.");
            }
            sb.Append("ReplaceColorMode-input1=").Append(replaceColorMode1?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_replaceColorMode1?.GetStringCombination() ?? "") : "");
            sb.Append("ReplaceColorMode-input2=").Append(replaceColorMode2?.GetStringCombination() ?? "").AppendLine(comments ? " // Default: " + (default_replaceColorMode2?.GetStringCombination() ?? "") : "");

            if(comments)
            {
                sb.AppendLine();
                sb.AppendLine("// List of inputs, generated from game data.");

                const int NEWLINE_EVERY_CHARACTERS = 130;

                int characters = 0;
                sb.Append("// Key names: ").AppendLine().Append("//     ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.MOUSE_PREFIX, StringComparison.Ordinal)
                       || kv.Key.StartsWith(InputHandler.GAMEPAD_PREFIX, StringComparison.Ordinal)
                       || kv.Key.StartsWith(InputHandler.CONTROL_PREFIX, StringComparison.Ordinal))
                        continue;

                    int prevLen = sb.Length;

                    sb.Append(kv.Key).Append(", ");

                    characters += (sb.Length - prevLen);
                    if(characters >= NEWLINE_EVERY_CHARACTERS)
                    {
                        sb.AppendLine().Append("//     ");
                        characters = 0;
                    }
                }
                sb.AppendLine();

                characters = 0;
                sb.Append("// Mouse button names: ").AppendLine().Append("//     ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.MOUSE_PREFIX, StringComparison.Ordinal))
                    {
                        int prevLen = sb.Length;

                        sb.Append(kv.Key).Append(", ");

                        characters += (sb.Length - prevLen);
                        if(characters >= NEWLINE_EVERY_CHARACTERS)
                        {
                            sb.AppendLine().Append("//     ");
                            characters = 0;
                        }
                    }
                }
                sb.AppendLine();

                characters = 0;
                sb.Append("// Gamepad button/axes names: ").AppendLine().Append("//     ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.GAMEPAD_PREFIX, StringComparison.Ordinal))
                    {
                        int prevLen = sb.Length;

                        sb.Append(kv.Key).Append(", ");

                        characters += (sb.Length - prevLen);
                        if(characters >= NEWLINE_EVERY_CHARACTERS)
                        {
                            sb.AppendLine().Append("//     ");
                            characters = 0;
                        }
                    }
                }
                sb.AppendLine();

                characters = 0;
                sb.Append("// Control names: ").AppendLine().Append("//     ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.CONTROL_PREFIX, StringComparison.Ordinal))
                    {
                        int prevLen = sb.Length;

                        sb.Append(kv.Key).Append(", ");

                        characters += (sb.Length - prevLen);
                        if(characters >= NEWLINE_EVERY_CHARACTERS)
                        {
                            sb.AppendLine().Append("//     ");
                            characters = 0;
                        }
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
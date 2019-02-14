using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using VRage.Game.ModAPI;

namespace Digi
{
    public class ControlCombination
    {
        public List<object> combination = new List<object>();
        public List<string> raw = new List<string>();
        public string combinationString = "";

        public ControlCombination() { }

        public string GetStringCombination()
        {
            return combinationString;
        }

        public string GetFriendlyString(bool xboxChars = true)
        {
            var combined = new List<string>();

            foreach(var o in combination)
            {
                if(o is MyStringId)
                {
                    var control = MyAPIGateway.Input.GetGameControl((MyStringId)o);

                    if(control.GetMouseControl() != MyMouseButtonsEnum.None)
                    {
                        combined.Add(InputHandler.inputNames[control.GetMouseControl()]);
                    }
                    else if(control.GetKeyboardControl() != MyKeys.None)
                    {
                        combined.Add(InputHandler.inputNames[control.GetKeyboardControl()]);
                    }
                    else if(control.GetSecondKeyboardControl() != MyKeys.None)
                    {
                        combined.Add(InputHandler.inputNames[control.GetSecondKeyboardControl()]);
                    }
                    else
                    {
                        combined.Add(InputHandler.inputNames[control]);
                    }
                }
                else if(xboxChars && (o is MyJoystickAxesEnum || o is MyJoystickButtonsEnum))
                {
                    char c = InputHandler.xboxCodes.GetValueOrDefault(o, ' ');
                    combined.Add(c == ' ' ? InputHandler.inputNames[o] : c.ToString());
                }
                else
                {
                    combined.Add(InputHandler.inputNames[o]);
                }
            }

            return String.Join(" ", combined);
        }

        public static ControlCombination CreateFrom(string combinationString, bool logErrors = false)
        {
            if(combinationString == null)
                return null;

            string[] data = combinationString.ToLower().Split(' ');

            if(data.Length == 0)
                return null;

            var obj = new ControlCombination();

            for(int d = 0; d < data.Length; d++)
            {
                var s = data[d].Trim();

                if(s.Length == 0 || obj.raw.Contains(s))
                    continue;

                object o;

                if(InputHandler.inputs.TryGetValue(s, out o))
                {
                    obj.raw.Add(s);
                    obj.combination.Add(o);
                }
                else
                {
                    if(logErrors)
                        Log.Info("WARNING: Input not found: " + s);

                    return null;
                }
            }

            obj.combinationString = String.Join(" ", obj.raw);
            return obj;
        }
    }

    public static class InputHandler
    {
        public static Dictionary<string, object> inputs = null;
        public static Dictionary<object, string> inputNames = null;
        public static Dictionary<string, string> inputNiceNames = null;
        public static List<object> inputValuesList = null;
        public static List<string> inputAnalogList = null;
        public static Dictionary<object, char> xboxCodes = null;
        public const string MOUSE_PREFIX = "m.";
        public const string GAMEPAD_PREFIX = "g.";
        public const string CONTROL_PREFIX = "c.";

        private static readonly StringBuilder tmp = new StringBuilder();

        private const float EPSILON = 0.000001f;

        static InputHandler()
        {
            inputs = new Dictionary<string, object>()
            {
                // keyboard: keys
                {"ctrl", MyKeys.Control},
                {"leftctrl", MyKeys.LeftControl},
                {"rightctrl", MyKeys.RightControl},
                {"shift", MyKeys.Shift},
                {"leftshift", MyKeys.LeftShift},
                {"rightshift", MyKeys.RightShift},
                {"alt", MyKeys.Alt},
                {"leftalt", MyKeys.LeftAlt},
                {"rightalt", MyKeys.RightAlt},
                {"apps", MyKeys.Apps},
                {"up", MyKeys.Up},
                {"down", MyKeys.Down},
                {"left", MyKeys.Left},
                {"right", MyKeys.Right},
                {"a", MyKeys.A},
                {"b", MyKeys.B},
                {"c", MyKeys.C},
                {"d", MyKeys.D},
                {"e", MyKeys.E},
                {"f", MyKeys.F},
                {"g", MyKeys.G},
                {"h", MyKeys.H},
                {"i", MyKeys.I},
                {"j", MyKeys.J},
                {"k", MyKeys.K},
                {"l", MyKeys.L},
                {"m", MyKeys.M},
                {"n", MyKeys.N},
                {"o", MyKeys.O},
                {"p", MyKeys.P},
                {"q", MyKeys.Q},
                {"r", MyKeys.R},
                {"s", MyKeys.S},
                {"t", MyKeys.T},
                {"u", MyKeys.U},
                {"v", MyKeys.V},
                {"w", MyKeys.W},
                {"x", MyKeys.X},
                {"y", MyKeys.Y},
                {"z", MyKeys.Z},
                {"0", MyKeys.D0},
                {"1", MyKeys.D1},
                {"2", MyKeys.D2},
                {"3", MyKeys.D3},
                {"4", MyKeys.D4},
                {"5", MyKeys.D5},
                {"6", MyKeys.D6},
                {"7", MyKeys.D7},
                {"8", MyKeys.D8},
                {"9", MyKeys.D9},
                {"f1", MyKeys.F1},
                {"f2", MyKeys.F2},
                {"f3", MyKeys.F3},
                {"f4", MyKeys.F4},
                {"f5", MyKeys.F5},
                {"f6", MyKeys.F6},
                {"f7", MyKeys.F7},
                {"f8", MyKeys.F8},
                {"f9", MyKeys.F9},
                {"f10", MyKeys.F10},
                {"f11", MyKeys.F11},
                {"f12", MyKeys.F12},
                {"num0", MyKeys.NumPad0},
                {"num1", MyKeys.NumPad1},
                {"num2", MyKeys.NumPad2},
                {"num3", MyKeys.NumPad3},
                {"num4", MyKeys.NumPad4},
                {"num5", MyKeys.NumPad5},
                {"num6", MyKeys.NumPad6},
                {"num7", MyKeys.NumPad7},
                {"num8", MyKeys.NumPad8},
                {"num9", MyKeys.NumPad9},
                {"nummultiply", MyKeys.Multiply},
                {"numsubtract", MyKeys.Subtract},
                {"numadd", MyKeys.Add},
                {"numdivide", MyKeys.Divide},
                {"numdecimal", MyKeys.Decimal},
                {"backslash", MyKeys.OemBackslash},
                {"comma", MyKeys.OemComma},
                {"minus", MyKeys.OemMinus},
                {"period", MyKeys.OemPeriod},
                {"pipe", MyKeys.OemPipe},
                {"plus", MyKeys.OemPlus},
                {"question", MyKeys.OemQuestion},
                {"quote", MyKeys.OemQuotes},
                {"semicolon", MyKeys.OemSemicolon},
                {"openbrackets", MyKeys.OemOpenBrackets},
                {"closebrackets", MyKeys.OemCloseBrackets},
                {"tilde", MyKeys.OemTilde},
                {"tab", MyKeys.Tab},
                {"capslock", MyKeys.CapsLock},
                {"enter", MyKeys.Enter},
                {"backspace", MyKeys.Back},
                {"space", MyKeys.Space},
                {"delete", MyKeys.Delete},
                {"insert", MyKeys.Insert},
                {"home", MyKeys.Home},
                {"end", MyKeys.End},
                {"pageup", MyKeys.PageUp},
                {"pagedown", MyKeys.PageDown},
                {"scrollock", MyKeys.ScrollLock},
                //{"print", MyKeys.Print}, or {"snapshot", MyKeys.Snapshot},
                {"pause", MyKeys.Pause},
                
                // mouse: buttons
                {MOUSE_PREFIX+"left", MyMouseButtonsEnum.Left},
                {MOUSE_PREFIX+"right", MyMouseButtonsEnum.Right},
                {MOUSE_PREFIX+"middle", MyMouseButtonsEnum.Middle},
                {MOUSE_PREFIX+"button4", MyMouseButtonsEnum.XButton1},
                {MOUSE_PREFIX+"button5", MyMouseButtonsEnum.XButton2},
                {MOUSE_PREFIX+"analog", MOUSE_PREFIX+"analog"},
                {MOUSE_PREFIX+"scroll", MOUSE_PREFIX+"scroll"},
                {MOUSE_PREFIX+"scrollup", MOUSE_PREFIX+"scrollup"},
                {MOUSE_PREFIX+"scrolldown", MOUSE_PREFIX+"scrolldown"},
                {MOUSE_PREFIX+"x", MOUSE_PREFIX+"x"},
                {MOUSE_PREFIX+"y", MOUSE_PREFIX+"y"},
                {MOUSE_PREFIX+"x+", MOUSE_PREFIX+"x+"},
                {MOUSE_PREFIX+"x-", MOUSE_PREFIX+"x-"},
                {MOUSE_PREFIX+"y+", MOUSE_PREFIX+"y+"},
                {MOUSE_PREFIX+"y-", MOUSE_PREFIX+"y-"},
                
                // gamepad
                {GAMEPAD_PREFIX+"a", MyJoystickButtonsEnum.J01},
                {GAMEPAD_PREFIX+"b", MyJoystickButtonsEnum.J02},
                {GAMEPAD_PREFIX+"x", MyJoystickButtonsEnum.J03},
                {GAMEPAD_PREFIX+"y", MyJoystickButtonsEnum.J04},
                {GAMEPAD_PREFIX+"back", MyJoystickButtonsEnum.J07},
                {GAMEPAD_PREFIX+"start", MyJoystickButtonsEnum.J08},
                {GAMEPAD_PREFIX+"lb", MyJoystickButtonsEnum.J05},
                {GAMEPAD_PREFIX+"rb", MyJoystickButtonsEnum.J06},
                {GAMEPAD_PREFIX+"lt", MyJoystickAxesEnum.Zpos},
                {GAMEPAD_PREFIX+"rt", MyJoystickAxesEnum.Zneg},
                {GAMEPAD_PREFIX+"ltanalog", GAMEPAD_PREFIX+"ltanalog"},
                {GAMEPAD_PREFIX+"rtanalog", GAMEPAD_PREFIX+"rtanalog"},
                {GAMEPAD_PREFIX+"ls", MyJoystickButtonsEnum.J09},
                {GAMEPAD_PREFIX+"rs", MyJoystickButtonsEnum.J10},
                {GAMEPAD_PREFIX+"lsanalog", GAMEPAD_PREFIX+"lsanalog"},
                {GAMEPAD_PREFIX+"rsanalog", GAMEPAD_PREFIX+"rsanalog"},
                {GAMEPAD_PREFIX+"dpadup", MyJoystickButtonsEnum.JDUp},
                {GAMEPAD_PREFIX+"dpaddown", MyJoystickButtonsEnum.JDDown},
                {GAMEPAD_PREFIX+"dpadleft", MyJoystickButtonsEnum.JDLeft},
                {GAMEPAD_PREFIX+"dpadright", MyJoystickButtonsEnum.JDRight},
                {GAMEPAD_PREFIX+"lsup", MyJoystickAxesEnum.Yneg},
                {GAMEPAD_PREFIX+"lsdown", MyJoystickAxesEnum.Ypos},
                {GAMEPAD_PREFIX+"lsleft", MyJoystickAxesEnum.Xneg},
                {GAMEPAD_PREFIX+"lsright", MyJoystickAxesEnum.Xpos},
                {GAMEPAD_PREFIX+"rsup", MyJoystickAxesEnum.RotationYneg},
                {GAMEPAD_PREFIX+"rsdown", MyJoystickAxesEnum.RotationYpos},
                {GAMEPAD_PREFIX+"rsleft", MyJoystickAxesEnum.RotationXneg},
                {GAMEPAD_PREFIX+"rsright", MyJoystickAxesEnum.RotationXpos},
                
                // gamepad: unknown
                {GAMEPAD_PREFIX+"j11", MyJoystickButtonsEnum.J11},
                {GAMEPAD_PREFIX+"j12", MyJoystickButtonsEnum.J12},
                {GAMEPAD_PREFIX+"j13", MyJoystickButtonsEnum.J13},
                {GAMEPAD_PREFIX+"j14", MyJoystickButtonsEnum.J14},
                {GAMEPAD_PREFIX+"j15", MyJoystickButtonsEnum.J15},
                {GAMEPAD_PREFIX+"j16", MyJoystickButtonsEnum.J16},
                {GAMEPAD_PREFIX+"rotz+", MyJoystickAxesEnum.RotationZpos},
                {GAMEPAD_PREFIX+"rotz-", MyJoystickAxesEnum.RotationZneg},
                {GAMEPAD_PREFIX+"slider1+", MyJoystickAxesEnum.Slider1pos},
                {GAMEPAD_PREFIX+"slider1-", MyJoystickAxesEnum.Slider1neg},
                {GAMEPAD_PREFIX+"slider2+", MyJoystickAxesEnum.Slider2pos},
                {GAMEPAD_PREFIX+"slider2-", MyJoystickAxesEnum.Slider2neg},
                
                // game controls
                {CONTROL_PREFIX+"view", CONTROL_PREFIX+"view"},
                {CONTROL_PREFIX+"forward", MyControlsSpace.FORWARD},
                {CONTROL_PREFIX+"backward", MyControlsSpace.BACKWARD},
                {CONTROL_PREFIX+"strafeleft", MyControlsSpace.STRAFE_LEFT},
                {CONTROL_PREFIX+"straferight", MyControlsSpace.STRAFE_RIGHT},
                {CONTROL_PREFIX+"rollleft", MyControlsSpace.ROLL_LEFT},
                {CONTROL_PREFIX+"rollright", MyControlsSpace.ROLL_RIGHT},
                {CONTROL_PREFIX+"sprint", MyControlsSpace.SPRINT},
                {CONTROL_PREFIX+"primaryaction", MyControlsSpace.PRIMARY_TOOL_ACTION},
                {CONTROL_PREFIX+"secondaryaction", MyControlsSpace.SECONDARY_TOOL_ACTION},
                {CONTROL_PREFIX+"jump", MyControlsSpace.JUMP},
                {CONTROL_PREFIX+"crouch", MyControlsSpace.CROUCH},
                {CONTROL_PREFIX+"walk", MyControlsSpace.SWITCH_WALK},
                {CONTROL_PREFIX+"use", MyControlsSpace.USE},
                {CONTROL_PREFIX+"terminal", MyControlsSpace.TERMINAL},
                {CONTROL_PREFIX+"inventory", MyControlsSpace.INVENTORY},
                {CONTROL_PREFIX+"controlmenu", MyControlsSpace.CONTROL_MENU},
                {CONTROL_PREFIX+"factions", MyControlsSpace.FACTIONS_MENU},
                {CONTROL_PREFIX+"lookleft", MyControlsSpace.ROTATION_LEFT},
                {CONTROL_PREFIX+"lookright", MyControlsSpace.ROTATION_RIGHT},
                {CONTROL_PREFIX+"lookup", MyControlsSpace.ROTATION_UP},
                {CONTROL_PREFIX+"lookdown", MyControlsSpace.ROTATION_DOWN},
                {CONTROL_PREFIX+"light", MyControlsSpace.HEADLIGHTS},
                {CONTROL_PREFIX+"helmet", MyControlsSpace.HELMET},
                {CONTROL_PREFIX+"thrusts", MyControlsSpace.THRUSTS},
                {CONTROL_PREFIX+"damping", MyControlsSpace.DAMPING},
                {CONTROL_PREFIX+"broadcasting", MyControlsSpace.BROADCASTING},
                {CONTROL_PREFIX+"reactors", MyControlsSpace.TOGGLE_REACTORS},
                {CONTROL_PREFIX+"landinggear", MyControlsSpace.LANDING_GEAR},
                {CONTROL_PREFIX+"lookaround", MyControlsSpace.LOOKAROUND},
                {CONTROL_PREFIX+"cameramode", MyControlsSpace.CAMERA_MODE},
                {CONTROL_PREFIX+"buildmenu", MyControlsSpace.BUILD_SCREEN},
                {CONTROL_PREFIX+"paint", MyControlsSpace.CUBE_COLOR_CHANGE},
                {CONTROL_PREFIX+"switchleft", MyControlsSpace.SWITCH_LEFT}, // previous color or cam
                {CONTROL_PREFIX+"switchright", MyControlsSpace.SWITCH_RIGHT}, // next color or cam
                {CONTROL_PREFIX+"slot1", MyControlsSpace.SLOT1},
                {CONTROL_PREFIX+"slot2", MyControlsSpace.SLOT2},
                {CONTROL_PREFIX+"slot3", MyControlsSpace.SLOT3},
                {CONTROL_PREFIX+"slot4", MyControlsSpace.SLOT4},
                {CONTROL_PREFIX+"slot5", MyControlsSpace.SLOT5},
                {CONTROL_PREFIX+"slot6", MyControlsSpace.SLOT6},
                {CONTROL_PREFIX+"slot7", MyControlsSpace.SLOT7},
                {CONTROL_PREFIX+"slot8", MyControlsSpace.SLOT8},
                {CONTROL_PREFIX+"slot9", MyControlsSpace.SLOT9},
                {CONTROL_PREFIX+"slot0", MyControlsSpace.SLOT0},
                {CONTROL_PREFIX+"nexttoolbar", MyControlsSpace.TOOLBAR_UP},
                {CONTROL_PREFIX+"prevtoolbar", MyControlsSpace.TOOLBAR_DOWN},
                {CONTROL_PREFIX+"nextitem", MyControlsSpace.TOOLBAR_NEXT_ITEM},
                {CONTROL_PREFIX+"previtem", MyControlsSpace.TOOLBAR_PREV_ITEM},
                {CONTROL_PREFIX+"cubesizemode", MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE},
                {CONTROL_PREFIX+"stationrotation", MyControlsSpace.FREE_ROTATION},
                {CONTROL_PREFIX+"cyclesymmetry", MyControlsSpace.SYMMETRY_SWITCH},
                {CONTROL_PREFIX+"symmetry", MyControlsSpace.USE_SYMMETRY},
                {CONTROL_PREFIX+"cuberotatey+", MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE},
                {CONTROL_PREFIX+"cuberotatey-", MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE},
                {CONTROL_PREFIX+"cuberotatex+", MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE},
                {CONTROL_PREFIX+"cuberotatex-", MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE},
                {CONTROL_PREFIX+"cuberotatez+", MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE},
                {CONTROL_PREFIX+"cuberotatez-", MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE},
                {CONTROL_PREFIX+"togglehud", MyControlsSpace.TOGGLE_HUD},
                {CONTROL_PREFIX+"togglesignals", MyControlsSpace.TOGGLE_SIGNALS},
                {CONTROL_PREFIX+"missionsettings", MyControlsSpace.MISSION_SETTINGS},
                {CONTROL_PREFIX+"suicide", MyControlsSpace.SUICIDE},
                {CONTROL_PREFIX+"chat", MyControlsSpace.CHAT_SCREEN},
                {CONTROL_PREFIX+"pause", MyControlsSpace.PAUSE_GAME},
                {CONTROL_PREFIX+"screenshot", MyControlsSpace.SCREENSHOT},
                {CONTROL_PREFIX+"console", MyControlsSpace.CONSOLE},
                {CONTROL_PREFIX+"help", MyControlsSpace.HELP_SCREEN},
                {CONTROL_PREFIX+"specnone", MyControlsSpace.SPECTATOR_NONE},
                {CONTROL_PREFIX+"specdelta", MyControlsSpace.SPECTATOR_DELTA},
                {CONTROL_PREFIX+"specfree", MyControlsSpace.SPECTATOR_FREE},
                {CONTROL_PREFIX+"specstatic", MyControlsSpace.SPECTATOR_STATIC},
                
                // unknown controls:
                //{CONTROL_PREFIX+"pickup", MyControlsSpace.PICK_UP},
                //{CONTROL_PREFIX+"copypaste", MyControlsSpace.COPY_PASTE_ACTION},
                //{CONTROL_PREFIX+"voicechat", MyControlsSpace.VOICE_CHAT},
                //{CONTROL_PREFIX+"voxelpaint", MyControlsSpace.VOXEL_PAINT},
                //{CONTROL_PREFIX+"compoundmode", MyControlsSpace.SWITCH_COMPOUND},
                //{CONTROL_PREFIX+"voxelhandsettings", MyControlsSpace.VOXEL_HAND_SETTINGS},
                //{CONTROL_PREFIX+"buildmode", MyControlsSpace.BUILD_MODE},
                //{CONTROL_PREFIX+"buildingmode", MyControlsSpace.SWITCH_BUILDING_MODE},
                //{CONTROL_PREFIX+"nextblockstage", MyControlsSpace.NEXT_BLOCK_STAGE},
                //{CONTROL_PREFIX+"prevblockstage", MyControlsSpace.PREV_BLOCK_STAGE},
                //{CONTROL_PREFIX+"movecloser", MyControlsSpace.MOVE_CLOSER}, // move block closer or further, undetectable
                //{CONTROL_PREFIX+"movefurther", MyControlsSpace.MOVE_FURTHER},
                //{CONTROL_PREFIX+"primarybuildaction", MyControlsSpace.PRIMARY_BUILD_ACTION}, // doesn't seem to be usable
                //{CONTROL_PREFIX+"secondarybuildaction", MyControlsSpace.SECONDARY_BUILD_ACTION},
            };

            inputAnalogList = new List<string>()
            {
                {MOUSE_PREFIX+"analog"},
                {MOUSE_PREFIX+"x"},
                {MOUSE_PREFIX+"y"},
                {MOUSE_PREFIX+"scroll"},

                {GAMEPAD_PREFIX+"lsanalog"},
                {GAMEPAD_PREFIX+"rsanalog"},
                {GAMEPAD_PREFIX+"ltanalog"},
                {GAMEPAD_PREFIX+"rtanalog"},
            };

            inputNames = new Dictionary<object, string>();
            inputValuesList = new List<object>();

            foreach(var kv in inputs)
            {
                if(!inputNames.ContainsKey(kv.Value))
                {
                    inputNames.Add(kv.Value, kv.Key);
                    inputValuesList.Add(kv.Value);
                }
            }

            inputNiceNames = new Dictionary<string, string>();

            foreach(var kv in inputs)
            {
                inputNiceNames.Add(kv.Key, (kv.Value is MyKeys ? char.ToUpper(kv.Key[0]) + kv.Key.Substring(1) : char.ToUpper(kv.Key[2]) + kv.Key.Substring(3)));
            }

            inputNiceNames["leftctrl"] = "Left Ctrl";
            inputNiceNames["leftshift"] = "Left Shift";
            inputNiceNames["leftalt"] = "Left Alt";
            inputNiceNames["rightctrl"] = "Right Ctrl";
            inputNiceNames["rightshift"] = "Right Shift";
            inputNiceNames["rightalt"] = "Right Alt";
            inputNiceNames["num0"] = "Numpad 0";
            inputNiceNames["num1"] = "Numpad 1";
            inputNiceNames["num2"] = "Numpad 2";
            inputNiceNames["num3"] = "Numpad 3";
            inputNiceNames["num4"] = "Numpad 4";
            inputNiceNames["num5"] = "Numpad 5";
            inputNiceNames["num6"] = "Numpad 6";
            inputNiceNames["num7"] = "Numpad 7";
            inputNiceNames["num8"] = "Numpad 8";
            inputNiceNames["num9"] = "Numpad 9";
            inputNiceNames["nummultiply"] = "Numpad *";
            inputNiceNames["numsubtract"] = "Numpad -";
            inputNiceNames["numadd"] = "Numpad +";
            inputNiceNames["numdivide"] = "Numpad /";
            inputNiceNames["numdecimal"] = "Numpad .";
            inputNiceNames["backslash"] = "/";
            inputNiceNames["comma"] = ",";
            inputNiceNames["minus"] = "-";
            inputNiceNames["period"] = ".";
            inputNiceNames["pipe"] = "|";
            inputNiceNames["plus"] = "+";
            inputNiceNames["question"] = "?";
            inputNiceNames["quote"] = "\"";
            inputNiceNames["semicolon"] = ";";
            inputNiceNames["openbrackets"] = "{";
            inputNiceNames["closebrackets"] = "}";
            inputNiceNames["tilde"] = "`";
            inputNiceNames["pageup"] = "Page Up";
            inputNiceNames["pagedown"] = "Page Down";
            inputNiceNames["capslock"] = "Caps Lock";
            inputNiceNames["scrollock"] = "Scroll Lock";

            inputNiceNames[MOUSE_PREFIX + "left"] = "Left Click";
            inputNiceNames[MOUSE_PREFIX + "right"] = "Right Click";
            inputNiceNames[MOUSE_PREFIX + "middle"] = "Middle Click";
            inputNiceNames[MOUSE_PREFIX + "button4"] = "Button 4";
            inputNiceNames[MOUSE_PREFIX + "button5"] = "Button 5";
            inputNiceNames[MOUSE_PREFIX + "analog"] = "X,Y,Scroll (analog)";
            inputNiceNames[MOUSE_PREFIX + "x"] = "X axis (analog)";
            inputNiceNames[MOUSE_PREFIX + "y"] = "Y axis (analog)";
            inputNiceNames[MOUSE_PREFIX + "scroll"] = "Scroll (analog)";
            inputNiceNames[MOUSE_PREFIX + "scrollup"] = "Scroll Up";
            inputNiceNames[MOUSE_PREFIX + "scrolldown"] = "Scroll Down";
            inputNiceNames[MOUSE_PREFIX + "x+"] = "X+ axis";
            inputNiceNames[MOUSE_PREFIX + "x-"] = "X- axis";
            inputNiceNames[MOUSE_PREFIX + "y+"] = "Y+ axis";
            inputNiceNames[MOUSE_PREFIX + "y-"] = "Y- axis";

            inputNiceNames[GAMEPAD_PREFIX + "lb"] = "Left Bumper";
            inputNiceNames[GAMEPAD_PREFIX + "rb"] = "Right Bumper";
            inputNiceNames[GAMEPAD_PREFIX + "lt"] = "Left Trigger";
            inputNiceNames[GAMEPAD_PREFIX + "rt"] = "Right Trigger";
            inputNiceNames[GAMEPAD_PREFIX + "ltanalog"] = "Left Trigger (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "rtanalog"] = "Right Trigger (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "ls"] = "Left Stick";
            inputNiceNames[GAMEPAD_PREFIX + "rs"] = "Right Stick";
            inputNiceNames[GAMEPAD_PREFIX + "lsanalog"] = "Left Stick (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "rsanalog"] = "Right Stick (analog)";
            inputNiceNames[GAMEPAD_PREFIX + "dpadup"] = "D-pad Up";
            inputNiceNames[GAMEPAD_PREFIX + "dpaddown"] = "D-pad Down";
            inputNiceNames[GAMEPAD_PREFIX + "dpadleft"] = "D-pad Left";
            inputNiceNames[GAMEPAD_PREFIX + "dpadright"] = "D-pad Right";
            inputNiceNames[GAMEPAD_PREFIX + "lsup"] = "Left Stick Up";
            inputNiceNames[GAMEPAD_PREFIX + "lsdown"] = "Left Stick Down";
            inputNiceNames[GAMEPAD_PREFIX + "lsleft"] = "Left Stick Left";
            inputNiceNames[GAMEPAD_PREFIX + "lsright"] = "Left Stick Right";
            inputNiceNames[GAMEPAD_PREFIX + "rsup"] = "Right Stick Up";
            inputNiceNames[GAMEPAD_PREFIX + "rsdown"] = "Right Stick Down";
            inputNiceNames[GAMEPAD_PREFIX + "rsleft"] = "Right Stick Left";
            inputNiceNames[GAMEPAD_PREFIX + "rsright"] = "Right Stick Right";

            inputNiceNames[CONTROL_PREFIX + "view"] = "View (analog)";
            inputNiceNames[CONTROL_PREFIX + "strafeleft"] = "Strafe Left";
            inputNiceNames[CONTROL_PREFIX + "straferight"] = "Strafe Right";
            inputNiceNames[CONTROL_PREFIX + "jump"] = "Up/Jump";
            inputNiceNames[CONTROL_PREFIX + "crouch"] = "Down/Crouch";
            inputNiceNames[CONTROL_PREFIX + "rollleft"] = "Roll Left";
            inputNiceNames[CONTROL_PREFIX + "rollright"] = "Roll Right";
            inputNiceNames[CONTROL_PREFIX + "use"] = "Use/Interact";
            inputNiceNames[CONTROL_PREFIX + "primaryaction"] = "Use Tool/Fire weapon";
            inputNiceNames[CONTROL_PREFIX + "secondaryaction"] = "Secondary Mode";
            inputNiceNames[CONTROL_PREFIX + "lookleft"] = "Rotate Left";
            inputNiceNames[CONTROL_PREFIX + "lookright"] = "Rotate Right";
            inputNiceNames[CONTROL_PREFIX + "lookup"] = "Rotate Up";
            inputNiceNames[CONTROL_PREFIX + "lookdown"] = "Rotate Down";
            inputNiceNames[CONTROL_PREFIX + "controlmenu"] = "Control Menu";
            inputNiceNames[CONTROL_PREFIX + "lookaround"] = "Look Around";
            inputNiceNames[CONTROL_PREFIX + "stationrotation"] = "Station Rotation";
            inputNiceNames[CONTROL_PREFIX + "buildmenu"] = "Build Menu";
            inputNiceNames[CONTROL_PREFIX + "cuberotatey+"] = "Cube Rotate Y+";
            inputNiceNames[CONTROL_PREFIX + "cuberotatey-"] = "Cube Rotate Y-";
            inputNiceNames[CONTROL_PREFIX + "cuberotatex+"] = "Cube Rotate X+";
            inputNiceNames[CONTROL_PREFIX + "cuberotatex-"] = "Cube Rotate X-";
            inputNiceNames[CONTROL_PREFIX + "cuberotatez+"] = "Cube Rotate Z+";
            inputNiceNames[CONTROL_PREFIX + "cuberotatez-"] = "Cube Rotate Z-";
            inputNiceNames[CONTROL_PREFIX + "missionsettings"] = "Scenario Settings";
            inputNiceNames[CONTROL_PREFIX + "cockpitbuild"] = "Cockpit Build";
            inputNiceNames[CONTROL_PREFIX + "nexttoolbar"] = "Next Toolbar";
            inputNiceNames[CONTROL_PREFIX + "prevtoolbar"] = "Previous Toolbar";
            inputNiceNames[CONTROL_PREFIX + "nextitem"] = "Next Toolbar Item";
            inputNiceNames[CONTROL_PREFIX + "previtem"] = "Prev Toolbar Item";
            inputNiceNames[CONTROL_PREFIX + "switchleft"] = "Next Camera/Color";
            inputNiceNames[CONTROL_PREFIX + "switchright"] = "Prev Camera/Color";
            inputNiceNames[CONTROL_PREFIX + "damping"] = "Dampeners";
            inputNiceNames[CONTROL_PREFIX + "thrusts"] = "Jetpack";
            inputNiceNames[CONTROL_PREFIX + "light"] = "Toggle Lights";
            inputNiceNames[CONTROL_PREFIX + "togglehud"] = "Toggle HUD";
            inputNiceNames[CONTROL_PREFIX + "togglesignals"] = "Toggle Signals";
            inputNiceNames[CONTROL_PREFIX + "cameramode"] = "Camera Mode";
            inputNiceNames[CONTROL_PREFIX + "paint"] = "Paint/Weapon Mode";
            inputNiceNames[CONTROL_PREFIX + "slot0"] = "Slot 0/Unequip";
            inputNiceNames[CONTROL_PREFIX + "slot1"] = "Slot 1";
            inputNiceNames[CONTROL_PREFIX + "slot2"] = "Slot 2";
            inputNiceNames[CONTROL_PREFIX + "slot3"] = "Slot 3";
            inputNiceNames[CONTROL_PREFIX + "slot4"] = "Slot 4";
            inputNiceNames[CONTROL_PREFIX + "slot5"] = "Slot 5";
            inputNiceNames[CONTROL_PREFIX + "slot6"] = "Slot 6";
            inputNiceNames[CONTROL_PREFIX + "slot7"] = "Slot 7";
            inputNiceNames[CONTROL_PREFIX + "slot8"] = "Slot 8";
            inputNiceNames[CONTROL_PREFIX + "slot9"] = "Slot 9";
            inputNiceNames[CONTROL_PREFIX + "cyclesymmetry"] = "Cycle Symmetry";
            inputNiceNames[CONTROL_PREFIX + "landinggear"] = "Landing Gear/Color Menu";
            inputNiceNames[CONTROL_PREFIX + "reactors"] = "Toggle ship power";
            inputNiceNames[CONTROL_PREFIX + "specnone"] = "Spectator Off";
            inputNiceNames[CONTROL_PREFIX + "specdelta"] = "Delta Spectator";
            inputNiceNames[CONTROL_PREFIX + "specfree"] = "Free Spectator";
            inputNiceNames[CONTROL_PREFIX + "specstatic"] = "Static Spectator";

            xboxCodes = new Dictionary<object, char>
            {
                // buttons
                { MyJoystickButtonsEnum.J01, '\xe001' },
                { MyJoystickButtonsEnum.J02, '\xe003' },
                { MyJoystickButtonsEnum.J03, '\xe002' },
                { MyJoystickButtonsEnum.J04, '\xe004' },
                { MyJoystickButtonsEnum.J05, '\xe005' },
                { MyJoystickButtonsEnum.J06, '\xe006' },
                { MyJoystickButtonsEnum.J07, '\xe00d' },
                { MyJoystickButtonsEnum.J08, '\xe00e' },
                { MyJoystickButtonsEnum.J09, '\xe00b' },
                { MyJoystickButtonsEnum.J10, '\xe00c' },
                { MyJoystickButtonsEnum.JDLeft, '\xe010' },
                { MyJoystickButtonsEnum.JDUp, '\xe011' },
                { MyJoystickButtonsEnum.JDRight, '\xe012' },
                { MyJoystickButtonsEnum.JDDown, '\xe013' },
                
                // axes
                { MyJoystickAxesEnum.Xneg, '\xe016' },
                { MyJoystickAxesEnum.Xpos, '\xe015' },
                { MyJoystickAxesEnum.Ypos, '\xe014' },
                { MyJoystickAxesEnum.Yneg, '\xe017' },
                { MyJoystickAxesEnum.RotationXneg, '\xe020' },
                { MyJoystickAxesEnum.RotationXpos, '\xe019' },
                { MyJoystickAxesEnum.RotationYneg, '\xe021' },
                { MyJoystickAxesEnum.RotationYpos, '\xe018' },
                { MyJoystickAxesEnum.Zneg, '\xe007' },
                { MyJoystickAxesEnum.Zpos, '\xe008' },
            };
        }

        public static bool IsInputReadable()
        {
            return !MyAPIGateway.Gui.ChatEntryVisible && !MyAPIGateway.Gui.IsCursorVisible;
        }

        public static void AppendNiceNamePrefix(string key, object obj, StringBuilder str)
        {
            if(obj is MyKeys)
            {
                str.Append("Key: ");
            }
            else
            {
                switch(key[0])
                {
                    case 'm': str.Append("Mouse: "); break;
                    case 'g': str.Append("Gamepad: "); break;
                    case 'c': str.Append("Control: "); break;
                }
            }
        }

        public static bool GetPressed(List<object> objects, bool any = true, bool justPressed = false, bool ignoreGameControls = false)
        {
            if(objects.Count == 0)
                return false;

            foreach(var o in objects)
            {
                if(o is MyKeys)
                {
                    bool pressed = (justPressed ? MyAPIGateway.Input.IsNewKeyPressed((MyKeys)o) : MyAPIGateway.Input.IsKeyPress((MyKeys)o));

                    if(any == pressed)
                        return any;
                }
                else if(o is MyStringId)
                {
                    if(ignoreGameControls)
                        continue;

                    bool pressed = false; // workaround for any == pressed not working here for some reason

                    if(justPressed ? MyAPIGateway.Input.IsNewGameControlPressed((MyStringId)o) : MyAPIGateway.Input.IsGameControlPressed((MyStringId)o))
                        pressed = true;

                    if(any == pressed)
                        return any;
                }
                else if(o is MyMouseButtonsEnum)
                {
                    bool pressed = false; // workaround for any == pressed not working here for some reason

                    if(justPressed ? MyAPIGateway.Input.IsNewMousePressed((MyMouseButtonsEnum)o) : MyAPIGateway.Input.IsMousePressed((MyMouseButtonsEnum)o))
                        pressed = true;

                    if(any == pressed)
                        return any;
                }
                else if(o is MyJoystickAxesEnum)
                {
                    bool pressed = (justPressed ? MyAPIGateway.Input.IsJoystickAxisNewPressed((MyJoystickAxesEnum)o) : MyAPIGateway.Input.IsJoystickAxisPressed((MyJoystickAxesEnum)o));

                    if(any == pressed)
                        return any;
                }
                else if(o is MyJoystickButtonsEnum)
                {
                    bool pressed = (justPressed ? MyAPIGateway.Input.IsJoystickButtonNewPressed((MyJoystickButtonsEnum)o) : MyAPIGateway.Input.IsJoystickButtonPressed((MyJoystickButtonsEnum)o));

                    if(any == pressed)
                        return any;
                }
                else
                {
                    var text = o as string;

                    switch(text) // no need to check justPressed from here
                    {
                        case InputHandler.CONTROL_PREFIX + "view":
                            if(any == GetFullRotation().LengthSquared() > 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "analog":
                            if(any == (MyAPIGateway.Input.GetMouseXForGamePlay() != 0 || MyAPIGateway.Input.GetMouseYForGamePlay() != 0 || MyAPIGateway.Input.DeltaMouseScrollWheelValue() != 0))
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "x":
                            if(any == (MyAPIGateway.Input.GetMouseXForGamePlay() != 0))
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "y":
                            if(any == (MyAPIGateway.Input.GetMouseYForGamePlay() != 0))
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "scroll":
                            if(any == (MyAPIGateway.Input.DeltaMouseScrollWheelValue() != 0))
                                return any;
                            break;
                        case InputHandler.GAMEPAD_PREFIX + "lsanalog":
                            {
                                var x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xpos);
                                var y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Yneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Ypos);

                                if(any == (Math.Abs(x) > EPSILON || Math.Abs(y) > EPSILON))
                                    return any;

                                break;
                            }
                        case InputHandler.GAMEPAD_PREFIX + "rsanalog":
                            {
                                var x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXpos);
                                var y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYpos);

                                if(any == (Math.Abs(x) > EPSILON || Math.Abs(y) > EPSILON))
                                    return any;

                                break;
                            }
                        case InputHandler.GAMEPAD_PREFIX + "ltanalog":
                            if(any == (Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos)) > EPSILON))
                                return any;
                            break;
                        case InputHandler.GAMEPAD_PREFIX + "rtanalog":
                            if(any == (Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg)) > EPSILON))
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "scrollup":
                            if(any == MyAPIGateway.Input.DeltaMouseScrollWheelValue() > 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "scrolldown":
                            if(any == MyAPIGateway.Input.DeltaMouseScrollWheelValue() < 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "x+":
                            if(any == MyAPIGateway.Input.GetMouseXForGamePlay() > 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "x-":
                            if(any == MyAPIGateway.Input.GetMouseXForGamePlay() < 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "y+":
                            if(any == MyAPIGateway.Input.GetMouseYForGamePlay() > 0)
                                return any;
                            break;
                        case InputHandler.MOUSE_PREFIX + "y-":
                            if(any == MyAPIGateway.Input.GetMouseYForGamePlay() < 0)
                                return any;
                            break;
                    }
                }
            }

            return !any;
        }

        public static bool GetPressedOr(ControlCombination input1, ControlCombination input2, bool anyPressed = false, bool justPressed = false)
        {
            if(input1 != null && GetPressed(input1.combination, anyPressed, justPressed))
                return true;

            if(input2 != null && GetPressed(input2.combination, anyPressed, justPressed))
                return true;

            return false;
        }

        public static string GetFriendlyStringOr(ControlCombination input1, ControlCombination input2)
        {
            tmp.Clear();

            if(input1 != null)
                tmp.Append(input1.GetFriendlyString().ToUpper());

            if(input2 != null)
            {
                string secondary = input2.GetFriendlyString();

                if(secondary.Length > 0)
                {
                    if(tmp.Length > 0)
                        tmp.Append(" or ");

                    tmp.Append(secondary.ToUpper());
                }
            }

            var val = tmp.ToString();
            tmp.Clear();
            return val;
        }

        public static string GetFriendlyStringForControl(IMyControl control)
        {
            tmp.Clear();

            if(control.GetKeyboardControl() != MyKeys.None)
            {
                if(tmp.Length > 0)
                    tmp.Append(" or ");

                var def = control.GetKeyboardControl().ToString();
                tmp.Append(inputNiceNames.GetValueOrDefault(inputNames.GetValueOrDefault(control.GetKeyboardControl(), def), def));
            }

            if(control.GetMouseControl() != MyMouseButtonsEnum.None)
            {
                var def = control.GetMouseControl().ToString();
                tmp.Append(inputNiceNames.GetValueOrDefault(inputNames.GetValueOrDefault(control.GetMouseControl(), def), def));
            }
            else if(control.GetSecondKeyboardControl() != MyKeys.None)
            {
                if(tmp.Length > 0)
                    tmp.Append(" or ");

                var def = control.GetSecondKeyboardControl().ToString();
                tmp.Append(inputNiceNames.GetValueOrDefault(inputNames.GetValueOrDefault(control.GetSecondKeyboardControl(), def), def));
            }

            var val = tmp.ToString();
            tmp.Clear();
            return val;
        }

        public static Vector3 GetFullRotation()
        {
            var rot = MyAPIGateway.Input.GetRotation();
            return new Vector3(rot.X, rot.Y, MyAPIGateway.Input.GetRoll());
        }
    }
}
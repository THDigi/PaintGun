using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.Utils;

using Digi.Utils;

namespace Digi.Utils
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
                        combined.Add(InputHandler.inputNames.GetValueOrDefault(control.GetMouseControl(), InputHandler.NOT_FOUND));
                    }
                    else if(control.GetKeyboardControl() != MyKeys.None)
                    {
                        combined.Add(InputHandler.inputNames.GetValueOrDefault(control.GetKeyboardControl(), InputHandler.NOT_FOUND));
                    }
                    else if(control.GetSecondKeyboardControl() != MyKeys.None)
                    {
                        combined.Add(InputHandler.inputNames.GetValueOrDefault(control.GetSecondKeyboardControl(), InputHandler.NOT_FOUND));
                    }
                    else
                    {
                        combined.Add(InputHandler.inputNames.GetValueOrDefault(control, InputHandler.NOT_FOUND));
                    }
                }
                else if(xboxChars && (o is MyJoystickAxesEnum || o is MyJoystickButtonsEnum))
                {
                    if(!MyAPIGateway.Input.IsJoystickConnected())
                        return ""; // one of the controls is a gamepad one and a gamepad is not connected, discard.
                    
                    char c = InputHandler.xboxCodes.GetValueOrDefault(o, ' ');
                    combined.Add(c == ' ' ? InputHandler.inputNames.GetValueOrDefault(o, InputHandler.NOT_FOUND) : c.ToString());
                }
                else
                {
                    combined.Add(InputHandler.inputNames.GetValueOrDefault(o, InputHandler.NOT_FOUND));
                }
            }
            
            return String.Join(" ", combined);
        }
        
        public bool AnyPressed()
        {
            return InputHandler.GetAnyPressed(combination);
        }
        
        public bool AllPressed()
        {
            if(combination.Count == 0)
                return false;
            
            foreach(var o in combination)
            {
                if(o is MyKeys)
                {
                    if(!MyAPIGateway.Input.IsKeyPress((MyKeys)o))
                        return false;
                }
                else if(o is MyStringId)
                {
                    if(!MyAPIGateway.Input.IsGameControlPressed((MyStringId)o))
                        return false;
                }
                else if(o is MyMouseButtonsEnum)
                {
                    if(!MyAPIGateway.Input.IsMousePressed((MyMouseButtonsEnum)o))
                        return false;
                }
                else if(o is MyJoystickAxesEnum)
                {
                    if(!MyAPIGateway.Input.IsJoystickAxisPressed((MyJoystickAxesEnum)o))
                        return false;
                }
                else if(o is MyJoystickButtonsEnum)
                {
                    if(!MyAPIGateway.Input.IsJoystickButtonPressed((MyJoystickButtonsEnum)o))
                        return false;
                }
                else
                {
                    var text = o as string;
                    
                    switch(text)
                    {
                        case InputHandler.MOUSE_PREFIX+"scrollup":
                            if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() <= 0)
                                return false;
                            break;
                        case InputHandler.MOUSE_PREFIX+"scrolldown":
                            if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() >= 0)
                                return false;
                            break;
                        case InputHandler.MOUSE_PREFIX+"x+":
                            if(MyAPIGateway.Input.GetMouseXForGamePlay() <= 0)
                                return false;
                            break;
                        case InputHandler.MOUSE_PREFIX+"x-":
                            if(MyAPIGateway.Input.GetMouseXForGamePlay() >= 0)
                                return false;
                            break;
                        case InputHandler.MOUSE_PREFIX+"y+":
                            if(MyAPIGateway.Input.GetMouseYForGamePlay() <= 0)
                                return false;
                            break;
                        case InputHandler.MOUSE_PREFIX+"y-":
                            if(MyAPIGateway.Input.GetMouseYForGamePlay() >= 0)
                                return false;
                            break;
                        case InputHandler.MOUSE_PREFIX+"analog":
                            {
                                var x = MyAPIGateway.Input.GetMouseXForGamePlay();
                                var y = MyAPIGateway.Input.GetMouseYForGamePlay();
                                var z = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
                                
                                if(x == 0 && y == 0 && z == 0)
                                    return false;
                                
                                break;
                            }
                        case InputHandler.MOUSE_PREFIX+"x":
                            if(MyAPIGateway.Input.GetMouseXForGamePlay() == 0)
                                return false;
                            break;
                        case InputHandler.MOUSE_PREFIX+"y":
                            if(MyAPIGateway.Input.GetMouseYForGamePlay() == 0)
                                return false;
                            break;
                        case InputHandler.MOUSE_PREFIX+"scroll":
                            if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() == 0)
                                return false;
                            break;
                        case InputHandler.GAMEPAD_PREFIX+"lsanalog":
                            {
                                var x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xpos);
                                var y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Yneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Ypos);
                                
                                if(Math.Abs(x) < float.Epsilon && Math.Abs(y) < float.Epsilon)
                                    return false;
                                
                                break;
                            }
                        case InputHandler.GAMEPAD_PREFIX+"rsanalog":
                            {
                                var x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXpos);
                                var y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYpos);
                                
                                if(Math.Abs(x) < float.Epsilon && Math.Abs(y) < float.Epsilon)
                                    return false;
                                
                                break;
                            }
                        case InputHandler.GAMEPAD_PREFIX+"ltanalog":
                            if(Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos)) < float.Epsilon)
                                return false;
                            break;
                        case InputHandler.GAMEPAD_PREFIX+"rtanalog":
                            if(Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg)) < float.Epsilon)
                                return false;
                            break;
                    }
                }
            }
            
            return true;
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
                        Log.Info("WARNING: Input not found: "+s);
                    
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
        public static List<object> inputValueList = null;
        public static Dictionary<object, char> xboxCodes = null;
        public const string MOUSE_PREFIX = "m.";
        public const string GAMEPAD_PREFIX = "g.";
        public const string CONTROL_PREFIX = "c.";
        public const string NOT_FOUND = "?";
        
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
                
                // mouse: analog
                {MOUSE_PREFIX+"analog", MOUSE_PREFIX+"analog"},
                {MOUSE_PREFIX+"x", MOUSE_PREFIX+"x"},
                {MOUSE_PREFIX+"y", MOUSE_PREFIX+"y"},
                {MOUSE_PREFIX+"scroll", MOUSE_PREFIX+"scroll"},
                
                // mouse: analog to digital
                {MOUSE_PREFIX+"scrollup", MOUSE_PREFIX+"scrollup"},
                {MOUSE_PREFIX+"scrolldown", MOUSE_PREFIX+"scrolldown"},
                {MOUSE_PREFIX+"x+", MOUSE_PREFIX+"x+"},
                {MOUSE_PREFIX+"x-", MOUSE_PREFIX+"x-"},
                {MOUSE_PREFIX+"y+", MOUSE_PREFIX+"y+"},
                {MOUSE_PREFIX+"y-", MOUSE_PREFIX+"y-"},
                
                // gamepad: analog
                {GAMEPAD_PREFIX+"lsanalog", GAMEPAD_PREFIX+"lsanalog"},
                {GAMEPAD_PREFIX+"rsanalog", GAMEPAD_PREFIX+"rsanalog"},
                {GAMEPAD_PREFIX+"ltanalog", GAMEPAD_PREFIX+"ltanalog"},
                {GAMEPAD_PREFIX+"rtanalog", GAMEPAD_PREFIX+"rtanalog"},
                
                // gamepad: buttons
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
                {GAMEPAD_PREFIX+"ls", MyJoystickButtonsEnum.J09},
                {GAMEPAD_PREFIX+"rs", MyJoystickButtonsEnum.J10},
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
                {CONTROL_PREFIX+"help", MyControlsSpace.HELP_SCREEN},
                {CONTROL_PREFIX+"controlmenu", MyControlsSpace.CONTROL_MENU},
                {CONTROL_PREFIX+"factions", MyControlsSpace.FACTIONS_MENU},
                {CONTROL_PREFIX+"lookleft", MyControlsSpace.ROTATION_LEFT},
                {CONTROL_PREFIX+"lookright", MyControlsSpace.ROTATION_RIGHT},
                {CONTROL_PREFIX+"lookup", MyControlsSpace.ROTATION_UP},
                {CONTROL_PREFIX+"lookdown", MyControlsSpace.ROTATION_DOWN},
                {CONTROL_PREFIX+"light", MyControlsSpace.HEADLIGHTS},
                {CONTROL_PREFIX+"screenshot", MyControlsSpace.SCREENSHOT},
                {CONTROL_PREFIX+"lookaround", MyControlsSpace.LOOKAROUND},
                {CONTROL_PREFIX+"paint", MyControlsSpace.CUBE_COLOR_CHANGE},
                {CONTROL_PREFIX+"reactors", MyControlsSpace.TOGGLE_REACTORS},
                {CONTROL_PREFIX+"buildmenu", MyControlsSpace.BUILD_SCREEN},
                {CONTROL_PREFIX+"cuberotatey+", MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE},
                {CONTROL_PREFIX+"cuberotatey-", MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE},
                {CONTROL_PREFIX+"cuberotatex+", MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE},
                {CONTROL_PREFIX+"cuberotatex-", MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE},
                {CONTROL_PREFIX+"cuberotatez+", MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE},
                {CONTROL_PREFIX+"cuberotatez-", MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE},
                {CONTROL_PREFIX+"cyclesymmetry", MyControlsSpace.SYMMETRY_SWITCH},
                {CONTROL_PREFIX+"symmetry", MyControlsSpace.USE_SYMMETRY},
                {CONTROL_PREFIX+"missionsettings", MyControlsSpace.MISSION_SETTINGS},
                {CONTROL_PREFIX+"cockpitbuild", MyControlsSpace.COCKPIT_BUILD_MODE},
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
                {CONTROL_PREFIX+"togglehud", MyControlsSpace.TOGGLE_HUD},
                {CONTROL_PREFIX+"damping", MyControlsSpace.DAMPING},
                {CONTROL_PREFIX+"thrusts", MyControlsSpace.THRUSTS},
                {CONTROL_PREFIX+"cameramode", MyControlsSpace.CAMERA_MODE},
                {CONTROL_PREFIX+"broadcasting", MyControlsSpace.BROADCASTING},
                {CONTROL_PREFIX+"helmet", MyControlsSpace.HELMET},
                {CONTROL_PREFIX+"chat", MyControlsSpace.CHAT_SCREEN},
                {CONTROL_PREFIX+"console", MyControlsSpace.CONSOLE},
                {CONTROL_PREFIX+"suicide", MyControlsSpace.SUICIDE},
                {CONTROL_PREFIX+"landinggear", MyControlsSpace.LANDING_GEAR},
                {CONTROL_PREFIX+"inventory", MyControlsSpace.INVENTORY},
                {CONTROL_PREFIX+"pause", MyControlsSpace.PAUSE_GAME},
                {CONTROL_PREFIX+"specnone", MyControlsSpace.SPECTATOR_NONE},
                {CONTROL_PREFIX+"specdelta", MyControlsSpace.SPECTATOR_DELTA},
                {CONTROL_PREFIX+"specfree", MyControlsSpace.SPECTATOR_FREE},
                {CONTROL_PREFIX+"specstatic", MyControlsSpace.SPECTATOR_STATIC},
                {CONTROL_PREFIX+"stationrotation", MyControlsSpace.STATION_ROTATION},
                {CONTROL_PREFIX+"switchleft", MyControlsSpace.SWITCH_LEFT}, // previous color or cam
                {CONTROL_PREFIX+"switchright", MyControlsSpace.SWITCH_RIGHT}, // next color or cam
                {CONTROL_PREFIX+"voicechat", MyControlsSpace.VOICE_CHAT},
                {CONTROL_PREFIX+"voxelpaint", MyControlsSpace.VOXEL_PAINT},
                {CONTROL_PREFIX+"compoundmode", MyControlsSpace.SWITCH_COMPOUND},
                {CONTROL_PREFIX+"voxelhandsettings", MyControlsSpace.VOXEL_HAND_SETTINGS},
                {CONTROL_PREFIX+"buildmode", MyControlsSpace.BUILD_MODE},
                {CONTROL_PREFIX+"buildingmode", MyControlsSpace.SWITCH_BUILDING_MODE},
                {CONTROL_PREFIX+"nextblockstage", MyControlsSpace.NEXT_BLOCK_STAGE},
                {CONTROL_PREFIX+"prevblockstage", MyControlsSpace.PREV_BLOCK_STAGE},
                {CONTROL_PREFIX+"movecloser", MyControlsSpace.MOVE_CLOSER},
                {CONTROL_PREFIX+"movefurther", MyControlsSpace.MOVE_FURTHER},
                {CONTROL_PREFIX+"primarybuildaction", MyControlsSpace.PRIMARY_BUILD_ACTION},
                {CONTROL_PREFIX+"secondarybuildaction", MyControlsSpace.SECONDARY_BUILD_ACTION},
                {CONTROL_PREFIX+"copypaste", MyControlsSpace.COPY_PASTE_ACTION},
            };
            
            inputNames = new Dictionary<object, string>();
            inputValueList = new List<object>();
            
            foreach(var kv in inputs)
            {
                if(!inputNames.ContainsKey(kv.Value))
                {
                    inputNames.Add(kv.Value, kv.Key);
                    inputValueList.Add(kv.Value);
                }
            }
            
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
            // TODO detect: chat, escape menu, F10 and F11 menus, mission screens, yes/no notifications
            return MyGuiScreenGamePlay.ActiveGameplayScreen == null && MyGuiScreenTerminal.GetCurrentScreen() == MyTerminalPageEnum.None;
        }
        
        public static bool GetAnyPressed(List<object> objects, bool ignoreGameControls = false)
        {
            if(objects.Count == 0)
                return false;
            
            foreach(var o in objects)
            {
                if(o is MyKeys)
                {
                    if(MyAPIGateway.Input.IsKeyPress((MyKeys)o))
                        return true;
                }
                else if(o is MyStringId)
                {
                    if(ignoreGameControls)
                        continue;
                    
                    if(MyAPIGateway.Input.IsGameControlPressed((MyStringId)o))
                        return true;
                }
                else if(o is MyMouseButtonsEnum)
                {
                    if(MyAPIGateway.Input.IsMousePressed((MyMouseButtonsEnum)o))
                        return true;
                }
                else if(o is MyJoystickAxesEnum)
                {
                    if(MyAPIGateway.Input.IsJoystickAxisPressed((MyJoystickAxesEnum)o))
                        return true;
                }
                else if(o is MyJoystickButtonsEnum)
                {
                    if(MyAPIGateway.Input.IsJoystickButtonPressed((MyJoystickButtonsEnum)o))
                        return true;
                }
                else
                {
                    var text = o as string;
                    
                    switch(text)
                    {
                        case InputHandler.MOUSE_PREFIX+"scrollup":
                            if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() > 0)
                                return true;
                            break;
                        case InputHandler.MOUSE_PREFIX+"scrolldown":
                            if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() < 0)
                                return true;
                            break;
                        case InputHandler.MOUSE_PREFIX+"x+":
                            if(MyAPIGateway.Input.GetMouseXForGamePlay() > 0)
                                return true;
                            break;
                        case InputHandler.MOUSE_PREFIX+"x-":
                            if(MyAPIGateway.Input.GetMouseXForGamePlay() < 0)
                                return true;
                            break;
                        case InputHandler.MOUSE_PREFIX+"y+":
                            if(MyAPIGateway.Input.GetMouseYForGamePlay() > 0)
                                return true;
                            break;
                        case InputHandler.MOUSE_PREFIX+"y-":
                            if(MyAPIGateway.Input.GetMouseYForGamePlay() < 0)
                                return true;
                            break;
                        case InputHandler.MOUSE_PREFIX+"analog":
                            {
                                var x = MyAPIGateway.Input.GetMouseXForGamePlay();
                                var y = MyAPIGateway.Input.GetMouseYForGamePlay();
                                var z = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
                                
                                if(x != 0 || y != 0 || z != 0)
                                    return true;
                                
                                break;
                            }
                        case InputHandler.MOUSE_PREFIX+"x":
                            if(MyAPIGateway.Input.GetMouseXForGamePlay() != 0)
                                return true;
                            break;
                        case InputHandler.MOUSE_PREFIX+"y":
                            if(MyAPIGateway.Input.GetMouseYForGamePlay() != 0)
                                return true;
                            break;
                        case InputHandler.MOUSE_PREFIX+"scroll":
                            if(MyAPIGateway.Input.DeltaMouseScrollWheelValue() != 0)
                                return true;
                            break;
                        case InputHandler.GAMEPAD_PREFIX+"lsanalog":
                            {
                                var x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Xpos);
                                var y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Yneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Ypos);
                                
                                if(Math.Abs(x) > float.Epsilon || Math.Abs(y) > float.Epsilon)
                                    return true;
                                
                                break;
                            }
                        case InputHandler.GAMEPAD_PREFIX+"rsanalog":
                            {
                                var x = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationXpos);
                                var y = -MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYneg) + MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.RotationYpos);
                                
                                if(Math.Abs(x) > float.Epsilon || Math.Abs(y) > float.Epsilon)
                                    return true;
                                
                                break;
                            }
                        case InputHandler.GAMEPAD_PREFIX+"ltanalog":
                            if(Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zpos)) > float.Epsilon)
                                return true;
                            break;
                        case InputHandler.GAMEPAD_PREFIX+"rtanalog":
                            if(Math.Abs(MyAPIGateway.Input.GetJoystickAxisStateForGameplay(MyJoystickAxesEnum.Zneg)) > float.Epsilon)
                                return true;
                            break;
                    }
                }
            }
            
            return false;
        }
    }
}
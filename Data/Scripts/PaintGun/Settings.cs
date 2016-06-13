using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Sandbox.ModAPI;
using VRageMath;

using Digi.Utils;

namespace Digi.PaintGun
{
    public class Settings
    {
        private const string FILE = "paintgun.cfg";
        
        public bool extraSounds = true;
        public bool sprayParticles = true;
        public float spraySoundVolume = 0.8f;
        public ControlCombination pickColor1 = ControlCombination.CreateFrom("shift c.landinggear");
        public ControlCombination pickColor2 = ControlCombination.CreateFrom("g.lb g.rb");
        
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
                string[] hsv;
                int h,s,v;
                
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
                        Log.Error("Unknown "+FILE+" line: "+line+"\nMaybe is missing the '=' ?");
                        continue;
                    }
                    
                    args[0] = args[0].Trim().ToLower();
                    args[1] = args[1].Trim().ToLower();
                    
                    switch(args[0])
                    {
                        case "extrasounds":
                            if(bool.TryParse(args[1], out b))
                                extraSounds = b;
                            else
                                Log.Error("Invalid "+args[0]+" value: " + args[1]);
                            continue;
                        case "sprayparticles":
                            if(bool.TryParse(args[1], out b))
                                sprayParticles = b;
                            else
                                Log.Error("Invalid "+args[0]+" value: " + args[1]);
                            continue;
                        case "spraysoundvolume":
                            if(float.TryParse(args[1], out f))
                                spraySoundVolume = MathHelper.Clamp(f, 0, 1);
                            else
                                Log.Error("Invalid "+args[0]+" value: " + args[1]);
                            continue;
                        case "pickcolorinput1":
                        case "pickcolorinput2":
                            if(args[1].Length == 0)
                                continue;
                            var obj = ControlCombination.CreateFrom(args[1]);
                            if(obj != null)
                            {
                                if(args[0] == "pickcolorinput1")
                                    pickColor1 = obj;
                                else
                                    pickColor2 = obj;
                            }
                            else
                                Log.Error("Invalid "+args[0]+" value: " + args[1]);
                            continue;
                        case "lastpaintcolor":
                            hsv = args[1].Split(',');
                            if(hsv.Length >= 3 && int.TryParse(hsv[0].Trim(), out h) && int.TryParse(hsv[1].Trim(), out s) && int.TryParse(hsv[2].Trim(), out v))
                                PaintGunMod.instance.SetBuildColor(new Vector3(h / 360.0f, s / 100.0f, v / 100.0f), false);
                            else
                                Log.Error("Invalid "+args[0]+" value: " + args[1]);
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
                str.AppendLine("// Paint Gun mod config; this file gets automatically overwritten after being loaded so don't leave custom comments.");
                str.AppendLine("// You can reload this while the game is running by typing in chat: /pg reload");
                str.AppendLine("// Lines starting with // are comments. All values are case insensitive unless otherwise specified.");
                str.AppendLine();
            }
            
            str.Append("ExtraSounds=").Append(extraSounds).AppendLine(comments ? " // toggle sounds: when aiming at a different color in color pick mode and when finishing painting in survival. Default: true" : "");
            str.Append("SprayParticles=").Append(sprayParticles).AppendLine(comments ? " // toggles the spray particles. Default: true" : "");
            str.Append("SpraySoundVolume=").Append(spraySoundVolume).AppendLine(comments ? " // paint gun spraying sound volume. Default: 0.8" : "");
            
            if(comments)
            {
                str.AppendLine();
                str.AppendLine("// Key/mouse/gamepad combination to trigger '/pg pick' command.");
                str.AppendLine("// Separate multiple keys/buttons/controls with spaces. For gamepad add "+InputHandler.GAMEPAD_PREFIX+" prefix, for mouse add "+InputHandler.MOUSE_PREFIX+" prefix and for game controls add "+InputHandler.CONTROL_PREFIX+" prefix.");
                str.AppendLine("// All keys, mouse buttons, gamepad buttons/axes and control names are at the bottom of this file.");
            }
            str.Append("PickColorInput1=").Append(pickColor1 == null ? "" : pickColor1.GetStringCombination()).AppendLine(comments ? " // Default: shift c.landinggear" : "");
            str.Append("PickColorInput2=").Append(pickColor2 == null ? "" : pickColor2.GetStringCombination()).AppendLine(comments ? " // Default: g.lb g.rb" : "");
            
            if(comments)
            {
                str.AppendLine();
                str.AppendLine("// List of inputs, generated from game data.");
                
                str.Append("// Key names: ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.MOUSE_PREFIX, StringComparison.Ordinal)
                       || kv.Key.StartsWith(InputHandler.GAMEPAD_PREFIX, StringComparison.Ordinal)
                       || kv.Key.StartsWith(InputHandler.CONTROL_PREFIX, StringComparison.Ordinal))
                        continue;
                    
                    str.Append(kv.Key).Append(", ");
                }
                str.AppendLine();
                
                str.Append("// Mouse button names: ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.MOUSE_PREFIX, StringComparison.Ordinal))
                    {
                        str.Append(kv.Key).Append(", ");
                    }
                }
                str.AppendLine();
                
                str.Append("// Gamepad button/axes names: ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.GAMEPAD_PREFIX, StringComparison.Ordinal))
                    {
                        str.Append(kv.Key).Append(", ");
                    }
                }
                str.AppendLine();
                
                str.Append("// Control names: ");
                foreach(var kv in InputHandler.inputs)
                {
                    if(kv.Key.StartsWith(InputHandler.CONTROL_PREFIX, StringComparison.Ordinal))
                    {
                        str.Append(kv.Key).Append(", ");
                    }
                }
                str.AppendLine();
            }
            
            if(comments)
            {
                str.AppendLine().AppendLine().AppendLine();
                str.AppendLine("// DO NOT edit anything below here");
            }
            
            var paint = PaintGunMod.instance.GetBuildColor().ToHSVI();
            str.Append("LastPaintColor=").Append(paint.X).Append(",").Append(paint.Y).Append(",").Append(paint.Z).AppendLine();
            
            return str.ToString();
        }
        
        public void Close()
        {
        }
    }
}
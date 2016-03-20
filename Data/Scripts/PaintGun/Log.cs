using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Digi.Utils
{
    public static class Log
    {
        public const string MOD_NAME = "PaintGun";
        public const int WORKSHOP_ID = 500818376;
        public const string LOG_FILE = "info.log";
        
        private static System.IO.TextWriter writer = null;
        private static IMyHudNotification notify = null;
        private static StringBuilder cache = new StringBuilder(64);
        private static List<string> preInitMessages = new List<string>(0);
        private static int indent = 0;
        
        public static void IncreaseIndent()
        {
            indent++;
        }
        
        public static void DecreaseIndent()
        {
            if(indent > 0)
                indent--;
        }
        
        public static void ResetIndent()
        {
            indent = 0;
        }
        
        public static void Error(Exception e)
        {
            Error(e.ToString());
        }
        
        public static void Error(string msg)
        {
            Info("ERROR: " + msg);
            
            try
            {
                MyLog.Default.WriteLineAndConsole(MOD_NAME + " error: " + msg);
                
                if(MyAPIGateway.Session != null)
                {
                    string text = MOD_NAME + " error - open %AppData%/SpaceEngineers/Storage/" + WORKSHOP_ID + "_" + MOD_NAME + "/" + LOG_FILE + " for details";
                    
                    if(notify == null)
                    {
                        notify = MyAPIGateway.Utilities.CreateNotification(text, 10000, MyFontEnum.Red);
                    }
                    else
                    {
                        notify.Text = text;
                        notify.ResetAliveTime();
                    }
                    
                    notify.Show();
                }
            }
            catch(Exception e)
            {
                Info("ERROR: Could not send notification to local client: " + e.ToString());
            }
        }
        
        public static void Info(string msg)
        {
            Write(msg);
        }
        
        private static void Write(string msg)
        {
            try
            {
                cache.Clear();
                cache.Append(DateTime.Now.ToString("[HH:mm:ss] "));
                
                if(writer == null)
                    cache.Append("(PRE-INIT) ");
                
                for(int i = 0; i < indent; i++)
                {
                    cache.Append("\t");
                }
                
                cache.Append(msg);
                
                if(writer == null)
                {
                    preInitMessages.Add(cache.ToString());
                }
                else
                {
                    writer.WriteLine(cache);
                    writer.Flush();
                }
                
                cache.Clear();
            }
            catch(Exception e)
            {
                MyLog.Default.WriteLineAndConsole(MOD_NAME + " had an error while logging message='"+msg+"'\nLogger error: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        public static void Init()
        {
            if(MyAPIGateway.Utilities == null)
            {
                MyLog.Default.WriteLineAndConsole(MOD_NAME + " Log.Init() called before API was ready!");
                return;
            }
            
            if(writer != null)
            {
                Close();
            }
            
            writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(LOG_FILE, typeof(Log));
            
            if(preInitMessages.Count > 0)
            {
                foreach(var msg in preInitMessages)
                {
                    Log.Error(msg);
                }
                
                preInitMessages = new List<string>(0);
            }
        }
        
        public static void Close()
        {
            if(writer != null)
            {
                writer.Flush();
                writer.Close();
                writer = null;
            }
            
            indent = 0;
            cache.Clear();
        }
    }
}
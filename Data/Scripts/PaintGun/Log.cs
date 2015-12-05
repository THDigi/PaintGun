using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI;
using Sandbox.Common;
using VRage.Utils;

namespace Digi.Utils
{
    class Log
    {
        public const string MOD_NAME = "PaintGun";
        public const string LOG_FILE = "info.log";

        private static System.IO.TextWriter writer = null;
        private static IMyHudNotification notify = null;
        private static int indent = 0;
        private static StringBuilder cache = new StringBuilder();

        public static void IncreaseIndent()
        {
            indent++;
        }

        public static void DecreaseIndent()
        {
            if (indent > 0)
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
                
                string text = MOD_NAME + " error - open %AppData%/SpaceEngineers/Storage/" + MyAPIGateway.Session.WorkshopId + "_" + MOD_NAME + "/" + LOG_FILE + " for details";
                
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
            catch (Exception e)
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
                if(writer == null)
                {
                    if (MyAPIGateway.Utilities == null)
                        throw new Exception("API not initialied but got a log message: " + msg);

                    writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(LOG_FILE, typeof(Log));
                }

                cache.Clear();
                cache.Append(DateTime.Now.ToString("[HH:mm:ss] "));

                for(int i = 0; i < indent; i++)
                {
                    cache.Append("\t");
                }

                cache.Append(msg);

                writer.WriteLine(cache);
                writer.Flush();

                cache.Clear();
            }
            catch(Exception e)
            {
                MyLog.Default.WriteLineAndConsole(MOD_NAME + " had an error while logging message='"+msg+"'\nLogger error: " + e.Message + "\n" + e.StackTrace);
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
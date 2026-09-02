using System;
using System.IO;

namespace RebuildBotPlugin.Services
{
    /// <summary>
    /// Centralized, thread-safe logger for RebuildBotPlugin.
    /// By default, routes all logs to the active character profile's bot.log for consumption by the Fleet Orchestrator.
    /// When launched with the -bepinexlog CLI flag, routes logs to the BepInEx terminal console instead.
    /// </summary>
    public static class BotLog
    {
        private static readonly object FileLock = new object();

        public static void Info(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            if (ProfileManager.BepInExLoggingCliFlag)
            {
                Plugin.Instance?.Log.LogInfo(message);
            }
            else
            {
                WriteToFile(message);
            }
        }

        public static void Warn(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            string line = message.StartsWith("[") ? message : $"[WARN] {message}";
            if (ProfileManager.BepInExLoggingCliFlag)
            {
                Plugin.Instance?.Log.LogWarning(line);
            }
            else
            {
                WriteToFile(line);
            }
        }

        public static void Error(string message, Exception ex = null)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            string fullMessage = ex != null ? $"{message} (Exception: {ex.Message})" : message;
            string line = fullMessage.StartsWith("[") ? fullMessage : $"[ERROR] {fullMessage}";

            if (ProfileManager.BepInExLoggingCliFlag)
            {
                Plugin.Instance?.Log.LogError(line);
            }
            else
            {
                WriteToFile(line);
            }
        }

        public static void Debug(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (!BotConfigManager.Current.VerboseLogging) return;

            string line = message.StartsWith("[DEBUG]") ? message : $"[DEBUG] {message}";
            if (ProfileManager.BepInExLoggingCliFlag)
            {
                Plugin.Instance?.Log.LogDebug(line);
            }
            else
            {
                WriteToFile(line);
            }
        }

        private static void WriteToFile(string message)
        {
            try
            {
                string logPath = ProfileManager.GetLogPath();
                string dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logLine = $"[{timestamp}] {message}{Environment.NewLine}";

                lock (FileLock)
                {
                    File.AppendAllText(logPath, logLine);
                }
            }
            catch { }
        }
    }
}

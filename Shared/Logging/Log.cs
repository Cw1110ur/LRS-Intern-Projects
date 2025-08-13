using System;
using System.IO;
using LoadGenTool.Shared.PathFinder;

namespace LoadGenTool.Shared.Logging
{
    public static class Log
    {
        private static readonly string logFilePath = GetLogFilePath();

        public static void Write(string message)
        {
            string logEntry = $"{DateTime.Now}: {message}";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
                using (FileStream fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter writer = new StreamWriter(fileStream))
                {
                    writer.WriteLine(logEntry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOGGING ERROR] {ex.Message}");
            }
        }

        private static string GetLogFilePath()
        {
            string Root = PathHelper.FindRootFolder();

            // You can customize this relative path inside BIGTEST however you want
            return Path.Combine(Root, "CLT", "Logs", "application.log");
        }
    }
}
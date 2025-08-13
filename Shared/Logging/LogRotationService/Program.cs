using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;

namespace LogRotationService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (!IsRunningAsService())
            {
                // Install the service if not already installed
                InstallService();
                return;
            }

            CreateHostBuilder(args).Build().Run();
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                });

        private static bool IsRunningAsService()
        {
            // Service will not have a console window
            return !Environment.UserInteractive;
        }

        private static void InstallService()
        {
            string serviceName = "LogRotationService";
            string exePath = Process.GetCurrentProcess().MainModule.FileName;

            try
            {
                Console.WriteLine("Installing Windows Service...");

                RunAsAdmin($"sc create {serviceName} binPath= \"{exePath}\" start= auto");
                RunAsAdmin($"sc start {serviceName}");

                Console.WriteLine("✅ Service installed and started.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Install failed: {ex.Message}");
            }
        }

        private static void RunAsAdmin(string command)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                UseShellExecute = true,
                Verb = "runas", // UAC prompt
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(psi);
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new Exception($"Command failed: {command}");
        }
    }
}
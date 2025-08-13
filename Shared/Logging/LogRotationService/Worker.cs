using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LogRotationService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        private const long maxLogSizeBytes = 512 * 1024 * 1024;

        private static readonly string baseDir = AppContext.BaseDirectory;
        private static readonly string logFilePath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\RUN EXES HERE\Load Tester Simulator .v3\References\LogIn\PD\CLT\Logs\application.log"));
        private static readonly string archiveDir = Path.GetFullPath(Path.Combine(baseDir, @"..\Archive"));

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
            Directory.CreateDirectory(archiveDir);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("LogRotationService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(logFilePath))
                    {
                        var fi = new FileInfo(logFilePath);
                        if (fi.Length >= maxLogSizeBytes)
                        {
                            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                            string archivePath = Path.Combine(archiveDir, $"log_{timestamp}.log");

                            File.Move(logFilePath, archivePath);
                            File.Create(logFilePath).Close();

                            _logger.LogInformation($"Log rotated to {archivePath}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Log file not found: {logFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in log rotation loop.");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }

            _logger.LogInformation("LogRotationService stopped.");
        }
    }
}
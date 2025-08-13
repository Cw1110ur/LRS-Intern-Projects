using LoadGenTool.Shared.Logging;
using LoadGenTool.Shared.PathFinder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace LRS.TestTools.LoadSim
{
    public class LoadSim : ConsoleApplication
    {
        static int Main(string[] args)
        {
            return new LoadSim().ExecuteAsync(args).GetAwaiter().GetResult();
        }

        public override async Task<int> RunAsync(Dictionary<string, string> args)
        {
            bool metricsCalled = false;

            if (!args.TryGetValue("soapToken", out var soapToken) ||
                !args.TryGetValue("totalJobs", out var totalJobsStr) ||
                !args.TryGetValue("stepValue", out var stepValueStr) ||
                !args.TryGetValue("hostname", out var hostname) ||
                !args.TryGetValue("vpsid", out var vpsid) ||
                !args.TryGetValue("username", out var username) ||
                !args.TryGetValue("password", out var password) ||
                !args.TryGetValue("sessionId", out var sessionId) ||
                !args.TryGetValue("queues", out var queuesString))
            {
                LogMessage("Missing required arguments.");
                return 1;
            }

            if (!args.TryGetValue("pipeName", out var pipeName))
            {
                LogMessage("Missing required argument: pipeName.");
                return 1;
            }

            if (!int.TryParse(totalJobsStr, out var totalJobs) ||
                !int.TryParse(stepValueStr, out var stepValue))
            {
                LogMessage("Invalid numeric arguments.");
                return 1;
            }

            LogMessage($"Received args: soapToken={soapToken}, totalJobs={totalJobs}, stepValue={stepValue}, " +
                       $"hostname={hostname}, vpsid={vpsid}, sessionId={sessionId}, queues={queuesString}");

            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            try
            {
                await pipe.ConnectAsync(5000); // CHANGE: Increased timeout to 5 seconds
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to connect to pipe {pipeName}: {ex.Message}");
                return 1;
            }

            var pipeReader = new StreamReader(pipe);
            var pipeWriter = new StreamWriter(pipe) { AutoFlush = true };

            CancellationTokenSource cts = new();
            CancellationToken token = cts.Token;

            // Start a task to listen for cancel messages
            _ = Task.Run(async () =>
            {
                try
                {
                    while (pipe.IsConnected && !pipeReader.EndOfStream)
                    {
                        string line = await pipeReader.ReadLineAsync();
                        if (line?.Trim().ToLower() == "cancel")
                        {
                            LogMessage("Cancellation requested from main process.");
                            cts.Cancel();
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Pipe listen error: {ex.Message}");
                }
                finally
                {
                    pipeReader?.Dispose();
                    pipeWriter?.Dispose();
                }
            }, token);

            string MetricsMonitorScriptPath = Path.Combine(
                FindLoadGenFolder(),
                "RUN EXES HERE",
                "Load Tester Simulator .v3",
                "References",
                "LogIn",
                "PD",
                "LRSMetrics.exe");

            string RandomSelectionScriptPath = Path.Combine(
                FindLoadGenFolder(),
                "RUN EXES HERE",
                "Load Tester Simulator .v3",
                "References",
                "LogIn",
                "PD",
                "updatedRandom.exe");

            List<string> queues = queuesString.Split(' ').ToList();
            string gatewayUrl = $"https://{hostname}/lrs.gateway";

            string PPMSimPath = Path.Combine(
                FindLoadGenFolder(),
                "RUN EXES HERE",
                "Load Tester Simulator .v3",
                "References",
                "LogIn",
                "PD",
                "CLT",
                "theOneDriver",
                "PPMJobSimulator",
                "lrsGatewayTester.exe");

            LogMessage($"STEP: {stepValue}");
            LogMessage($"TOTAL JOBS: {totalJobs}");

            NamedPipeClientStream client = null;
            try
            {
                client = new NamedPipeClientStream(".", "ProgressPipe", PipeDirection.Out);
                await client.ConnectAsync(5000); // CHANGE: Increased timeout to 5 seconds
                using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                writer.WriteLine($"set_progress_config:{totalJobs},{stepValue}");

                int totalSubmitted = 0;
                metricsCalled = false;

                while (totalSubmitted < totalJobs && !token.IsCancellationRequested)
                {
                    int remainingJobs = totalJobs - totalSubmitted;
                    int batchSize = Math.Min(stepValue, remainingJobs);

                    LogMessage($"JOBS submitted so far: {totalSubmitted}");
                    LogMessage($"JOBS remaining: {remainingJobs}");
                    LogMessage($"Submitting batch of {batchSize} job(s)");

                    try
                    {
                        List<string> selectedItems = RunPowerShellScript(RandomSelectionScriptPath, $"-queues {string.Join(',', queues)}");

                        if (selectedItems.Count < 2)
                        {
                            LogMessage("Random selection script did not return expected output.");
                            break;
                        }

                        string queue = selectedItems[0];
                        string job = selectedItems[1];

                        ProcessStartInfo startInfo = new()
                        {
                            FileName = PPMSimPath,
                            Arguments = $"-g \"{gatewayUrl}\" -u \"{username}\" -p {password} -q {queue} -j \"{job}\" -t {batchSize}",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true, // CHANGE: Redirect errors for better logging
                            CreateNoWindow = true // CHANGE: Ensure no window is created
                        };

                        using Process process = new() { StartInfo = startInfo };
                        process.ErrorDataReceived += (sender, args) =>
                        {
                            if (args.Data != null)
                                LogMessage("ERROR0: " + args.Data);
                        };
                        process.OutputDataReceived += (sender, args) =>
                        {
                            if (args.Data != null)
                                LogMessage(args.Data);
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        await Task.Run(() => process.WaitForExit(), token);
                        process.Close();

                        totalSubmitted += batchSize;
                        LogMessage($"Batch of {batchSize} job(s) submitted. Total submitted: {totalSubmitted}");

                        if (!metricsCalled && totalSubmitted >= totalJobs * 0.75)
                        {
                            metricsCalled = true;
                            LogMessage("Calling Metrics Script at 75% completion.");
                            RunPowerShellScript(MetricsMonitorScriptPath, $"-sessID {sessionId} -jobsAmount {totalJobs}");
                        }

                        writer.WriteLine("increment");
                        await Task.Delay(750, token);
                    }
                    catch (OperationCanceledException)
                    {
                        LogMessage("Job submission cancelled.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error submitting batch: {ex.Message}");
                    }

                    LogMessage($"Batch {Math.Ceiling((double)totalSubmitted / stepValue)} complete.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Progress pipe error: {ex.Message}");
                return 1;
            }
            finally
            {
                client?.Dispose();
                pipe?.Dispose();
                cts?.Dispose();
            }

            return 0;
        }

        private static void LogMessage(string message)
        {
            string logFilePath = Path.Combine(
                FindLoadGenFolder(),
                "RUN EXES HERE",
                "Load Tester Simulator .v3",
                "References",
                "LogIn",
                "PD",
                "CLT",
                "Logs",
                "application.log");

            try
            {
                using (FileStream fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter writer = new StreamWriter(fileStream))
                {
                    writer.WriteLine($"{DateTime.Now}: {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception2: {ex.Message}");
            }
        }

        public static string FindLoadGenFolder()
        {
            string targetFolder = "LoadGenTool";
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current != null && !current.Name.Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                current = current.Parent;
            }

            if (current == null || !current.Name.Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                throw new DirectoryNotFoundException(
                    $"Could not locate the '{targetFolder}' folder in the directory hierarchy starting from '{AppContext.BaseDirectory}'.");
            }

            return current.FullName;
        }

        private static List<string> RunPowerShellScript(string scriptPath, string arguments)
        {
            LogMessage($"Arguments: {arguments}");
            LogMessage($"Path: {scriptPath}");

            var psi = new ProcessStartInfo
            {
                FileName = scriptPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process
            {
                StartInfo = psi
            };

            List<string> wantedOutputs = new();

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    string output = args.Data;
                    if (output.StartsWith("Selected Queue: "))
                    {
                        wantedOutputs.Add(output[16..]);
                    }
                    else if (output.StartsWith("Selected Job: "))
                    {
                        wantedOutputs.Add(output[14..]);
                    }
                    LogMessage(args.Data);
                }
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                    LogMessage("ERROR1: " + args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            process.Close();

            return wantedOutputs;
        }
    }
}
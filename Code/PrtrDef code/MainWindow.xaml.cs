using LoadGenTool.Shared.Logging;
using LoadGenTool.Shared.PathFinder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Printing;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;

namespace VPSXPrinterAdder
{
    public class PrinterInfo
    {
        public string Name { get; set; }
        public string Contact { get; set; }
    }

    public partial class MainWindow : Window
    {
        private readonly string queueFilePath = Path.Combine(FindLoadGenFolder(), "Queues.txt");
        private readonly string prtDefFilePath = Path.Combine(FindLoadGenFolder(), "PrtDef.txt");
        private CancellationTokenSource _cts;

        private readonly string HOSTNAME;
        private readonly string SOAPTOKEN;
        private readonly string SESSIONID;
        private readonly string VPSID;

        private List<string> queueLines;
        private List<string> prtDefLines;

        private bool _isLoaded;
        public MainWindow(string hostname, string soapToken, string sessionId, string vpsid)
        {
            try
            {
                InitializeComponent();
                ColorSlider.Value = 210;
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Initialization error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            // Validate and log constructor parameters
            this.HOSTNAME = hostname?.Trim();
            this.SOAPTOKEN = soapToken?.Trim();
            this.SESSIONID = sessionId?.Trim();
            this.VPSID = vpsid?.Trim();

            Dispatcher.Invoke(() =>
            {
                OutputBox.AppendText($"Constructor Parameters:\n");
                OutputBox.AppendText($"HOSTNAME: {(string.IsNullOrEmpty(HOSTNAME) ? "MISSING" : HOSTNAME)}\n");
                OutputBox.AppendText($"SOAPTOKEN: {(string.IsNullOrEmpty(SOAPTOKEN) ? "MISSING" : "****")}\n");
                OutputBox.AppendText($"SESSIONID: {(string.IsNullOrEmpty(SESSIONID) ? "MISSING" : SESSIONID)}\n");
                OutputBox.AppendText($"VPSID: {(string.IsNullOrEmpty(VPSID) ? "MISSING" : VPSID)}\n");
            });

            if (string.IsNullOrEmpty(HOSTNAME) || string.IsNullOrEmpty(SOAPTOKEN) || string.IsNullOrEmpty(SESSIONID) || string.IsNullOrEmpty(VPSID))
            {
                Dispatcher.Invoke(() => OutputBox.AppendText("Error: One or more constructor parameters are missing. Please check login interface.\n"));
                return;
            }

            _cts = new CancellationTokenSource();
            CancelButton.IsEnabled = false;
            RunButton.IsEnabled = false;

            RefreshPrtDefWithDelay(); // Trigger initial refresh
        }

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancellationToken cancellationToken = _cts.Token;

            OutputBox.Clear();

            if (!int.TryParse(PrinterCountBox.Text, out int count) || count <= 0)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText("Invalid printer count. Must be a positive number.\n");
                    CancelButton.IsEnabled = false;
                });
                return;
            }

            string hostname = this.HOSTNAME;
            string soapToken = this.SOAPTOKEN;
            string sessionId = this.SESSIONID;
            string vpsid = this.VPSID;
            string baseName = BasePrinterBox.Text.Trim();
            string holdQ = HoldQNameBox.Text.Trim();
            string HOLDQ = $"1{holdQ}";
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); // Single timestamp for the group

            if (string.IsNullOrEmpty(hostname) || string.IsNullOrEmpty(soapToken) || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(vpsid))
            {
                Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText("Error loading necessary data from login interface. Please close program and try again.\n");
                    CancelButton.IsEnabled = false;
                });
                return;
            }

            if (string.IsNullOrEmpty(baseName) || string.IsNullOrEmpty(holdQ) || string.IsNullOrEmpty(vpsid))
            {
                Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText("Error: One or more fields are empty. Please try again.\n");
                    CancelButton.IsEnabled = false;
                });
                return;
            }

            if (baseName.Contains(" ") || holdQ.Contains(" "))
            {
                Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText("Error: Printer name or Queue name must not contain spaces.\n");
                    CancelButton.IsEnabled = false;
                });
                return;
            }

            char[] disallowedChars = { ' ', '&', '@', '#', '$', '%', '^', '*', '(', ')', '=', '+', '[', ']', '{', '}', ';', ':', '\'', '"', '\\', '|', '<', '>', ',', '?', '/' };

            if (baseName.IndexOfAny(disallowedChars) >= 0 || holdQ.IndexOfAny(disallowedChars) >= 0)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText("Error: Printer name or Queue name must not contain spaces or special characters.\n");
                    CancelButton.IsEnabled = false;
                });
                return;
            }

            OutputBox.AppendText("Starting...\n");
            CancelButton.IsEnabled = true;

            try
            {
                this.queueLines = new List<string>();
                this.prtDefLines = new List<string>();

                if (File.Exists(prtDefFilePath) && new FileInfo(prtDefFilePath).Length == 0)
                {
                    using (var stream = new FileStream(queueFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write("");
                    }
                    Dispatcher.Invoke(() => OutputBox.AppendText("PrtDef.txt was empty; cleared Queues.txt.\n"));
                }

                using (var stream = new FileStream(queueFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write("");
                }

                string sessId = sessionId;
                Dispatcher.Invoke(() => OutputBox.AppendText($"Session ID: {sessId}\n\n"));

                Dispatcher.Invoke(() => OutputBox.AppendText($"Adding Hold Queue {HOLDQ}...\n"));
                await AddPrinter(sessId, hostname, vpsid, HOLDQ, "NONE", null, 515, true, null, true, true, true, timestamp);
                prtDefLines.Add($"{HOLDQ} | {timestamp} | Hold Queue");

                for (int i = 1; i <= count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string prtName = $"1_{baseName}_{i}";
                    Dispatcher.Invoke(() => OutputBox.AppendText($"Adding {prtName}...\n"));

                    await AddPrinter(sessId, hostname, vpsid, prtName, "TCPIP/LRSQ", "localhost", 5500, false, HOLDQ, false, false, true, timestamp);

                    queueLines.Add(prtName);
                    prtDefLines.Add($"{prtName} | {timestamp} | Base Printer");
                }

                await WriteFileWithRetry(queueFilePath, string.Join(Environment.NewLine, queueLines) + Environment.NewLine, false, 3, 1000, cancellationToken);
                await WriteFileWithRetry(prtDefFilePath, string.Join(Environment.NewLine, prtDefLines) + Environment.NewLine, true, 3, 1000, cancellationToken);

                Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText("All printers added.\n");
                    AddButton.IsEnabled = true;
                    CancelButton.IsEnabled = false;
                });

                await RefreshPrtDefWithDelay();
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText("Operation cancelled by user.\n");
                    AddButton.IsEnabled = true;
                    CancelButton.IsEnabled = false;
                });

                await WriteFileWithRetry(queueFilePath, string.Join(Environment.NewLine, queueLines) + Environment.NewLine, false, 3, 1000, cancellationToken);
                await WriteFileWithRetry(prtDefFilePath, string.Join(Environment.NewLine, prtDefLines) + Environment.NewLine, true, 3, 1000, cancellationToken);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText($"Error: {ex.Message}\nStack Trace: {ex.StackTrace}\n");
                    AddButton.IsEnabled = true;
                    CancelButton.IsEnabled = false;
                });
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Dispatcher.Invoke(() =>
            {
                OutputBox.AppendText("Cancellation requested...\n");
                CancelButton.IsEnabled = false;
            });
        }

        private void UserImageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.ContextMenu != null)
                {
                    button.ContextMenu.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in UserImageButton_Click: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ThemesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ThemePopup.IsOpen = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in ThemesMenuItem_Click: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseThemePopup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ThemePopup.IsOpen = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in CloseThemePopup_Click: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (!_isLoaded) return;
                if (ColorPreview != null)
                {
                    double hue = e.NewValue;
                    var baseColor = HslToRgb(hue, 0.6, 0.4);

                    var themeBrush = new SolidColorBrush(baseColor);
                    Resources["ThemeColorBrush"] = themeBrush;

                    Color lightColor = Color.FromArgb(
                        20,
                        baseColor.R,
                        baseColor.G,
                        baseColor.B);
                    var lightBrush = new SolidColorBrush(lightColor);
                    Resources["LightThemeColorBrush"] = lightBrush;

                    ColorPreview.Fill = themeBrush;
                }
                else
                {
                    MessageBox.Show("ColorPreview is not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in ColorSlider_ValueChanged: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h / 360);
                g = HueToRgb(p, q, h / 360 - 1.0 / 3.0);
                b = HueToRgb(p, q, h / 360 + 1.0 / 3.0);
            }
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        private void SetAppColor(Color baseColor)
        {
            var themeBrush = new SolidColorBrush(baseColor);
            Application.Current.Resources["ThemeColorBrush"] = themeBrush;

            var lightColor = Color.FromArgb(85, baseColor.R, baseColor.G, baseColor.B);
            var lightBrush = new SolidColorBrush(lightColor);
            Application.Current.Resources["LightThemeColorBrush"] = lightBrush;
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("LRS Printer Adder\nVersion 1.0");
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to log out?", "Logout", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    string loadGenRoot = FindLoadGenFolder();
                    string CLTexePath = Path.Combine(
                        loadGenRoot,
                        "RUN EXES HERE",
                        "Load Tester Simulator .v3",
                        "References",
                        "LogIn",
                        "LTLogIn.exe"
                    );

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = CLTexePath,
                        UseShellExecute = true
                    });

                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to launch login screen:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("PrtrDef");
                foreach (var process in processes)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch (Exception ex)
            {
                OutputBox.AppendText($"Error terminating PrtrDef.exe: {ex.Message}\n");
            }
            Application.Current.Shutdown();
        }

        private async Task WriteFileWithRetry(string filePath, string content, bool append = false, int maxRetries = 3, int delayMs = 1000, CancellationToken token = default)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(filePath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream))
                    {
                        await writer.WriteAsync(content);
                    }
                    return;
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    Dispatcher.Invoke(() => OutputBox.AppendText($"File access retry {attempt}/{maxRetries} for {filePath}: {ex.Message}\n"));
                    await Task.Delay(delayMs, token);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to write to {filePath} after {maxRetries} attempts: {ex.Message}", ex);
                }
            }
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            string hostname = this.HOSTNAME;
            string soapToken = this.SOAPTOKEN;
            string sessId = this.SESSIONID;
            string vpsid = this.VPSID;

            try
            {
                OutputBox.AppendText("Running ClientLoadTester...");
                await RunClientLoadTester(hostname, soapToken, vpsid, sessId);

                RunButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => OutputBox.AppendText($"Error launching ClientLoadTester: {ex.Message}\n"));
            }
        }

        private async void ClearPrtDef_Click(object sender, RoutedEventArgs e)
        {
            string hostname = this.HOSTNAME;
            string sessionId = this.SESSIONID;
            string vpsid = this.VPSID;

            ButtonsEnabled(false);
            RunButton.IsEnabled = false;

            if (string.IsNullOrEmpty(hostname) || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(vpsid))
            {
                OutputBox.AppendText("Error loading necessary data from login interface. Unable to run this delete command.\n");
                ButtonsEnabled(true);
                return;
            }

            try
            {
                string sessId = sessionId;
                OutputBox.AppendText($"Session ID: {sessId}\n\n");

                if (File.Exists(prtDefFilePath))
                {
                    string[] lines = File.ReadAllLines(prtDefFilePath);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        string[] parts = line.Split('|');
                        if (parts.Length < 1) continue;

                        string printerName = parts[0].Trim();

                        OutputBox.AppendText($"Deleting {printerName}...\n");
                        await DeletePrinter(sessId, hostname, vpsid, printerName);
                    }
                }

                OutputBox.AppendText("All printers in PrtDef.txt deleted.\n");

                File.WriteAllText(prtDefFilePath, "");
                File.WriteAllText(queueFilePath, "");
                PrtDefListBox.Items.Clear();
                HoldQueueListBox.Items.Clear();

                await RefreshPrtDefWithDelay();

                ButtonsEnabled(true);
            }
            catch (Exception ex)
            {
                OutputBox.AppendText($"Cleanup error: {ex.Message}\n");
                ButtonsEnabled(true);
            }
            ButtonsEnabled(true);
        }

        private async void DeleteSinglePrinter_Click(object sender, RoutedEventArgs e)
        {
            ButtonsEnabled(false);

            if (PrtDefListBox.SelectedItem is not string selectedLine || string.IsNullOrWhiteSpace(selectedLine))
            {
                MessageBox.Show("Please select a printer from the list to delete.");
                ButtonsEnabled(true);
                return;
            }

            string printerName = selectedLine.Split('|')[0].Trim();

            string hostname = this.HOSTNAME;
            string token = this.SOAPTOKEN;
            string vpsid = this.VPSID;
            string sessId = this.SESSIONID;
            string holdQ = HoldQNameBox.Text.Trim();
            string HOLDQ = $"1{holdQ}";

            await SetPrinter(sessId, hostname, vpsid, printerName, "off", HOLDQ);

            if (string.IsNullOrEmpty(hostname) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sessId) || string.IsNullOrEmpty(vpsid))
            {
                MessageBox.Show("Error loading necessary data from login interface. Unable to run this delete command.\n");
                ButtonsEnabled(true);
                return;
            }

            try
            {
                OutputBox.AppendText($"Starting deletion for: {printerName}\n");
                OutputBox.AppendText($"Session ID: {sessId}\n");
                OutputBox.AppendText($"Deleting '{printerName}' from VPSX...\n");
                await DeletePrinter(sessId, hostname, vpsid, printerName);
                OutputBox.AppendText($"Deleted '{printerName}' from VPSX.\n");

                if (File.Exists(prtDefFilePath))
                {
                    var lines = File.ReadAllLines(prtDefFilePath)
                        .Where(line => !line.StartsWith(printerName))
                        .ToList();
                    File.WriteAllLines(prtDefFilePath, lines);
                }

                if (File.Exists(queueFilePath))
                {
                    var queueLines = File.ReadAllLines(queueFilePath)
                        .Where(line => !line.Trim().Equals(printerName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    File.WriteAllLines(queueFilePath, queueLines);
                }

                PrtDefListBox.Items.Remove(selectedLine);
                OutputBox.AppendText($"Deleted '{printerName}' from local files.\n");

                await RefreshPrtDefWithDelay();
            }
            catch (Exception ex)
            {
                OutputBox.AppendText($"❌ Error: {ex.Message}\n");
                ButtonsEnabled(true);
            }
            ButtonsEnabled(true);
        }

        private async void DeletePrinterGroup_Click(object sender, RoutedEventArgs e)
        {
            ButtonsEnabled(false);

            if (PrtDefListBox.SelectedItem == null)
            {
                MessageBox.Show("Select a printer to determine the group by timestamp.");
                ButtonsEnabled(true);
                return;
            }

            if (PrtDefListBox.SelectedItem is not string selectedLine)
            {
                MessageBox.Show("Select a valid printer from the list.");
                ButtonsEnabled(true);
                return;
            }

            string[] parts = selectedLine.Split('|');
            if (parts.Length < 2)
            {
                MessageBox.Show("Selected printer line format is invalid.");
                ButtonsEnabled(true);
                return;
            }

            string timestamp = parts[1].Trim();

            if (!File.Exists(prtDefFilePath))
            {
                MessageBox.Show("PrtDef.txt file not found.");
                ButtonsEnabled(true);
                return;
            }

            var allLines = File.ReadAllLines(prtDefFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            var groupLines = allLines
                .Where(line =>
                {
                    var split = line.Split('|');
                    return split.Length >= 2 && split[1].Trim() == timestamp;
                })
                .ToList();

            if (groupLines.Count == 0)
            {
                OutputBox.AppendText($"No printers found with timestamp '{timestamp}'.\n");
                ButtonsEnabled(true);
                return;
            }

            string hostname = this.HOSTNAME;
            string token = this.SOAPTOKEN;
            string sessId = this.SESSIONID;
            string vpsid = this.VPSID;

            if (string.IsNullOrEmpty(hostname) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sessId) || string.IsNullOrEmpty(vpsid))
            {
                MessageBox.Show("Error loading necessary data from login interface. Unable to run this delete command.\n");
                ButtonsEnabled(true);
                return;
            }

            try
            {
                OutputBox.AppendText($"Session ID: {sessId}\n");

                foreach (string line in groupLines)
                {
                    string prtName = line.Split('|')[0].Trim();
                    OutputBox.AppendText($"Deleting printer '{prtName}' from VPSX...\n");
                    await DeletePrinter(sessId, hostname, vpsid, prtName);
                    OutputBox.AppendText($"Deleted '{prtName}' from VPSX.\n");
                }

                var remainingLines = allLines.Except(groupLines).ToList();
                File.WriteAllLines(prtDefFilePath, remainingLines);

                if (File.Exists(queueFilePath))
                {
                    var queueLines = File.ReadAllLines(queueFilePath).ToList();
                    var prtNamesToRemove = groupLines.Select(l => l.Split('|')[0].Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var updatedQueueLines = queueLines.Where(line => !prtNamesToRemove.Contains(line.Trim())).ToList();
                    File.WriteAllLines(queueFilePath, updatedQueueLines);
                }

                foreach (string line in groupLines)
                {
                    PrtDefListBox.Items.Remove(line);
                }

                OutputBox.AppendText($"Deleted printer group with timestamp '{timestamp}'.\n");

                await RefreshPrtDefWithDelay();
            }
            catch (Exception ex)
            {
                OutputBox.AppendText($"Error deleting printer group: {ex.Message}\n");
                ButtonsEnabled(true);
            }
            ButtonsEnabled(true);
        }

        private async Task LoadPrtDefToViewer()
        {
            string hostname = this.HOSTNAME;
            string sessId = this.SESSIONID;
            string vpsid = this.VPSID;
            string holdQ = HoldQNameBox.Text.Trim();
            string HOLDQ = string.IsNullOrEmpty(holdQ) ? "" : $"1{holdQ}";

            Dispatcher.Invoke(() => OutputBox.AppendText("Starting LoadPrtDefToViewer...\n"));

            // Validate parameters
            if (string.IsNullOrEmpty(hostname) || string.IsNullOrEmpty(sessId) || string.IsNullOrEmpty(vpsid))
            {
                Dispatcher.Invoke(() => OutputBox.AppendText("Error: Missing necessary data (hostname, session ID, or VPSID). Cannot fetch printers.\n"));
                return;
            }

            try
            {
                // Create PrtDef.txt if it doesn't exist
                if (!File.Exists(prtDefFilePath))
                {
                    File.WriteAllText(prtDefFilePath, "");
                    //Dispatcher.Invoke(() => OutputBox.AppendText($"Created empty PrtDef.txt at {prtDefFilePath}.\n"));
                }

                // Fetch all printers from VPSX with group name "Printer Adder"
                //Dispatcher.Invoke(() => OutputBox.AppendText("Fetching all printers from VPSX with group 'Printer Adder'...\n"));
                var soapPrinters = await GetPrintersByGroupName(sessId, hostname, vpsid, "Printer Adder");
                //Dispatcher.Invoke(() => OutputBox.AppendText($"Retrieved {soapPrinters.Count} printers from VPSX.\n"));

                // Read existing PrtDef.txt entries
                var fileLines = File.Exists(prtDefFilePath)
                    ? File.ReadAllLines(prtDefFilePath)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Select(l => l.Trim())
                        .ToList()
                    : new List<string>();
                var filePrinterNames = fileLines
                    .Select(line => line.Split('|')[0].Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Identify and process only new printers
                var missingPrinters = soapPrinters
                    .Where(p => !string.IsNullOrEmpty(p.Name) && !filePrinterNames.Contains(p.Name))
                    .Distinct() // Remove duplicates
                    .ToList();

                // Process missing printers
                var newLines = new List<string>();
                foreach (var printer in missingPrinters)
                {
                    string printerType = await IsHoldQueuePrinter(printer.Name) ? "Hold Queue" : "Base Printer";
                    string newLine = $"{printer.Name} | {printer.Contact} | {printerType}";
                    newLines.Add(newLine);
                    Dispatcher.Invoke(() => OutputBox.AppendText($"Adding missing printer to PrtDef.txt: {newLine}\n"));
                }

                // Append new printers to PrtDef.txt
                if (newLines.Any())
                {
                    await WriteFileWithRetry(prtDefFilePath, string.Join(Environment.NewLine, newLines) + Environment.NewLine, true, 3, 1000);
                    Dispatcher.Invoke(() => OutputBox.AppendText($"Appended {newLines.Count} missing printers to PrtDef.txt.\n"));
                }
                else
                {
                    Dispatcher.Invoke(() => OutputBox.AppendText("No new printers found.\n"));
                }

                // Reload, sort by timestamp, and validate all printers
                var allLines = File.ReadAllLines(prtDefFilePath)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => l.Trim())
                    .OrderBy(l => l.Split('|')[1].Trim()) // Sort all lines by timestamp
                    .ToList();

                PrtDefListBox.Items.Clear();
                var validPrinters = new List<string>();
                foreach (var line in allLines)
                {
                    var printerName = line.Split('|')[0].Trim();
                    bool exists = await VPSXPrinterCheck(sessId, hostname, printerName);
                    if (exists)
                    {
                        validPrinters.Add(line);
                        PrtDefListBox.Items.Add(line);
                    }
                    else
                    {
                        Dispatcher.Invoke(() => OutputBox.AppendText($"Removing non-existent printer from PrtDef.txt: {printerName}\n"));
                    }
                }

                // Write back only valid printers to PrtDef.txt
                await WriteFileWithRetry(prtDefFilePath, string.Join(Environment.NewLine, validPrinters) + (validPrinters.Any() ? Environment.NewLine : ""), false, 3, 1000);
                Dispatcher.Invoke(() => OutputBox.AppendText($"Updated PrtDef.txt with {validPrinters.Count} valid printers.\n"));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => OutputBox.AppendText($"Error in LoadPrtDefToViewer: {ex.Message}\n"));
            }
        }

        private async Task LoadHoldQueuesToViewer()
        {
            HoldQueueListBox.Items.Clear();

            if (File.Exists(prtDefFilePath))
            {
                var lines = File.ReadAllLines(prtDefFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains("| Hold Queue"))
                    .ToList();

                // Ensure at least one hold queue is present; if not, check for recently added ones
                if (!lines.Any() && File.Exists(prtDefFilePath))
                {
                    var allLines = File.ReadAllLines(prtDefFilePath)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();
                    lines = allLines
                        .Where(line => line.Contains("| Hold Queue"))
                        .ToList();
                }

                foreach (var line in lines)
                {
                    HoldQueueListBox.Items.Add(line.Trim());
                    Dispatcher.Invoke(() => OutputBox.AppendText($"Added hold queue to selection: {line.Trim()}\n"));
                }

                if (lines.Any())
                {
                    Dispatcher.Invoke(() => OutputBox.AppendText($"Loaded {lines.Count} hold queues to HoldQueueListBox.\n"));
                }
                else
                {
                    Dispatcher.Invoke(() => OutputBox.AppendText("No hold queues found in PrtDef.txt.\n"));
                }
            }
            else
            {
                Dispatcher.Invoke(() => OutputBox.AppendText("PrtDef.txt file not found.\n"));
            }
        }

        private void HoldQueueListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RunButton.IsEnabled = true;
            if (HoldQueueListBox.SelectedItem is not string selectedLine || string.IsNullOrWhiteSpace(selectedLine))
            {
                return;
            }

            string[] parts = selectedLine.Split('|');
            if (parts.Length < 2)
            {
                OutputBox.AppendText("Selected hold queue line format is invalid.\n");
                return;
            }

            string timestamp = parts[1].Trim();

            try
            {
                if (File.Exists(prtDefFilePath))
                {
                    var basePrinters = File.ReadAllLines(prtDefFilePath)
                        .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains($"| {timestamp} | Base Printer"))
                        .Select(line => line.Split('|')[0].Trim())
                        .ToList();

                    if (basePrinters.Count == 0)
                    {
                        OutputBox.AppendText($"No base printers found for timestamp '{timestamp}'.\n");
                        return;
                    }

                    File.WriteAllText(queueFilePath, "");
                    File.WriteAllLines(queueFilePath, basePrinters);
                    OutputBox.AppendText($"Updated Queues.txt with {basePrinters.Count} base printers for timestamp '{timestamp}'.\n");
                    RunButton.IsEnabled = true;
                    CancelButton.IsEnabled = false;
                }
                else
                {
                    OutputBox.AppendText("PrtDef.txt file not found.\n");
                }
            }
            catch (Exception ex)
            {
                OutputBox.AppendText($"Error updating Queues.txt: {ex.Message}\n");
            }
        }

        private void RefreshPrtDef_Click(object sender, RoutedEventArgs e)
        {
            ButtonsEnabled(false);
            OutputBox.AppendText("Starting refresh of printer definitions...\n");
            RefreshPrtDefWithDelay(); // Use delayed refresh to maintain consistency
            ButtonsEnabled(true);
        }

        private async Task RefreshPrtDefWithDelay(int delayMs = 100)
        {
            await Task.Delay(delayMs);
            if (!_isLoaded) return; // Prevent refresh if not fully initialized
            OutputBox.AppendText("Starting automatic refresh of printer definitions...\n");
            await LoadPrtDefToViewer();
            LoadHoldQueuesToViewer(); // Ensure hold queues are updated after every refresh
            OutputBox.AppendText("🔄 Refreshed PrtDef and Hold Queue lists.\n");
        }

        private void ButtonsEnabled(bool enabled)
        {
            RefreshButton.IsEnabled = enabled;
            ClearPrtDefButton.IsEnabled = enabled;
            DeleteGroupButton.IsEnabled = enabled;
            DeleteSingleButton.IsEnabled = enabled;
        }

        private bool PrtDefContains(string printerName)
        {
            if (!File.Exists(prtDefFilePath))
            {
                OutputBox.AppendText($"PrtDef file cannot be found at {prtDefFilePath}.\n");
                return false;
            }

            var fileLines = File.ReadAllLines(prtDefFilePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            return fileLines.Any(line => line.StartsWith(printerName + " |"));
        }

        private async Task RunClientLoadTester(string hostname, string soapToken, string vpsid, string sessionId)
        {
            try
            {
                string loadGenRoot = FindLoadGenFolder();
                string CLTexePath = Path.Combine(
                    loadGenRoot,
                    "RUN EXES HERE",
                    "Load Tester Simulator .v3",
                    "References",
                    "LogIn",
                    "PD",
                    "CLT",
                    "ClientLoadTester.exe"
                );

                if (!File.Exists(CLTexePath))
                {
                    throw new FileNotFoundException($"ClientLoadTester executable not found at path: {CLTexePath}");
                }

                List<string> queuesList = File.ReadAllLines(queueFilePath).ToList();
                string queuesString = string.Join(" ", queuesList);

                string args = $"-hostname \"{hostname}\" -soapToken \"{soapToken}\" -vpsid \"{vpsid}\" -sessionId \"{sessionId}\" -queues \"{queuesString}\"";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = CLTexePath,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText("ClientLoadTester finished.\n");
                    if (!string.IsNullOrWhiteSpace(output))
                        OutputBox.AppendText("Output:\n" + output + "\n");
                    if (!string.IsNullOrWhiteSpace(error))
                        OutputBox.AppendText("Errors:\n" + error + "\n");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText("Failed to run ClientLoadTester: " + ex.Message + "\n");
                });
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

        private async Task<string> GetSessionId(string soapToken, string hostname)
        {
            string url = $"https://{hostname}/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX";

            string envelope = $@"
            <soapenv:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' 
                              xmlns:xsd='http://www.w3.org/2001/XMLSchema' 
                              xmlns:soapenv='http://schemas.xmlsoap.org/soap/envelope/' 
                              xmlns:lrs='http://www.lrs.com'>
              <soapenv:Header/>
              <soapenv:Body>
                <lrs:Gateway_Logon soapenv:encodingStyle='http://schemas.xmlsoap.org/soap/encoding/'>
                  <soapToken xsi:type='xsd:string'>{soapToken}</soapToken>
                </lrs:Gateway_Logon>
              </soapenv:Body>
            </soapenv:Envelope>";

            try
            {
                using var client = new HttpClient();
                var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
                var response = await client.PostAsync(url, content);
                string body = await response.Content.ReadAsStringAsync();

                XmlDocument xml = new XmlDocument();
                xml.LoadXml(body);
                XmlNode? sessNode = xml.SelectSingleNode("//SessID");

                if (sessNode == null || string.IsNullOrWhiteSpace(sessNode.InnerText))
                {
                    throw new Exception("Invalid Soap Token. Please try again.\n");
                }
                return sessNode.InnerText;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("No such host is known."))
                {
                    throw new Exception("Invalid Hostname. Please try again.\n");
                }
                else
                {
                    throw new Exception(ex.Message);
                }
            }
        }

        private async Task SetPrinter(string sessionId, string hostname, string vpsid, string printerName, string licenseState, string? tcpPrtr)
        {
            string url = $"https://{hostname}/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX";
            bool isLicenseOn = licenseState.ToLower() == "on";
            string body = $@"<soapenv:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                                          xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                                          xmlns:soapenv='http://schemas.xmlsoap.org/soap/envelope/'
                                          xmlns:lrs='http://www.lrs.com'>
                <soapenv:Header/>
                <soapenv:Body>
                    <lrs:VPSX_PrinterSet soapenv:encodingStyle='http://schemas.xmlsoap.org/soap/encoding/'>
                        <SessID xsi:type='xsd:string'>{sessionId}</SessID>
                        <PrtName xsi:type='xsd:string'>{printerName}</PrtName>
                        <VPSID xsi:type='xsd:string'>{vpsid}</VPSID>
                        <TCPPRTR xsi:type='xsd:string'>{(!isLicenseOn ? "" : tcpPrtr ?? "")}</TCPPRTR>
                    </lrs:VPSX_PrinterSet>
                </soapenv:Body>
            </soapenv:Envelope>";

            using var client = new HttpClient();
            var response = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "text/xml"));
            string result = await response.Content.ReadAsStringAsync();

            if (result.Contains("<SOAP-ENV:Fault>"))
            {
                Dispatcher.Invoke(() => OutputBox.AppendText($"SOAP Fault for setting printer {printerName}:\n{result}\n"));
                throw new Exception($"Error setting printer {printerName}.");
            }
        }

        private async Task AddPrinter(string sessionId, string hostname, string vpsid, string name,
            string commType, string? tcpHost, int tcpPort, bool holdFlag, string? tcpPrtr, bool PDM, bool WIN, bool ENTR, string timestamp)
        {
            string url = $"https://{hostname}/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX";
            string body = $@"<soapenv:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                                          xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                                          xmlns:soapenv='http://schemas.xmlsoap.org/soap/envelope/'
                                          xmlns:lrs='http://www.lrs.com'>
        <soapenv:Header/>
        <soapenv:Body>
            <lrs:VPSX_PrinterAdd soapenv:encodingStyle='http://schemas.xmlsoap.org/soap/encoding/'>
                <SessID xsi:type='xsd:string'>{sessionId}</SessID>
                <PrtName xsi:type='xsd:string'>{name}</PrtName>
                <VPSID xsi:type='xsd:string'>{vpsid}</VPSID>
                <AFFLCLNTTO xsi:type='xsd:int'>30</AFFLCLNTTO>
                <COMMTYPE xsi:type='xsd:string'>{commType}</COMMTYPE>
                <EKEY xsi:type='xsd:string'>dynamic</EKEY>
                <ERRACTN xsi:type='xsd:string'>HOLD</ERRACTN>
                <SNMPCOMM xsi:type='xsd:string'>public</SNMPCOMM>
                {(tcpHost != null ? $"<TCPHOST xsi:type='xsd:string'>{tcpHost}</TCPHOST>" : "")}
                <TCPRPORT xsi:type='xsd:int'>{tcpPort}</TCPRPORT>
                {(holdFlag ? "<HOLD xsi:type='xsd:string'>Y</HOLD>" : "")}
                {(tcpPrtr != null ? $"<TCPPRTR xsi:type='xsd:string'>{tcpPrtr}</TCPPRTR>" : "")}
                {(PDM ? "<LICPDM xsi:type='xsd:string'>Y</LICPDM>" : "")}
                {(WIN ? "<LICWINDOWS xsi:type='xsd:string'>Y</LICWINDOWS>" : "")}
                {(ENTR ? "<LICENTERPRISE xsi:type='xsd:string'>Y</LICENTERPRISE>" : "")}
                <CONTACT xsi:type='xsd:string'>{timestamp}</CONTACT>
                <GRPNAME xsi:type='xsd:string'>Printer Adder</GRPNAME>
            </lrs:VPSX_PrinterAdd>
        </soapenv:Body>
    </soapenv:Envelope>";

            var client = new HttpClient();
            var response = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "text/xml"));
            string result = await response.Content.ReadAsStringAsync();

            if (result.Contains("<SOAP-ENV:Fault>"))
            {
                OutputBox.AppendText(result);
                throw new Exception("Error adding printer.");
            }
        }

        private async Task DeletePrinter(string sessionId, string hostname, string vpsid, string printerName)
        {
            string url = $"https://{hostname}/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX";

            using var client = new HttpClient();

            async Task PurgeJobs()
            {
                int queueType = FindPrtType(printerName);
                OutputBox.AppendText($" Queue type is {queueType}.\n");

                string body = $@"<soapenv:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                                           xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                                           xmlns:soapenv='http://schemas.xmlsoap.org/soap/envelope/'
                                           xmlns:lrs='http://www.lrs.com'>
                <soapenv:Header/>
                    <soapenv:Body>
                        <lrs:VPSX_SpoolDeleteAll soapenv:encodingStyle='http://schemas.xmlsoap.org/soap/encoding/'>
                            <SessID xsi:type='xsd:string'>{sessionId}</SessID>
                            <VPSID xsi:type='xsd:string'>{vpsid}</VPSID>
                            <PrtName xsi:type='xsd:string'>{printerName}</PrtName>
                            <QueType xsi:type='xsd:int'>{queueType}</QueType>
                        </lrs:VPSX_SpoolDeleteAll>
                    </soapenv:Body>
                </soapenv:Envelope>";

                var response = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "text/xml"));
                string result = await response.Content.ReadAsStringAsync();

                if (result.Contains("<SOAP-ENV:Fault>"))
                {
                    throw new Exception($"Error purging {(queueType == 0 ? "queued" : "retained")} jobs for printer {printerName}.");
                }
            }

            await PurgeJobs();

            string deletePrinterBody = $@"<soapenv:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                                              xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                                              xmlns:soapenv='http://schemas.xmlsoap.org/soap/envelope/'
                                              xmlns:lrs='http://www.lrs.com'>
            <soapenv:Header/>
                <soapenv:Body>
                    <lrs:VPSX_PrinterDel soapenv:encodingStyle='http://schemas.xmlsoap.org/soap/encoding/'>
                        <SessID xsi:type='xsd:string'>{sessionId}</SessID>
                        <VPSID xsi:type='xsd:string'>{vpsid}</VPSID>
                        <PrtName xsi:type='xsd:string'>{printerName}</PrtName>
                    </lrs:VPSX_PrinterDel>
                </soapenv:Body>
            </soapenv:Envelope>";

            var deletePrinterResponse = await client.PostAsync(url, new StringContent(deletePrinterBody, Encoding.UTF8, "text/xml"));
            string deletePrinterResult = await deletePrinterResponse.Content.ReadAsStringAsync();

            if (deletePrinterResult.Contains("<SOAP-ENV:Fault>"))
            {
                throw new Exception($"Error deleting printer {printerName}.");
            }
        }

        private int FindPrtType(string prtname)
        {
            var currentItems = PrtDefListBox.Items.Cast<string>().ToList();
            foreach (var item in currentItems)
            {
                if (item.Contains(prtname))
                {
                    if (item.Contains("Hold Queue")) { return 0; }
                    if (item.Contains("Base Printer")) { return 1; }
                }
            }
            return 2;
        }

        private async Task<bool> IsHoldQueuePrinter(string printerName)
        {
            // Determine hold queue based on name pattern
            // If starts with "1_" it's a base printer, if starts with "1" followed by some name (e.g., "1H") it's a hold queue
            return printerName.StartsWith("1") && !printerName.StartsWith("1_");
        }

        public async Task<List<PrinterInfo>> GetPrintersByGroupName(string sessionId, string hostname, string vpsid, string groupName)
        {
            var printers = new List<PrinterInfo>();
            string url = $"https://{hostname}/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX";
            const int arrayMax = 50; // Set a reasonable batch size
            int offset = 0;

            while (true)
            {
                string body = $@"<soapenv:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                                              xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                                              xmlns:soapenv='http://schemas.xmlsoap.org/soap/envelope/'
                                              xmlns:lrs='http://www.lrs.com'>
                    <soapenv:Header/>
                    <soapenv:Body>
                        <lrs:VPS_PrtList4 soapenv:encodingStyle='http://schemas.xmlsoap.org/soap/encoding/'>
                            <SessID xsi:type='xsd:string'>{sessionId}</SessID>
                            <VPSID xsi:type='xsd:string'>{vpsid}</VPSID>
                            <GroupMask xsi:type='xsd:string'>{groupName}</GroupMask>
                            <Offset xsi:type='xsd:int'>{offset}</Offset>
                            <ArrayMax xsi:type='xsd:int'>{arrayMax}</ArrayMax>
                        </lrs:VPS_PrtList4>
                    </soapenv:Body>
                </soapenv:Envelope>";

                Dispatcher.Invoke(() => OutputBox.AppendText($"Sending SOAP request to {url} for group '{groupName}' with offset {offset}...\n"));

                try
                {
                    using var client = new HttpClient();
                    var response = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "text/xml"));
                    string result = await response.Content.ReadAsStringAsync();

                    // Log the raw SOAP response
                    //Dispatcher.Invoke(() => OutputBox.AppendText($"Raw SOAP response:\n{result}\n"));

                    if (result.Contains("<SOAP-ENV:Fault>"))
                    {
                        //Dispatcher.Invoke(() => OutputBox.AppendText($"SOAP Fault in GetPrintersByGroupName: {result}\n"));
                        throw new Exception($"Error retrieving printer list for group '{groupName}' at offset {offset}.");
                    }

                    var xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(result);

                    var nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
                    nsmgr.AddNamespace("SOAP-ENV", "http://schemas.xmlsoap.org/soap/envelope/");
                    nsmgr.AddNamespace("m", "http://www.lrs.com");
                    nsmgr.AddNamespace("lrs", "http://www.lrs.com");
                    nsmgr.AddNamespace("SOAP-ENC", "http://schemas.xmlsoap.org/soap/encoding/");
                    nsmgr.AddNamespace("xsi", "http://www.w3.org/2001/XMLSchema-instance");
                    nsmgr.AddNamespace("xsd", "http://www.w3.org/2001/XMLSchema");

                    var totalPrtNode = xmlDoc.SelectSingleNode("//m:VPS_PrtList4Response/TotalPrt", nsmgr);
                    int totalPrt = totalPrtNode != null ? int.Parse(totalPrtNode.InnerText) : 0;
                    var printerNodes = xmlDoc.SelectNodes("//m:VPS_PrtList4Response/PrtList/item", nsmgr);
                    Dispatcher.Invoke(() => OutputBox.AppendText($"Found {printerNodes?.Count ?? 0} printer nodes in response, TotalPrt={totalPrt}.\n"));

                    if (printerNodes == null || printerNodes.Count == 0)
                    {
                        //Dispatcher.Invoke(() => OutputBox.AppendText($"No more printers found for group '{groupName}' at offset {offset}.\n"));
                        break;
                    }

                    foreach (XmlNode node in printerNodes)
                    {
                        var printer = new PrinterInfo();
                        var prtNameNode = node.SelectSingleNode("PrtName", nsmgr);
                        var contactNode = node.SelectSingleNode("Contact", nsmgr);

                        printer.Name = prtNameNode?.InnerText ?? "";
                        printer.Contact = contactNode?.InnerText ?? "";

                        if (!string.IsNullOrEmpty(printer.Name))
                        {
                            printers.Add(printer);
                            //Dispatcher.Invoke(() => OutputBox.AppendText($"Found printer: {printer.Name}, Contact: {printer.Contact}\n"));
                        }
                    }

                    if (printers.Count >= totalPrt || printerNodes.Count < arrayMax)
                    {
                        break; // All printers retrieved
                    }

                    offset += arrayMax;
                }
                catch (Exception ex)
                {
                    //Dispatcher.Invoke(() => OutputBox.AppendText($"Error in GetPrintersByGroupName at offset {offset}: {ex.Message}\n"));
                    break;
                }
            }

            return printers;
        }

        private async Task<bool> VPSXPrinterCheck(string sessionId, string hostname, string printerName)
        {
            string url = $"https://{hostname}/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX";

            string body = $@"<soapenv:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                                                  xmlns:soapenv='http://schemas.xmlsoap.org/soap/envelope/' xmlns:lrs='http://www.lrs.com'>
           <soapenv:Header/>
           <soapenv:Body>
              <lrs:VPS_PrtList1 soapenv:encodingStyle='http://schemas.xmlsoap.org/soap/encoding/'>
                 <SessID xsi:type='xsd:string'>{sessionId}</SessID>
                 <Find xsi:type='xsd:string'>{printerName}</Find>
              </lrs:VPS_PrtList1>
           </soapenv:Body>
        </soapenv:Envelope>";

            var client = new HttpClient();
            var response = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "text/xml"));
            string result = await response.Content.ReadAsStringAsync();

            if (result.Contains("<SOAP-ENV:Fault>"))
            {
                OutputBox.AppendText(result);
                throw new Exception($"SOAP fault while checking printer: {printerName}");
            }

            if (result.Contains(@"<TotalPrt xsi:type='xsd:int'>0</TotalPrt>"))
            {
                return false;
            }
            return true;
        }
    }
}
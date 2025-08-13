using LiveCharts;
using LiveCharts.Wpf;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClientLoadTester
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly string HOSTNAMEE;
        private readonly string SOAPTOKENN;
        private readonly string VPSIDD;
        private readonly string SESSIONIDD;
        private readonly string QUEUESS;
        private bool _isLoaded;

        private double _progress = 0;
        private int _totalBatches = 0;

        private CancellationTokenSource _cts;
        private bool _isRunning = false;
        private RotateTransform _rotateTransform;
        private Task _mainTask;
        private NamedPipeServerStream _pipeServer;
        private Process _driverProcess;
        private Process _metricsProcess;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _runtimeText;
        public string RuntimeText
        {
            get => _runtimeText;
            set
            {
                if (_runtimeText != value)
                {
                    _runtimeText = value;
                    OnPropertyChanged(nameof(RuntimeText));
                }
            }
        }

        private string _jobNumberText;
        public string JobNumberText
        {
            get => _jobNumberText;
            set
            {
                if (_jobNumberText != value)
                {
                    _jobNumberText = value;
                    OnPropertyChanged(nameof(JobNumberText));
                }
            }
        }

        private string _stepSizeText;
        public string StepSizeText
        {
            get => _stepSizeText;
            set
            {
                if (_stepSizeText != value)
                {
                    _stepSizeText = value;
                    OnPropertyChanged(nameof(StepSizeText));
                }
            }
        }

        private string _usernameText;
        public string UsernameText
        {
            get => _usernameText;
            set
            {
                if (_usernameText != value)
                {
                    _usernameText = value;
                    OnPropertyChanged(nameof(UsernameText));
                }
            }
        }

        private string _passwordText;
        public string PasswordText
        {
            get => _passwordText;
            set
            {
                if (_passwordText != value)
                {
                    _passwordText = value;
                    OnPropertyChanged(nameof(PasswordText));
                }
            }
        }

        private string _logText;
        public string LogText
        {
            get => _logText;
            set
            {
                if (_logText != value)
                {
                    _logText = value;
                    OnPropertyChanged(nameof(LogText));
                }
            }
        }

        public MainWindow(string HOSTNAME, string SOAPTOKEN, string VPSID, string SESSIONID, string QUEUES)
        {
            InitializeComponent();
            DataContext = this;
            ColorSlider.Value = 210;
            _isLoaded = true;

            this.HOSTNAMEE = HOSTNAME.Trim();
            this.SOAPTOKENN = SOAPTOKEN.Trim();
            this.VPSIDD = VPSID.Trim();
            this.SESSIONIDD = SESSIONID.Trim();
            this.QUEUESS = QUEUES.Trim();

            SetupLogging();
            SetupLoadingAnimation();
            ListenForProgressUpdates();
        }

        private void SetupLogging()
        {
            try
            {
                Log.ResetTempLogs();
                Log.Write("Application started.");
                Log.WriteTempLog("Load Tester 3000 initialized.");
                UpdateLogText();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize logging: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateLogText()
        {
            try
            {
                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "templogs.txt");
                if (File.Exists(logFilePath))
                {
                    using (FileStream fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader reader = new StreamReader(fileStream))
                    {
                        Dispatcher.Invoke(() => LogText = reader.ReadToEnd());
                    }
                }
                else
                {
                    Log.Write($"Log file not found: {logFilePath}");
                    Dispatcher.Invoke(() => LogText = "Log file not found.");
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Failed to read templogs.txt: {ex.Message}");
                Dispatcher.Invoke(() => LogText = $"Error reading logs: {ex.Message}");
            }
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
                MessageBox.Show($"Error opening context menu: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Error opening theme popup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Error closing theme popup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    Color lightColor = Color.FromArgb(51, baseColor.R, baseColor.G, baseColor.B);
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
                MessageBox.Show($"Error changing theme color: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void About_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBox.Show("Load Tester 3000\nVersion 3.0", "About", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing About dialog: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exiting application: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            var passwordBox = sender as PasswordBox;
            if (passwordBox != null)
            {
                PasswordText = passwordBox.Password;
            }
        }

        private void SetupLoadingAnimation()
        {
            _rotateTransform = new RotateTransform();
            LoadingPath.RenderTransform = _rotateTransform;
        }

        private async void ListenForProgressUpdates()
        {
            while (true) // CHANGE: Allow continuous listening for multiple runs
            {
                try
                {
                    using (var server = new NamedPipeServerStream("ProgressPipe", PipeDirection.In))
                    {
                        await server.WaitForConnectionAsync();
                        using (var reader = new StreamReader(server, Encoding.UTF8))
                        {
                            while (true)
                            {
                                string message = await reader.ReadLineAsync();
                                if (message == null) break;
                                if (message.StartsWith("set_progress_config:"))
                                {
                                    var parts = message.Substring("set_progress_config:".Length).Split(',');
                                    if (parts.Length == 2 &&
                                        int.TryParse(parts[0], out int totalJobs) &&
                                        int.TryParse(parts[1], out int stepValue) &&
                                        totalJobs > 0 && stepValue > 0)
                                    {
                                        _totalBatches = (int)Math.Ceiling((double)totalJobs / stepValue);
                                        Dispatcher.Invoke(() =>
                                        {
                                            TestProgressBar.Maximum = _totalBatches;
                                            _progress = 0;
                                            TestProgressBar.Value = 0;
                                        });
                                        Log.WriteTempLog($"Progress configured: Total Jobs = {totalJobs}, Step Size = {stepValue}, Batches = {_totalBatches}");
                                        Dispatcher.Invoke(UpdateLogText);
                                    }
                                }
                                else if (message == "increment")
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        _progress = Math.Min(_progress + 1, TestProgressBar.Maximum);
                                        TestProgressBar.Value = _progress;
                                        double percentComplete = (_progress / _totalBatches) * 100;
                                        Log.WriteTempLog($"Progress: Batch {_progress}/{_totalBatches} ({percentComplete:F1}%)");
                                        UpdateLogText();
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    //Log.Write($"Progress pipe error: {ex.Message}");
                    //Log.WriteTempLog($"Progress pipe error: {ex.Message}");
                    Dispatcher.Invoke(UpdateLogText);
                    await Task.Delay(1000); // CHANGE: Wait before retrying to avoid rapid looping
                }
            }
        }

        private Brush GetStatusBrush(string status)
        {
            if (status.ToLower() == "idle")
                return Brushes.Blue;
            else if (status.ToLower() == "error")
                return Brushes.Red;
            else
                return Brushes.Green;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void TestTypeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TestTypeListBox.SelectedItem is ListBoxItem selectedItem)
            {
                string testType = selectedItem.Content.ToString();
                switch (testType)
                {
                    case "Normal Day":
                        Log.Write("Parameters for Normal Day");
                        Log.WriteTempLog($"Selected Test Type: Normal Day");
                        RuntimeText = "120";
                        JobNumberText = "150000";
                        StepSizeText = "10";
                        UsernameText = "dummy";
                        PasswordText = "dummy";
                        break;
                    case "Busy/Quiet Switching":
                        Log.Write("Parameters for Busy/Quiet Switching");
                        Log.WriteTempLog($"Selected Test Type: Busy/Quiet Switching");
                        RuntimeText = "120";
                        JobNumberText = "150000";
                        StepSizeText = "10";
                        UsernameText = "dummy";
                        PasswordText = "dummy";
                        break;
                    case "Gradual Increase":
                        Log.Write("Parameters for Gradual Increase");
                        Log.WriteTempLog($"Selected Test Type: Gradual Increase");
                        RuntimeText = "0";
                        JobNumberText = "0";
                        StepSizeText = "0";
                        UsernameText = "dummy";
                        PasswordText = "dummy";
                        break;
                    default:
                        Log.Write("Parameters for Unknown Test Type");
                        Log.WriteTempLog($"Selected Test Type: Unknown ({testType})");
                        RuntimeText = "0";
                        JobNumberText = "0";
                        StepSizeText = "0";
                        UsernameText = "dummy";
                        PasswordText = "dummy";
                        break;
                }
                Log.Write($"Selected Test Type: {testType}");
                Dispatcher.Invoke(UpdateLogText);
            }
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                StatusText.Text = "A test is already running.";
                Log.Write("A test is already running.");
                Log.WriteTempLog("Test start failed: Another test is already running.");
                Dispatcher.Invoke(UpdateLogText);
                return;
            }

            Log.ResetTempLogs();
            Log.WriteTempLog("Starting new test...");
            Dispatcher.Invoke(UpdateLogText);

            // Reset progress bar
            try
            {
                Dispatcher.Invoke(() =>
                {
                    _progress = 0;
                    TestProgressBar.Value = 0;
                    TestProgressBar.Maximum = 0; // CHANGE: Reset maximum to ensure clean state
                    Log.WriteTempLog("Progress bar reset to 0.");
                    UpdateLogText();
                });
            }
            catch (Exception ex)
            {
                Log.Write($"Failed to reset progress bar: {ex.Message}");
                Log.WriteTempLog($"Failed to reset progress bar: {ex.Message}");
                Dispatcher.Invoke(UpdateLogText);
            }

            string soapToken = this.SOAPTOKENN;
            string hostname = this.HOSTNAMEE;
            string vpsid = this.VPSIDD;
            string sessionId = this.SESSIONIDD;
            string queues = this.QUEUESS;
            string username = UsernameText;
            string password = PasswordText;

            if (
                !int.TryParse(RuntimeText, out int runtime) ||
                !int.TryParse(JobNumberText, out int totalJobs) ||
                !int.TryParse(StepSizeText, out int stepSize) ||
                string.IsNullOrWhiteSpace(UsernameText) ||
                string.IsNullOrWhiteSpace(PasswordText))
            {
                StatusText.Text = "Invalid input. Please check that all fields are correct.";
                StatusText.Foreground = new SolidColorBrush(Colors.Red);
                Log.Write("Invalid input. Please check that all fields are correct.");
                Log.WriteTempLog("Test start failed: Invalid input in one or more fields.");
                Dispatcher.Invoke(UpdateLogText);
                return;
            }

            if (string.IsNullOrWhiteSpace(hostname) ||
                string.IsNullOrWhiteSpace(soapToken) ||
                string.IsNullOrWhiteSpace(vpsid) ||
                string.IsNullOrWhiteSpace(sessionId))
            {
                StatusText.Text = "Error passing necessary arguments from Printer Setup interface.";
                StatusText.Foreground = new SolidColorBrush(Colors.Red);
                Log.Write("Error passing necessary arguments from Printer Setup interface.");
                Log.WriteTempLog("Test start failed: Missing arguments from Printer Setup interface.");
                Dispatcher.Invoke(UpdateLogText);
                return;
            }

            LoadingSymbol.Visibility = Visibility.Visible;
            RunButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            _isRunning = true;
            StatusText.Text = "Test running...";
            Log.Write("Test running...");
            Log.WriteTempLog($"Test started with parameters: Total Jobs = {totalJobs}, Step Size = {stepSize}, Username = {username}, Queues = {queues}");
            Dispatcher.Invoke(UpdateLogText);

            _cts?.Dispose(); // CHANGE: Dispose previous CancellationTokenSource
            _cts = new CancellationTokenSource();

            try
            {
                var animationTask = AnimateLoading();
                await Task.Delay(3000, _cts.Token);
                _mainTask = Task.Run(async () =>
                {
                    if (!_cts.Token.IsCancellationRequested)
                    {
                        Log.Write($"Job Number: {totalJobs}");
                        Log.Write($"Step Size: {stepSize}");
                        Log.Write($"Soap Token: {soapToken}");
                        Log.Write($"Hostname: {hostname}");
                        Log.Write($"VPSID: {vpsid}");
                        Log.Write($"Username: {username}");
                        Log.Write($"Password: {password.Substring(0, 2)}-");
                        Log.Write($"SessionId: {sessionId}");
                        Log.Write($"Queues: {queues}");
                        await RunDriverTest(totalJobs, stepSize, soapToken, hostname, vpsid, username, password, sessionId, queues);
                        RunMetricsProcess(hostname, soapToken, vpsid, sessionId);
                    }
                }, _cts.Token);
                await _mainTask;
                StatusText.Text = "Test Complete";
                StatusText.Foreground = new SolidColorBrush(Colors.Green);
                Log.Write("Test Complete");
                Log.WriteTempLog("Test completed successfully.");
                Dispatcher.Invoke(UpdateLogText);
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Test was stopped by the user.";
                Log.Write("Test was stopped by the user.");
                Log.WriteTempLog("Test stopped by user.");
                Dispatcher.Invoke(UpdateLogText);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"An error occurred: {ex.Message}";
                Log.Write($"An error occurred: {ex.Message}");
                Log.WriteTempLog($"Test failed: {ex.Message}");
                Dispatcher.Invoke(UpdateLogText);
            }
            finally
            {
                _isRunning = false;
                LoadingSymbol.Visibility = Visibility.Collapsed;
                RunButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                _cts?.Dispose(); // CHANGE: Dispose CancellationTokenSource
            }
        }

        private async Task RunDriverTest(int totalJobs, int stepValue, string soapToken, string hostname, string vpsid, string username, string password, string sessionId, string queues)
        {
            try
            {
                string loadGenRoot = FindLoadGenFolder();
                string driverExePath = Path.Combine(loadGenRoot, "RUN EXES HERE", "Load Tester Simulator .v3", "References", "LogIn", "PD", "CLT", "theOneDriver", "theOneDriver.exe");

                if (!File.Exists(driverExePath))
                    throw new FileNotFoundException($"Driver executable not found at path: {driverExePath}");

                string pipeNameDriver = "CLTto1DPipe_" + Guid.NewGuid();
                Log.Write($"Driver {pipeNameDriver}");
                Log.WriteTempLog($"Starting driver process (Pipe: {pipeNameDriver})");
                Dispatcher.Invoke(UpdateLogText);

                // CHANGE: Ensure previous pipe server is fully disposed
                if (_pipeServer != null)
                {
                    try
                    {
                        if (_pipeServer.IsConnected)
                            _pipeServer.Disconnect();
                        _pipeServer.Dispose();
                        Log.WriteTempLog("Previous pipe server disposed.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Error disposing previous pipe server: {ex.Message}");
                        Log.WriteTempLog($"Error disposing previous pipe server: {ex.Message}");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                    _pipeServer = null;
                }

                // Initialize the pipe server
                _pipeServer = new NamedPipeServerStream(pipeNameDriver, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                Log.WriteTempLog($"Pipe server created for {pipeNameDriver}. Waiting for connection...");
                Dispatcher.Invoke(UpdateLogText);

                // CHANGE: Increased delay to ensure pipe server is ready
                await Task.Delay(1000, _cts.Token);

                string driverArgs = $"-soapToken {soapToken} -totalJobs {totalJobs} -stepValue {stepValue} " +
                                    $"-hostname {hostname} -vpsid {vpsid} -username {username} -password {password} " +
                                    $"-sessionId {sessionId} -pipeName {pipeNameDriver} -queues \"{queues}\"";

                var psiDriver = new ProcessStartInfo
                {
                    FileName = driverExePath,
                    Arguments = driverArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // CHANGE: Ensure previous driver process is terminated
                if (_driverProcess != null && !_driverProcess.HasExited)
                {
                    try
                    {
                        _driverProcess.Kill();
                        _driverProcess.WaitForExit();
                        Log.WriteTempLog("Previous driver process terminated.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Error terminating previous driver process: {ex.Message}");
                        Log.WriteTempLog($"Error terminating previous driver process: {ex.Message}");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                }

                _driverProcess = new Process { StartInfo = psiDriver };
                _driverProcess.OutputDataReceived += (s, args) =>
                {
                    if (args.Data != null)
                    {
                        Log.Write(args.Data);
                        if (args.Data.Contains("Started") || args.Data.Contains("Completed"))
                        {
                            Log.WriteTempLog($"Driver: {args.Data}");
                            Dispatcher.Invoke(UpdateLogText);
                        }
                    }
                };
                _driverProcess.ErrorDataReceived += (s, args) =>
                {
                    if (args.Data != null)
                    {
                        Log.Write("ERROR: " + args.Data);
                        Log.WriteTempLog($"Driver Error: {args.Data}");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                };

                Log.WriteTempLog("Starting driver process...");
                Dispatcher.Invoke(UpdateLogText);
                _driverProcess.Start();
                _driverProcess.BeginOutputReadLine();
                _driverProcess.BeginErrorReadLine();

                // Retry loop for pipe connection
                const int maxRetries = 5; // CHANGE: Increased retries
                int retryCount = 0;
                bool connected = false;
                while (retryCount < maxRetries && !connected && !_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        Log.WriteTempLog($"Attempting pipe connection (Attempt {retryCount + 1}/{maxRetries})...");
                        Dispatcher.Invoke(UpdateLogText);
                        await _pipeServer.WaitForConnectionAsync(_cts.Token);
                        connected = true;
                        Log.WriteTempLog("Pipe connection established with driver process.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        Log.WriteTempLog($"Pipe connection attempt {retryCount} failed: {ex.Message}");
                        Dispatcher.Invoke(UpdateLogText);
                        if (retryCount < maxRetries)
                        {
                            await Task.Delay(1000, _cts.Token); // CHANGE: Increased delay between retries
                        }
                    }
                }

                if (!connected)
                {
                    throw new IOException($"Failed to establish pipe connection after {maxRetries} attempts.");
                }

                await Task.Run(() => _driverProcess.WaitForExit(), _cts.Token);

                Log.Write("Driver process exited normally.");
                Log.WriteTempLog("Driver process completed.");
                Dispatcher.Invoke(UpdateLogText);
            }
            catch (Exception ex)
            {
                Log.Write($"Failed to run driver process: {ex.Message}");
                Log.WriteTempLog($"Driver process failed: {ex.Message}");
                Dispatcher.Invoke(UpdateLogText);
                throw;
            }
            finally
            {
                try
                {
                    if (_pipeServer != null)
                    {
                        if (_pipeServer.IsConnected)
                            _pipeServer.Disconnect();
                        _pipeServer.Dispose();
                        Log.WriteTempLog("Pipe server disposed.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write($"Error disposing pipe server: {ex.Message}");
                    Log.WriteTempLog($"Error disposing pipe server: {ex.Message}");
                    Dispatcher.Invoke(UpdateLogText);
                }
                _pipeServer = null;

                // CHANGE: Ensure driver process is cleaned up
                if (_driverProcess != null && !_driverProcess.HasExited)
                {
                    try
                    {
                        _driverProcess.Kill();
                        _driverProcess.WaitForExit();
                        Log.WriteTempLog("Driver process terminated in cleanup.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Error terminating driver process in cleanup: {ex.Message}");
                        Log.WriteTempLog($"Error terminating driver process in cleanup: {ex.Message}");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                }
                _driverProcess?.Dispose();
                _driverProcess = null;
            }
        }

        private void RunMetricsProcess(string hostname, string soapToken, string vpsid, string sessionId)
        {
            try
            {
                string loadGenRoot = FindLoadGenFolder();
                string metricsScriptPath = Path.Combine(loadGenRoot, "Code", "theOneDriver code", "LRSMetrics.ps1");

                if (!File.Exists(metricsScriptPath))
                    throw new FileNotFoundException($"Metrics script not found at path: {metricsScriptPath}");

                string pipeNameMetrics = "MetricsPipe_" + Guid.NewGuid();
                Log.WriteTempLog($"Starting metrics process (Pipe: {pipeNameMetrics})");
                Dispatcher.Invoke(UpdateLogText);

                string metricsArgs = $"-ExecutionPolicy Bypass -File \"{metricsScriptPath}\" " +
                                     $"-Hostname {hostname} -SoapToken {soapToken} -VpsId {vpsid} -SessionId {sessionId} -pipeName {pipeNameMetrics}";

                var psiMetrics = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = metricsArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // CHANGE: Ensure previous metrics process is terminated
                if (_metricsProcess != null && !_metricsProcess.HasExited)
                {
                    try
                    {
                        _metricsProcess.Kill();
                        _metricsProcess.WaitForExit();
                        Log.WriteTempLog("Previous metrics process terminated.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Error terminating previous metrics process: {ex.Message}");
                        Log.WriteTempLog($"Error terminating previous metrics process: {ex.Message}");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                }

                _metricsProcess = new Process { StartInfo = psiMetrics };
                _metricsProcess.OutputDataReceived += (s, args) =>
                {
                    if (args.Data != null)
                    {
                        Log.Write("METRICS: " + args.Data);
                        if (args.Data.Contains("Started") || args.Data.Contains("Completed"))
                        {
                            Log.WriteTempLog($"Metrics: {args.Data}");
                        }
                    }
                };
                _metricsProcess.ErrorDataReceived += (s, args) =>
                {
                    if (args.Data != null)
                    {
                        Log.Write("METRICS ERROR: " + args.Data);
                        Log.WriteTempLog($"Metrics Error: {args.Data}");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                };

                _metricsProcess.Start();
                _metricsProcess.BeginOutputReadLine();
                _metricsProcess.BeginErrorReadLine();

                Task.Run(async () =>
                {
                    try
                    {
                        using (var metricsPipe = new NamedPipeServerStream(pipeNameMetrics, PipeDirection.Out, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous))
                        {
                            await metricsPipe.WaitForConnectionAsync();
                            using (var writer = new StreamWriter(metricsPipe))
                            {
                                writer.AutoFlush = true;
                                await writer.WriteLineAsync("RUN");
                            }
                        }
                    }
                    catch (Exception pipeEx)
                    {
                        Log.Write("Metrics pipe error: " + pipeEx.Message);
                        Log.WriteTempLog($"Metrics pipe error: {pipeEx.Message}");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Write("Failed to run metrics process: " + ex.Message);
                Log.WriteTempLog($"Metrics process failed: {ex.Message}");
                Dispatcher.Invoke(UpdateLogText);
            }
            finally
            {
                // CHANGE: Ensure metrics process is cleaned up
                if (_metricsProcess != null && !_metricsProcess.HasExited)
                {
                    try
                    {
                        _metricsProcess.Kill();
                        _metricsProcess.WaitForExit();
                        Log.WriteTempLog("Metrics process terminated in cleanup.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"Error terminating metrics process in cleanup: {ex.Message}");
                        Log.WriteTempLog($"Error terminating metrics process in cleanup: {ex.Message}");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                }
                _metricsProcess?.Dispose();
                _metricsProcess = null;
            }
        }

        private async Task AnimateLoading()
        {
            while (_isRunning)
            {
                for (int i = 0; i < 360; i += 10)
                {
                    if (!_isRunning) break;
                    _rotateTransform.Angle = i;
                    await Task.Delay(25);
                }
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                _cts?.Cancel();
                _isRunning = false;
                StatusText.Text = "Stopping test...";
                Log.Write("Test stop requested by user.");
                Log.WriteTempLog("Test stopped by user.");
                Dispatcher.Invoke(UpdateLogText);

                try
                {
                    if (_pipeServer != null && _pipeServer.IsConnected)
                    {
                        using (var writer = new StreamWriter(_pipeServer, Encoding.UTF8, 1024, leaveOpen: true))
                        {
                            writer.AutoFlush = true;
                            await writer.WriteLineAsync("cancel");
                            Log.Write("Sent 'cancel' message to OneDriver.");
                            Log.WriteTempLog("Sent cancel signal to driver process.");
                            Dispatcher.Invoke(UpdateLogText);
                        }
                    }
                    else
                    {
                        Log.Write("Pipe to OneDriver was not connected.");
                        Log.WriteTempLog("No pipe connection to driver process.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Error while sending cancel to OneDriver: " + ex.Message);
                    Log.WriteTempLog($"Error sending cancel to driver: {ex.Message}");
                    Dispatcher.Invoke(UpdateLogText);
                }

                try
                {
                    if (_metricsProcess != null && !_metricsProcess.HasExited)
                    {
                        _metricsProcess.Kill();
                        _metricsProcess.WaitForExit();
                        Log.Write("Metrics process killed.");
                        Log.WriteTempLog("Metrics process terminated.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Failed to kill metrics process: " + ex.Message);
                    Log.WriteTempLog($"Failed to terminate metrics process: {ex.Message}");
                    Dispatcher.Invoke(UpdateLogText);
                }

                try
                {
                    if (_driverProcess != null && !_driverProcess.HasExited)
                    {
                        _driverProcess.Kill();
                        _driverProcess.WaitForExit();
                        Log.Write("Driver process killed.");
                        Log.WriteTempLog("Driver process terminated.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Failed to kill driver process: " + ex.Message);
                    Log.WriteTempLog($"Failed to terminate driver process: {ex.Message}");
                    Dispatcher.Invoke(UpdateLogText);
                }

                try
                {
                    if (_pipeServer != null)
                    {
                        if (_pipeServer.IsConnected)
                            _pipeServer.Disconnect();
                        _pipeServer.Dispose();
                        Log.WriteTempLog("Pipe server disposed.");
                        Dispatcher.Invoke(UpdateLogText);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Error disposing pipe server: " + ex.Message);
                    Log.WriteTempLog($"Error disposing pipe server: {ex.Message}");
                    Dispatcher.Invoke(UpdateLogText);
                }
                _pipeServer = null;

                RunButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusText.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        private void LogsButton_Click(object sender, RoutedEventArgs e)
        {
            LogsWindow logsWindow = new LogsWindow();
            logsWindow.Show();
        }

        public static class Log
        {
            private static string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "application.log");
            private static string tempLogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "templogs.txt");

            public static void ResetTempLogs()
            {
                try
                {
                    string logsDir = Path.GetDirectoryName(tempLogFilePath);
                    if (!Directory.Exists(logsDir))
                    {
                        Directory.CreateDirectory(logsDir);
                        Console.WriteLine($"Created Logs directory: {logsDir}");
                    }
                    File.WriteAllText(tempLogFilePath, string.Empty);
                    Console.WriteLine($"Reset templogs.txt at: {tempLogFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception in Log.ResetTempLogs: {ex.Message}");
                    throw;
                }
            }

            public static void Write(string message)
            {
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}";
                try
                {
                    string logsDir = Path.GetDirectoryName(logFilePath);
                    if (!Directory.Exists(logsDir))
                    {
                        Directory.CreateDirectory(logsDir);
                        Console.WriteLine($"Created Logs directory: {logsDir}");
                    }
                    using (FileStream fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (StreamWriter writer = new StreamWriter(fileStream))
                    {
                        writer.WriteLine(logEntry);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception in Log.Write: {ex.Message}");
                }
            }

            public static void WriteTempLog(string message)
            {
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}";
                try
                {
                    string logsDir = Path.GetDirectoryName(tempLogFilePath);
                    if (!Directory.Exists(logsDir))
                    {
                        Directory.CreateDirectory(logsDir);
                        Console.WriteLine($"Created Logs directory: {logsDir}");
                    }
                    using (FileStream fileStream = new FileStream(tempLogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (StreamWriter writer = new StreamWriter(fileStream))
                    {
                        writer.WriteLine(logEntry);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception in Log.WriteTempLog: {ex.Message}");
                }
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
    }

    public class Account
    {
        public string Username { get; set; }
        public string Password { get; set; }

        public Account(string username, string password)
        {
            Username = username;
            Password = password;
        }
    }

    public class DataPoint
    {
        public int TotalJobs { get; set; }
        public double CurrentStorage { get; set; }
        public double MaxMemory { get; set; }
        public string Hostname { get; set; }
        public string VPSID { get; set; }
        public double MaxStorage { get; set; }
        public DateTime Timestamp { get; set; }
        public double JobsPerMinOut { get; set; }
        public double JobsPerMinIn { get; set; }
        public string Status { get; set; }
        public double CurrentMemory { get; set; }
    }
}
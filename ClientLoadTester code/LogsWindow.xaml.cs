using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace ClientLoadTester
{
    public partial class LogsWindow : Window
    {
        public ObservableCollection<string> LogEntries { get; set; }
        public string LogFilePath { get; set; }
        private string logDirectory = "Logs";
        private string logFileName = "application.log";

        public LogsWindow()
        {
            InitializeComponent();
            LogEntries = new ObservableCollection<string>();
            SetupLogFile();
            DataContext = this;
            LoadLogs();
        }

        private void SetupLogFile()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string fullLogDirectory = Path.Combine(baseDirectory, logDirectory);
            Directory.CreateDirectory(fullLogDirectory);
            LogFilePath = Path.Combine(fullLogDirectory, logFileName);
        }

        private void LoadLogs()
        {
            LogEntries.Clear();
            if (File.Exists(LogFilePath))
            {
                try
                {
                    using (FileStream fileStream = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader reader = new StreamReader(fileStream))
                    {
                        string logContents = reader.ReadLine();
                        while(logContents != null)
                        {   
                            LogEntries.Add(logContents);
                            logContents = reader.ReadLine();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception: {ex.Message}");
                }
     //           string[] lines = File.ReadAllLines(LogFilePath);
       //         foreach (string line in lines)
         //       {
           //         LogEntries.Add(line);
             //   }
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadLogs();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(LogFilePath))
            {
                File.WriteAllText(LogFilePath, string.Empty);
                LogEntries.Clear();
            }
        }
    }
}
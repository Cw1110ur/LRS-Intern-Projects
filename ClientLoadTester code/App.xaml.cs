using System;
using System.Windows;

namespace ClientLoadTester
{

    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Defaults
            string hostname = "Default";
            string soapToken = "Default";
            string vpsid = "Default";
            string sessionId = "Default";
            string queues = "Default";


            // Parse arguments
            for (int i = 0; i < e.Args.Length; i++)
            {
                switch (e.Args[i])
                {
                    case "-hostname":
                        if (i + 1 < e.Args.Length)
                            hostname = e.Args[++i];
                        break;
                    case "-soapToken":
                        if (i + 1 < e.Args.Length)
                            soapToken = e.Args[++i];
                        break;
                    case "-vpsid":
                        if (i + 1 < e.Args.Length)
                            vpsid = e.Args[++i];
                        break;
                    case "-sessionId":
                        if (i + 1 < e.Args.Length)
                            sessionId = e.Args[++i];
                        break;
                    case "-queues":
                        if (i + 1 < e.Args.Length)
                            queues = e.Args[++i];
                        break;
                }
            }
            Console.WriteLine($"Parsed hostname: {hostname}");
            Console.WriteLine($"Parsed soapToken: {soapToken}");
            Console.WriteLine($"Parsed vpsid: {vpsid}");
            Console.WriteLine($"Parsed sessionId: {sessionId}");
            Console.WriteLine($"Parsed queues: {queues}");

            if (hostname == "Default" || soapToken == "Default" || vpsid == "Default" || sessionId == "Default" || queues == "Default")
            {
                MessageBox.Show("Missing required arguments.");
                Current.Shutdown();
                return;
            }

            // Pass arguments into MainWindow constructor
            var mainWindow = new MainWindow(hostname, soapToken, vpsid, sessionId, queues);
            mainWindow.ShowDialog();
        }
    }
}
using System;
using System.Configuration;
using System.Data;
using System.Windows;

namespace VPSXPrinterAdder
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
		protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string hostname = "", soapToken = "", sessionId = "", vpsid = "";

            for (int i = 0; i < e.Args.Length; i++)
            {
                switch (e.Args[i])
                {
                    case "--hostname":
                        if (i + 1 < e.Args.Length)
                            hostname = e.Args[++i];
                        break;
                    case "--soapToken":
                        if (i + 1 < e.Args.Length)
                            soapToken = e.Args[++i];
                        break;
                    case "--sessionId":
                        if (i + 1 < e.Args.Length)
                            sessionId = e.Args[++i];
                        break;

                    case "--vpsid":
                        if (i + 1 < e.Args.Length)
                            vpsid = e.Args[++i];
                        break;
                }
            }

            MainWindow mainWindow;

            if (!string.IsNullOrWhiteSpace(hostname) && !string.IsNullOrWhiteSpace(soapToken) && !string.IsNullOrWhiteSpace(sessionId))
                mainWindow = new MainWindow(hostname, soapToken, sessionId, vpsid);
            else
                mainWindow = new MainWindow(null, null, null, null);

            mainWindow.ShowDialog();
        }
    
    }

}

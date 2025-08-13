/*
====================================================================================================================================================================================================================
Creator(s): Colin Willour 
Version Author: Colin Willour

Explanation:

==================================================================================================================================================================================================================
*/

using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using System.Windows.Threading;

namespace VPSXPrinterAdder
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            OutputBox.Text = "";

            string hostname = HostnameBox.Text.Trim();
            string soapToken = SoapTokenBox.Text.Trim();
            string vpsid = VpsidBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(soapToken))
            {
                OutputBox.Text = "Hostname and SOAP Token are required.";
                return;
            }

            try
            {
                string sessionId = await GetSessionId(soapToken, hostname);
                OutputBox.Text = $"✅ Session ID:\n{sessionId}";

                RunPrtrDef(hostname, soapToken, sessionId, vpsid);

            }
            catch (Exception ex)
            {
                OutputBox.Text = $"❌ Error:\n{ex.Message}";
            }



            // Close the window after 10 seconds
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(10);
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                this.Close();
            };
            timer.Start();
        }

        private void RunPrtrDef (string hostname, string soapToken, string sessionID, string vpsid)
        {
            string args = $"--hostname \"{hostname}\" --soapToken \"{soapToken}\" --sessionId \"{sessionID}\" --vpsid \"{vpsid}";

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "PD\\PrtrDef.exe",
                Arguments = args,
                UseShellExecute = true
            });
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
				  <Server xsi:type='xsd:string'>VSV1</Server>
				  <soapToken xsi:type='xsd:string'>{soapToken}</soapToken>
				</lrs:Gateway_Logon>
			  </soapenv:Body>
			</soapenv:Envelope>";

            using var client = new HttpClient();
            var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
            var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"HTTP Error: {response.StatusCode}");
            }

            string responseBody = await response.Content.ReadAsStringAsync();

            XmlDocument xml = new XmlDocument();
            xml.LoadXml(responseBody);
            XmlNode? sessNode = xml.SelectSingleNode("//SessID");

            if (sessNode == null || string.IsNullOrWhiteSpace(sessNode.InnerText))
                throw new Exception("Session ID not found in SOAP response.");

            return sessNode.InnerText;
        }
    }
}
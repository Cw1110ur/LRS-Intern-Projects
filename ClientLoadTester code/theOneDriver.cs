/*
 ====================================================================================================================================================================================================================
Creator: Sam Scoles

Explanation:
Imports: The code imports several libraries to handle various functionalities such as JSON parsing, HTTP requests, and XML processing.

Namespace and Class: The code is encapsulated within the LRS.TestTools.LoadSim namespace, and the main class LoadSim inherits from ConsoleApplication.

Main Method: The Main method serves as the entry point for the application. It calls the asynchronous RunAsync method to execute the load simulation.

RunAsync Method: This asynchronous method handles the main logic of the load simulation. It validates input arguments, reads configuration settings, obtains a session ID, runs scripts, and processes jobs.

SOAP Request: The GetSessionId method sends a SOAP request to obtain a session ID. It constructs the SOAP envelope, sends the request, and parses the response to retrieve the session ID.

PowerShell Script Execution: The RunPowerShellScript method executes PowerShell scripts and captures their output. It starts a process, redirects output and error streams, and returns the relevant output.

Job Processing Loop: The code iterates through the specified number of jobs, submitting them in batches and invoking the metrics script at 75% completion.

Error Handling: The code includes error handling mechanisms to capture and log exceptions, providing informative messages when issues occur.
 ===================================================================================================================================================================================================================
 */





using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json.Linq;

//define a namespace for the load sim tools
namespace LRS.TestTools.LoadSim
{
    //the LoadSim class extends ConsoleApplication and handles the main execution of the load test
    public class LoadSim : ConsoleApplication
    {
        //main entry point for the application
        static int Main(string[] args)
        {
            //execute the asynchronous RunAsync method and wait for it to complete
            return new LoadSim().ExecuteAsync(args).GetAwaiter().GetResult();
        }

        //asynchronous method to run the load sim
        public override async Task<int> RunAsync(Dictionary<string, string> args)
        {

            if (File.Exists(@"data.csv"))
            {
                File.Delete(@"data.csv");
            }

            //flag to ensure metrics script is called only once
            bool metricsCalled = false;

            //validate and retrieve required args from the dictionary
            if (!args.TryGetValue("soapToken", out var soapToken) ||
                !args.TryGetValue("totalJobs", out var totalJobsStr) ||
                !args.TryGetValue("stepValue", out var stepValueStr))
            {
                //print an error msg if any args are missing
                Console.WriteLine("Missing required arguments.");
                return 1;
            }

            //parse numeric args and validate them
            if (!int.TryParse(totalJobsStr, out var totalJobs) ||
                !int.TryParse(stepValueStr, out var stepValue))
            {
                //print an error msg if the args are not valid #s
                Console.WriteLine("Invalid numeric arguments.");
                return 1;
            }

            //load config from .json
            JObject config = JObject.Parse(File.ReadAllText(@"config.json"));
            
            //retrieve the log path from the config, or throw an exception
            string logpath = config["logPath"]?.ToString() ?? throw new Exception("Log Path configuration not found.");
            Console.WriteLine($"{logpath}");
            
            //get sessID using the SOAP token
            string sessionId = await GetSessionId(soapToken);
            if (sessionId == null)
            {
                //print an error msg if the sessID is not obtained
                Console.WriteLine("Failed to obtain session ID.");
                return 1;
            }

            //define paths for external scripts and executables
            const string GathererScriptPath = @"gatherer.exe";
            const string MetricsMonitorScriptPath = @"take5.exe";
            const string RandomSelectionScriptPath = @"updatedRandom.exe";

            //run gatherer script to prepare the queue info
            //RunPowerShellScript(GathererScriptPath, $"-configFilePath \"config.json\" -outputFilePath \"queues.csv\" -sessionId {sessionId}");

            //wait for the queues.csv file to be created
            string queuesFile = "queues.csv";
            while (!File.Exists(queuesFile))
            {
                Console.WriteLine("Waiting for queues.csv to be created...");
                await Task.Delay(1000);
            }

            //read queue information from the .csv
            List<string> queues = new();
            using (var reader = new StreamReader(queuesFile))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    //skip the header line and empty lines
                    if (line == "QueueName" || line == null) continue;
                    queues.Add(line);
                }
            }

            //retrieve account info from the config
            JObject account = config["Account"]?.ToObject<JObject>() ?? throw new Exception("Account configuration not found or incomplete");
            string username = account["Username"]?.ToString() ?? throw new Exception("Account Username not found");
            string password = account["Password"]?.ToString() ?? throw new Exception("Account Password not found");

            //construct the gateway URL using the hostname from the config
            string gatewayUrl = $"https://{config["hostname"]}/lrs.gateway";

            //define a path for the PPMsim exe
            string PPMSimPath = "PPMJobSimulator\\lrsGatewayTester.exe";
            
            //output the step value and total jobs for debugging
            Console.WriteLine($"STEP: {stepValue}");
            Console.WriteLine($"TOTAL JOBS: {totalJobs}");

            //loop through the number of jobs in increments of stepValue
            for (int jobs = stepValue; jobs <= totalJobs; jobs += stepValue)
            {
                //calculate the number of jobs per submission
                int jobsPerSubmission = Math.Max((int)Math.Ceiling(((double)jobs / (double)queues.Count)), 1);
                //output the number of jobs for debugging
                Console.WriteLine($"JOBS: {jobs}");
                
                //loop through each job submission
                for (int i = jobsPerSubmission; i <= jobs; i += jobsPerSubmission)
                {
                   

                    //output the current job count for debugging
                    Console.WriteLine($"I: {i}");

                    try
                    {
                        //output msg indicating the start of random
                        Console.WriteLine("Starting Random Selection...");
                        //run the random selection script
                        List<string> selectedItems = RunPowerShellScript(RandomSelectionScriptPath, $"-queues {string.Join(',', queues)}");
                        
                        string queue = selectedItems[0];
                        string job = selectedItems[1];

                        //output selected details for debugging
                        string output = $"Selected Account: {username}\nSelected Password: {password}\nSelected Print Job: {job}\nSelected Queue: {queue}\nNumber of times: {jobsPerSubmission}\n";
                        Console.WriteLine(output);
                        Console.WriteLine($"Sending print job {job} for account {username} using queue {queue}");

                        //config process start info for running the PPMsim
                        ProcessStartInfo startInfo = new()
                        {
                            //path to the exe, args
                            FileName = PPMSimPath,
                            Arguments = $"-g \"{gatewayUrl}\" -u \"{username}\" -p {password} -q {queue} -j \"{job}\" -t {jobsPerSubmission}",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                        };

                        //create and config the process
                        Process process = new() { StartInfo = startInfo };
                        process.ErrorDataReceived += (sender, args) => Console.WriteLine("ERROR: " + args.Data);
                        process.Start();

                        //write process output to the log
                        using (StreamWriter writer = new(logpath, true))
                        {
                            writer.Write(process.StandardOutput.ReadToEnd());
                        }

                        //begin reading error data from process
                        process.BeginErrorReadLine();
                        //wait for process to exit
                        process.WaitForExit();

                        

                        //await 1.5 seconds before next iteration
                        await Task.Delay(1500);
                    }
                    catch (Exception ex)
                    {
                        //output any exception encountered during the loop
                        Console.WriteLine($"Error in the main loop: {ex.Message}");
                    }

                    //call the metrivs script when the progress reaches 75% && metrics hasn't been called yet
                    if (i >= (double)jobs * 0.75 && !metricsCalled)
                    {
                        //set flag to true
                        metricsCalled = true;
                        Console.WriteLine($"Calling Metrics Script at 75% completion for job count: {i}");

                        //run monitor script
                        RunPowerShellScript(MetricsMonitorScriptPath, $"-sessID {sessionId} -jobsAmount {jobs}");
                    }
                }

                metricsCalled = false;

                //output the completion of the iteration for debugging
                Console.WriteLine($"Completed iteration {jobs / stepValue}");
            }

            return 0;
        }

        //method to obtain sessID using SOAP
        private static async Task<string> GetSessionId(string soapToken)
        {
            //define the gateway URL for SOAP
            const string gatewayUrl = "https://hw09971.lrsinc.org/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX";

            //construct the SOAP envelope with the provided SOAP token
            var soapEnvelope = $@"
            <soapenv:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xsd='http://www.w3.org/2001/XMLSchema' xmlns:soapenv='http://schemas.xmlsoap.org/soap/envelope/' xmlns:lrs='http://www.lrs.com'>
                <soapenv:Header/>
                <soapenv:Body>
                    <lrs:Gateway_Logon soapenv:encodingStyle='http://schemas.xmlsoap.org/soap/encoding/'>
                        <soapToken xsi:type='xsd:string'>{soapToken}</soapToken>
                    </lrs:Gateway_Logon>
                </soapenv:Body>
            </soapenv:Envelope>";

            //create an HttpClient for sending requests
            using var client = new HttpClient();
            //create HTTP content for SOAP request
            var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");

            try
            {
                //send SOAP and await response
                var response = await client.PostAsync(gatewayUrl, content);
                //read the response content as a string
                var responseString = await response.Content.ReadAsStringAsync();

                //output the SOAP response
                Console.WriteLine("SOAP Response:");
                Console.WriteLine(responseString);

                //check if the response indicates success
                if (response.IsSuccessStatusCode)
                {
                    //create an XmlDocument to parse the response
                    var xml = new XmlDocument();
                    //load the response into the XmlDocument
                    xml.LoadXml(responseString);
                    //create a namespace manager for XPath queries
                    var nsmgr = new XmlNamespaceManager(xml.NameTable);
                    nsmgr.AddNamespace("soapenv", "http://schemas.xmlsoap.org/soap/envelope/");
                    nsmgr.AddNamespace("m", "http://www.lrs.com");

                    //output parsed XML
                    Console.WriteLine("Parsed XML:");
                    Console.WriteLine(xml.OuterXml);

                    //select the sessID node from the XML response
                    var sessionIdNode = xml.SelectSingleNode("//m:Gateway_LogonResponse/SessID", nsmgr);

                    //check if the sessID node is found
                    if (sessionIdNode != null)
                    {
                        //output the sessID
                        Console.WriteLine($"Session ID Node Value: {sessionIdNode.InnerText}");
                        //return the sessID
                        return sessionIdNode.InnerText;
                    }
                    else
                    {
                        //output a message if the sessID node is not found
                        Console.WriteLine("Session ID Node not found.");
                        var faultStringNode = xml.SelectSingleNode("//soapenv:Fault/soapenv:faultstring", nsmgr);
                        if (faultStringNode != null)
                        {
                            //output any fault string found in the response
                            Console.WriteLine($"SOAP Fault: {faultStringNode.InnerText}");
                        }
                        else
                        {
                            Console.WriteLine("Failed to obtain session ID and no fault string found.");
                        }
                    }
                }
                else
                {
                    //output the http error code if the response is not successful
                    Console.WriteLine($"HTTP Error: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                //output any exception encountered while sending the SOAP request
                Console.WriteLine($"Error sending SOAP request: {ex.Message}");
            }

            //return null if sessID is not obtained
            return null;
        }

        //methid to run a powerShell script and return its output
        private static List<string> RunPowerShellScript(string scriptPath, string arguments)
        {
            //configure process start info for running the powerShell script
            var psi = new ProcessStartInfo
            {
                //path to script file, cmd line args
                FileName = scriptPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            //create and config the process
            var process = new Process
            {
                StartInfo = psi
            };

            //list to store the wanted output from the script
            List<string> wantedOutputs = new();

            //event handler for processing output data from the script
            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    //get the output data
                    string output = args.Data;

                    //check if the output starts with specific strings and add to the list
                    if (output.StartsWith("Selected Queue: "))
                    {
                        //add selected queue to the list
                        wantedOutputs.Add(output[16..]);
                    }
                    else if (output.StartsWith("Selected Job: "))
                    {
                        //add selected job to the list
                        wantedOutputs.Add(output[14..]);
                    }
                    //output the data for debug
                    Console.WriteLine(args.Data);
                }
            };
            //event handler for processing error data from the script
            process.ErrorDataReceived += (sender, args) => Console.WriteLine("ERROR: " + args.Data);

            //start the process
            process.Start();
            //begin reading the output
            process.BeginOutputReadLine();
            //begin reading error data
            process.BeginErrorReadLine();
            //wait for the process to exit
            process.WaitForExit();

            //return the list of wanted outputs
            return wantedOutputs;
        }
    }
}
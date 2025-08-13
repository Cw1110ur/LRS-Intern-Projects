#Creators: Sidharth Bijo, Lucas Harry

#Explanation:
#Configuration File: The script begins by loading a JSON configuration file that contains server information, including log file paths and server-specific details like hostname, VPSID, and SOAP tokens.
#Logging: The script sets up logging by starting a transcript, capturing all console output to a log file specified in the configuration.
#Error Handling: It uses try-catch-finally blocks to manage errors, setting ErrorActionPreference to "Stop" to ensure that exceptions halt execution unless caught.
#GatewayLogon Function: This function sends a SOAP request to log in to the gateway and retrieves a session ID. It handles errors by checking for SOAP faults and exits if an error is encountered.
#Get-SystemStats Function: This function sends a SOAP request to obtain system statistics for a specified VPSID. It captures and returns various metrics, such as total jobs, memory usage, and storage usage.
#Invoke-With-Retry Function: A utility function to retry a script block execution a specified number of times with a delay between attempts. It ensures robust execution by retrying on failures.
#Data Collection Loop: The script iterates over each server defined in the configuration, logging into the gateway and capturing performance data in a loop. It takes multiple samples, adding timestamps and system stats to an array for later export.
#CSV Export: After collecting all samples, the script exports the data to a CSV file, providing a comprehensive view of system performance metrics.
#Error Reporting and Cleanup: The script outputs detailed error messages and saves the SOAP response to an XML file if any SOAP requests fail. It concludes by stopping the transcript and prompting the user to press Enter before exiting.


# Read the JSON file
#$configPath = "config.json"
#$config = Get-Content -Path $configPath | ConvertFrom-Json
#Write-Output $config

param (
    [string]$Hostname,
    [string]$SoapToken,
    [string]$VpsId,
    [string]$SessionId,
    [string]$pipeName  # <-- make sure this is also passed by C#
)

<#
# Wait for signal from C# via named pipe
$pipe = new-object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::In)
$pipe.Connect()
$reader = new-object System.IO.StreamReader($pipe)
$signal = $reader.ReadLine()
if ($signal -eq "RUN") {
    Write-Host "Received RUN signal from parent EXE"
    # Now proceed to gather stats, etc.
} #>

# trouble shooting (currently blank)
$logPath = "C:\Users\E7624\OneDrive - Levi, Ray & Shoup, Inc\Training\LoadGenTool\Code\theOneDriver code\testlrsmetrics.txt"
 
# Log the incoming parameters
Add-Content -Path $logPath -Value "`n=== Metrics.ps1 started at $(Get-Date) ==="
Add-Content -Path $logPath -Value "Hostname: $Hostname"
Add-Content -Path $logPath -Value "SoapToken: $SoapToken"
Add-Content -Path $logPath -Value "VPS ID: $VpsId"
Add-Content -Path $logPath -Value "Session ID: $SessionId"
Add-Content -Path $logPath -Value "Pipe Name: $pipeName"

try {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::In)
    $pipe.Connect(10000)  # wait up to 10 seconds
    $reader = New-Object System.IO.StreamReader($pipe)
    $signal = $reader.ReadLine()

    if ($signal -eq "RUN") {
        Add-Content -Path $logPath -Value "✅ Received RUN signal from C#"
    } else {
        Add-Content -Path $logPath -Value "⚠️ Unexpected signal: $signal"
        Exit 1
    }
}
catch {
    Add-Content -Path $logPath -Value "❌ Failed to connect to pipe: $_"
    Exit 2
}
finally {
    if ($pipe) {
    $pipe.Dispose()
    }
}

Add-Content -Path $logPath -Value "All initial values received successfully. Script will now exit (test complete)."
Exit 0

# --- END: Argument and Pipe Testing Stub ---

###
 
# Define the number of samples and the interval between samples
$samples = 3
$interval = 1
$outputFile = "data.csv"
 
#Defining logFilePath
#$logFilePath = $config.logPath
 
# Start logging
#Write-Output "Starting process"
#"$(Get-Date): Metrics process started" | Out-File -FilePath "$logFilePath" -Append
 
 
 
 
<#
 
# Error catching variable
$ErrorActionPreference = "Stop"
 
#Error catching method
try {
 
# Initialize an array to store the data samples
$data = @()
 
# Function to log on to the gateway and get session ID
function GatewayLogon {
    param (
        [string]$hostname,
        [string]$soapToken
    )
   
    $url = "https://$hostname/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX"
    $soap = @"
<soapenv:Envelope xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:lrs="http://www.lrs.com">
    <soapenv:Header/>
    <soapenv:Body>
        <lrs:Gateway_Logon soapenv:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
            <soapToken xsi:type="xsd:string">$soapToken</soapToken>
        </lrs:Gateway_Logon>
    </soapenv:Body>
</soapenv:Envelope>
"@
   
    # Send SOAP request and capture response
    try {
        [xml]$return = Invoke-WebRequest -Uri $url -Method Post -ContentType "text/xml" -Body $soap
    } catch {
        Write-Host "Error sending SOAP request: $_" -ForegroundColor Red
        exit 1
    }
   
    # Checking for errors in the response
    if ($return.Envelope.Body.Fault) {
        Write-Host "`n*****!!"$return.Envelope.Body.Fault.faultstring"!!*****" -ForegroundColor Red
        "`n*****!!" + $return.Envelope.Body.Fault.faultstring + "!!*****"
        exit 2
    } else {
        return $return.Envelope.Body.Gateway_LogonResponse.SessID.InnerText
    }
}
#>
 
# Function to send SOAP request and get system stats
function Get-ServerStats {
    param (
        [string]$hostname,
        [string]$sessID,
        [string]$VPSID
    )
   
    $url = "https://$hostname/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX"
    $soapTemplate = @"
<SOAP-ENV:Envelope xmlns:SOAP-ENV='http://schemas.xmlsoap.org/soap/envelope/' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xsd='http://www.w3.org/2001/XMLSchema'>
   <SOAP-ENV:Body>
      <m:VPSX_SystemStats xmlns:m='http://www.lrs.com'>
         <SessID xsi:type="xsd:string">$sessID</SessID>
         <VPSID xsi:type="xsd:string">$VPSID</VPSID>                                                    
      </m:VPSX_SystemStats>
   </SOAP-ENV:Body>
</SOAP-ENV:Envelope>
"@
   
    # Send SOAP request and capture response
    try {
        [xml]$return = Invoke-WebRequest -Uri $url -Method Post -ContentType "text/xml" -Body $soapTemplate
    } catch {
        Write-Host "Error sending SOAP request: $_" -ForegroundColor Red
        exit 1
    }
 
    # Checking for errors in the response
    if ($return.Envelope.Body.Fault) {
        Write-Host "Error: $($return.Envelope.Body.Fault.faultstring)" -ForegroundColor Red
        Write-Output $return.OuterXml | Out-File -FilePath "error_response.xml" -Append
        return $null
    }
 
    # Extracting response details
    if ($return.Envelope.Body.VPSX_SystemStatsResponse) {
        return @{
            "Status" = $return.Envelope.Body.VPSX_SystemStatsResponse.Status.InnerText
            "TotalJobs" = $return.Envelope.Body.VPSX_SystemStatsResponse.TotalJobs.InnerText
            "JobPerMinIn" = $return.Envelope.Body.VPSX_SystemStatsResponse.JobPerMinIn.InnerText
            "JobPerMinOut" = $return.Envelope.Body.VPSX_SystemStatsResponse.JobPerMinOut.InnerText
            "CurrentMemory" = $return.Envelope.Body.VPSX_SystemStatsResponse.CurrentMemory.InnerText
            "MaxMemory" = $return.Envelope.Body.VPSX_SystemStatsResponse.MaxMemory.InnerText
            "CurrentStorage" = $return.Envelope.Body.VPSX_SystemStatsResponse.CurrentStorage.InnerText
            "MaxStorage" = $return.Envelope.Body.VPSX_SystemStatsResponse.MaxStorage.InnerText
 
            #added calls
            "TotalPages" = $return.Envelope.Body.VPSX_SystemStatsResponse.TotalPages.InnerText  #1554
            "InitTime"= $return.Envelope.Body.VPSX_SystemStatsResponse.InitTime.InnerText #start time?
            "TotalBytes" = $return.Envelope.Body.VPSX_SystemStatsResponse.TotalBytes.InnerText #jobsize
 
        }
    } else {
        Write-Host "Error: VPSX_SystemStatsResponse not found in the response." -ForegroundColor Red
        Write-Output $return.OuterXml | Out-File -FilePath "error_response.xml" -Append
        return $null
    }
}
 
# Function to ssh and get system stats
function Get-SystemStats {
    param (
        [int]$timeoutInSeconds,
        [string]$arguments
    )
 
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "ssh"
    $startInfo.Arguments = $arguments
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
 
    # Create the process
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
 
    Write-Host "Running the command: $($startInfo.FileName) $($startInfo.Arguments)"
 
    # Start the process
    $process.Start() | Out-Null
 
    # Create a StreamReader for the output
    $outputReader = $process.StandardOutput
    $errorReader = $process.StandardError
 
 
 
 
    # Create a timeout timer
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
 
    # Initialize a hash table to store the results
    $outputData = @{}
 
    try {
        # Read the output line by line until the process exits or times out
        while (!$process.HasExited) {
            if ($timer.Elapsed.TotalSeconds -ge $TimeoutInSeconds) {
                # Timeout reached, kill the process
                $process.Kill()
                Write-Host "SSH command timed out and was terminated."
                return $null
            }
        }
 
        $line = $outputReader.ReadLine()
        # Capture any remaining output after the process has exited
        while ($null -ne $line) {
            $line = $line.Trim()
            # Split the line into field and value
            if ($line -match "\\(?<fieldName>.*) :") {
                $fieldName = $matches['fieldName']
                $line = $outputReader.ReadLine()
                if($null -ne $line){
                    $line = $line.Trim()
                    # Add to the hash table
                    $outputData.Add($fieldName, $line)
                }
            }
            $line = $outputReader.ReadLine()
        }
 
        $errorOutput = $errorReader.ReadToEnd()
         # Capture any remaining output after the process has exited
         if($errorOutput.Length -ne 0){
            Write-Host "SSH Error: $errorOutput"
         }
       
        return $outputData
    }
    finally {
        # Clean up
        $outputReader.Close()
        $process.Close()
        $timer.Stop()
    }
}
 
# Function to handle retries
function Invoke-With-Retry {
    param (
        [scriptblock]$ScriptBlock,
        [int]$Retries = 3,
        [int]$Delay = 2
    )
   
    for ($i = 0; $i -lt $Retries; $i++) {
        $result = & $ScriptBlock
        if ($result) {
            return $result
        } else {
            Start-Sleep -Seconds $Delay
        }
    }
   
    Write-Host "Failed after $Retries attempts." -ForegroundColor Red
    exit 1
}
 
# Check if the output file exists and remove it if it does
# if (Test-Path -Path $outputFile) {
#     Remove-Item -Path $outputFile
#     Write-Output "Existing CSV file removed: $outputFile"
# }
 
# Loop through each server configuration
foreach ($server in $config.servers) {
    $hostname = $server.hostname
    $VPSID = $server.VPSID
    $soapToken = $server.soapToken
 
 
   
    Write-Output "Processing VPSID: $VPSID on hostname: $hostname"
   
    # Logon to the gateway and get session ID
    $sessID = GatewayLogon -hostname $hostname -soapToken $soapToken
 
    # Capture performance data
    for ($i = 0; $i -lt $samples; $i++) {
        Write-Output "Capturing sample $($i + 1) of $samples..."
        $timestamp = Get-Date
       
        # Get system stats with retries
        $serverStats = Invoke-With-Retry -ScriptBlock { Get-ServerStats -hostname $hostname -sessID $sessID -VPSID $VPSID }
        if (-not $serverStats) {
            Write-Host "Skipping this sample due to errors."
            continue
        }
       
        # Add timestamp to system stats
        $sample = @{
            "Timestamp" = $timestamp
            "Hostname" = $hostname
            "VPSID" = $VPSID
            "Status" = $serverStats["Status"]
            "TotalJobs" = $serverStats["TotalJobs"]
            "JobPerMinIn" = $serverStats["JobPerMinIn"]
            "JobPerMinOut" = $serverStats["JobPerMinOut"]
            "CurrentMemory" = $serverStats["CurrentMemory"]
            "MaxMemory" = $serverStats["MaxMemory"]
            "CurrentStorage" = $serverStats["CurrentStorage"]
            "MaxStorage" = $serverStats["MaxStorage"]
 
 
            "TotalPages" = $serverStats["TotalPages"]
            "InitTime" = $serverStats["InitTime"]
            "TotalBytes" = $serverStats["TotalBytes"]
        }
###
<#
        $counter = @{}  #initiallizes 2nd hash table
        $progress = 0
 
        #going to be using $samples = 3 limit
        while ($progress -lt $samples) {   #progress < 3
 
            foreach ($item in $sample) {
 
               
 
                if ($counter.ContainsKey($item)) {
                    $counter[$item]++  
                } else {
                    $counter[$item] = 1  
                }
            }
 
            $progress++
            Write-Host "Loading...$progress of $loadLimit"
             $progressBar.Dispatcher.Invoke([Action]{
 
            $progressBar.Value = $progress
        [System.Windows.Media.Animation.Storyboard]::SetTargetName($progressAnimation, "progressBar")
    $progressAnimation.Begin()
         })
    Start-Sleep -Milliseconds 500
           
        }
Write-Host "complete!" #>
####
 
##2nd loader attempt
 
$samples = 3
$progress = 0
 
while ($progress -lt $samples) {
    # Simulated work
    Start-Sleep -Milliseconds 500
    $progress++
    Write-Host "Loading... $progress of $samples"
}
 
Write-Host "complete!"
 
##2nd loader attempt
 
 
 
 
        if($server.ssh.serverDataSetup){
            $sshUserHost = $server.ssh.sshUserHost
            # $systemStats =
            $systemStats = Get-SystemStats -timeoutInSeconds 10 -arguments "$sshUserHost powershell -Command ""Get-Counter -Counter '\Processor(_Total)\% Processor Time', '\Network Interface(*)\Bytes Received/sec', '\Network Interface(*)\Bytes Sent/sec', '\Memory\Committed Bytes'"""
 
            if($null -ne $systemStats){
                foreach($key in $systemStats.keys){
                    Write-Output "Adding the value: $($systemStats[$key]) with the key: $key"
                    $sample.Add($key, $systemStats[$key])                    
                }
            }
        }
       
        # Store the sample
        $data += [PSCustomObject]$sample
        Write-Output "Sample $($i + 1) captured. Sleeping for $interval seconds..."
        Start-Sleep -Seconds $interval
    }
}
# Stop Logging
# Stop-Transcript
 
# Convert the data to a .csv file
$data | Export-Csv -Append -Path $outputFile -NoTypeInformation
Write-Output "Performance metrics have been exported to $outputFile."

#Catch method to look for errors
catch {
    Write-Output "An error occured: $_"
    Write-Output "Error"
"$(Get-Date): Metrics process failed" | Out-File -FilePath "$logFilePath" -Append
}
 
 
finally {
    Write-Output "Metric process end"
    "$(Get-Date): Metrics process complete." | Out-File -FilePath "$logFilePath" -Append
    }
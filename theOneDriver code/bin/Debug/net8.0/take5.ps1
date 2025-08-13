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




#define the path to the JSON configuration file and read its content
$configPath = "configNew.json"
$config = Get-Content -Path $configPath | ConvertFrom-Json

#define the number of samples to collect and the interval between each sample
$samples = 5
$interval = 1
$outputFile = "data.csv" #define the path for the output CSV file

#loop through each log file path specified in the configuration
foreach ($logFile in $config.logFile) {
    $logFilePath = $logFile.logFilePath
    Write-Output "$logFilePath"

    #start logging the script's execution to a transcript file
    Start-Transcript -Path $logFilePath

    #error catching variable to stop execution on errors
    $ErrorActionPreference = "Stop"

    #error handling using try-catch block
    try {

        #initialize an array to store the data samples
        $data = @()

        #function to log on to the gateway and get session ID
        function GatewayLogon {
            param (
                [string]$hostname, #the hostname of the gateway
                [string]$soapToken #the SOAP token for authentication
            )

            #construct the URL for the SOAP request
            $url = "https://$hostname/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX"
            
            #define the SOAP envelope for logging in to the gateway
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
            
            #send the SOAP request and capture the response
            try {
                [xml]$return = Invoke-WebRequest -Uri $url -Method Post -ContentType "text/xml" -Body $soap
            } catch {
                #output an error message if the SOAP request fails
                Write-Host "Error sending SOAP request: $_" -ForegroundColor Red
                exit 1
            }
            
            #check for errors in the response
            if ($return.Envelope.Body.Fault) {
                #output the fault string if an error is returned
                Write-Host "`n*****!!"$return.Envelope.Body.Fault.faultstring"!!*****" -ForegroundColor Red
                "`n*****!!" + $return.Envelope.Body.Fault.faultstring + "!!*****"
                exit 2
            } else {
                #return the session ID from the successful response
                return $return.Envelope.Body.Gateway_LogonResponse.SessID.InnerText
            }
        }

        #function to send a SOAP request and get system stats
        function Get-SystemStats {
            param (
                [string]$hostname, #the hostname of the gateway
                [string]$sessID,   #the session ID for the current session
                [string]$VPSID     #the VPS ID to get stats for
            )

            #construct the URL for the SOAP request
            $url = "https://$hostname/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX"
            
            #define the SOAP envelope for retrieving system stats
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
            
            #send the SOAP request and capture the response
            try {
                [xml]$return = Invoke-WebRequest -Uri $url -Method Post -ContentType "text/xml" -Body $soapTemplate
            } catch {
                #output an error message if the SOAP request fails
                Write-Host "Error sending SOAP request: $_" -ForegroundColor Red
                exit 1
            }

            #check for errors in the response
            if ($return.Envelope.Body.Fault) {
                #output the fault string if an error is returned and save the response to an XML file
                Write-Host "Error: $($return.Envelope.Body.Fault.faultstring)" -ForegroundColor Red
                Write-Output $return.OuterXml | Out-File -FilePath "error_response.xml" -Append
                return $null
            }

            #extract response details if available
            if ($return.Envelope.Body.VPSX_SystemStatsResponse) {
                #return a hashtable of system stats from the response
                return @{
                    "Status" = $return.Envelope.Body.VPSX_SystemStatsResponse.Status.InnerText
                    "TotalJobs" = $return.Envelope.Body.VPSX_SystemStatsResponse.TotalJobs.InnerText
                    "JobPerMinIn" = $return.Envelope.Body.VPSX_SystemStatsResponse.JobPerMinIn.InnerText
                    "JobPerMinOut" = $return.Envelope.Body.VPSX_SystemStatsResponse.JobPerMinOut.InnerText
                    "CurrentMemory" = $return.Envelope.Body.VPSX_SystemStatsResponse.CurrentMemory.InnerText
                    "MaxMemory" = $return.Envelope.Body.VPSX_SystemStatsResponse.MaxMemory.InnerText
                    "CurrentStorage" = $return.Envelope.Body.VPSX_SystemStatsResponse.CurrentStorage.InnerText
                    "MaxStorage" = $return.Envelope.Body.VPSX_SystemStatsResponse.MaxStorage.InnerText
                }
            } else {
                #output an error message if the response is not as expected and save the response to an XML file
                Write-Host "Error: VPSX_SystemStatsResponse not found in the response." -ForegroundColor Red
                Write-Output $return.OuterXml | Out-File -FilePath "error_response.xml" -Append
                return $null
            }
        }

        #function to handle retries for SOAP requests
        function Invoke-With-Retry {
            param (
                [scriptblock]$ScriptBlock, #the script block to execute
                [int]$Retries = 3,         #number of retry attempts
                [int]$Delay = 2            #delay between attempts in seconds
            )

            #retry logic
            for ($i = 0; $i -lt $Retries; $i++) {
                $result = & $ScriptBlock
                if ($result) {
                    #return the result if successful
                    return $result
                } else {
                    #wait for the specified delay before retrying
                    Start-Sleep -Seconds $Delay
                }
            }

            #output an error message if all retries fail
            Write-Host "Failed after $Retries attempts." -ForegroundColor Red
            exit 1
        }

        #loop through each server configuration in the JSON configuration
        foreach ($server in $config.servers) {
            $hostname = $server.hostname #the hostname of the server
            $VPSID = $server.VPSID       #the VPS ID to monitor
            $soapToken = $server.soapToken #the SOAP token for authentication

            Write-Output "Processing VPSID: $VPSID on hostname: $hostname" #debug output

            #log on to the gateway and get the session ID
            $sessID = GatewayLogon -hostname $hostname -soapToken $soapToken

            #capture performance data for the specified number of samples
            for ($i = 0; $i -lt $samples; $i++) {
                Write-Output "Capturing sample $($i + 1) of $samples..."
                $timestamp = Get-Date #get the current timestamp

                #get system stats with retries
                $systemStats = Invoke-With-Retry -ScriptBlock { Get-SystemStats -hostname $hostname -sessID $sessID -VPSID $VPSID }
                if (-not $systemStats) {
                    #skip this sample if there were errors retrieving system stats
                    Write-Host "Skipping this sample due to errors."
                    continue
                }

                #add timestamp and system stats to the sample
                $sample = @{
                    "Timestamp" = $timestamp
                    "Hostname" = $hostname
                    "VPSID" = $VPSID
                    "Status" = $systemStats["Status"]
                    "TotalJobs" = $systemStats["TotalJobs"]
                    "JobPerMinIn" = $systemStats["JobPerMinIn"]
                    "JobPerMinOut" = $systemStats["JobPerMinOut"]
                    "CurrentMemory" = $systemStats["CurrentMemory"]
                    "MaxMemory" = $systemStats["MaxMemory"]
                    "CurrentStorage" = $systemStats["CurrentStorage"]
                    "MaxStorage" = $systemStats["MaxStorage"]
                }

                #store the sample data in the array
                $data += [PSCustomObject]$sample
                Write-Output "Sample $($i + 1) captured. Sleeping for $interval seconds..."
                Start-Sleep -Seconds $interval #wait for the specified interval before capturing the next sample
            }
        }

        #stop logging
        Stop-Transcript

        #convert the collected data to a CSV file and export it
        $data | Export-Csv -Append -Path $outputFile -NoTypeInformation
        Write-Output "Performance metrics have been exported to $outputFile."
    }
    #catch block to handle errors
    catch {
        #output an error message if an exception occurs
        Write-Host "An error occurred: $_"
    }
}
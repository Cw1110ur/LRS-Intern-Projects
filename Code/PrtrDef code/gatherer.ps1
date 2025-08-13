#Creator: Sam Scoles

#Explanation:
#Parameters: The script takes three parameters: $configFilePath, $outputFilePath, and $sessionId. These are provided as inputs to the script and define the paths to the configuration file, output file, and the session ID, respectively.
#WSDL URL and ArrayMax: The WSDL URL ($wsdlUrl) specifies the endpoint for the SOAP service. The $arrayMax variable defines the maximum number of items to retrieve in a single SOAP call.
#Configuration File Loading: The script reads and parses the configuration file (config.json) to obtain administrator credentials. If the file cannot be loaded, the script outputs an error and exits.
#SOAP Request for Printer Queues: The script constructs a SOAP request template ($soapTemplate) for retrieving a list of printer queues. It includes the session ID, array max, scroll value, and admin username.
#SOAP Request Execution: The script attempts to send the SOAP request using Invoke-WebRequest. If successful, it captures the response; if not, it outputs an error message and sets the response to null.
#Response Validation and Extraction: The script checks the response for faults. If a valid response is received, it extracts the number of printers and iterates over each printer to extract its name, appending each name to the output CSV file.
#Output File Verification: The script ensures the output CSV file is not empty. If it is empty, it outputs an error message and exits.
#Completion Message: If the script completes successfully, it outputs a message indicating data gathering completion and specifies the output file path.



#define params for script
param (
    [string]$configFilePath,    #path to config.json
    [string]$outputFilePath,    #path to queues.csv
    [string]$sessionId          #session ID obtained from driver.cs
)

#output provided params to console for debugging
Write-Output "$configFilePath, config file path; $outputFilePath, output path; $sessionId, sessID"

#define the WSDL URL for SOAP
$wsdlUrl = "https://hw09971.lrsinc.org/lrs/nlrswc2.exe/vpsx/nlrswc2.exe?trid=VPSX" #place in config? FIXIT
$arrayMax = 20 #max number of arr items to receive SOAP call

#load config file to get admin credentials
try {
    #read config and parse from .json
    $config = Get-Content -Raw -Path $configFilePath | ConvertFrom-Json
    #extract the admin user/pass from config
    $adminUsername = $config.Account.Username
    $adminPassword = $config.Account.Password
    #output the admin creds for debugging
    Write-Output "$adminUsername"
    Write-Output "$adminPassword"
} catch {
    #output error msg and exit if config fails to load
    Write-Output "Error loading config file: $_"
    exit 1
}

#function to queues for admin
function Get-Queues ($sessId) {
    
    #debug output to show sessID
    Write-Output "Get Queues sessID: $sessId"
    #debug to show SOAP being sent
    Write-Output "$soapTemplate" 
}

#initialize CSV file
"QueueName" | Out-File -FilePath $outputFilePath -Encoding utf8

#initialize the SOAP request template for printer queues
Write-Output "Entering function call" #for debugging
$soapTemplate = @"
    <soapenv:Envelope xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xsd='http://www.w3.org/2001/XMLSchema' xmlns:soapenv='http://schemas.xmlsoap.org/soap/envelope/' xmlns:lrs='http://www.lrs.com'>
        <soapenv:Header/>
        <soapenv:Body>
            <lrs:VPS_PrtList7 soapenv:encodingStyle='http://schemas.xmlsoap.org/soap/encoding/'>
                <SessID xsi:type='xsd:string'>$sessionId</SessID>
                <ArrayMax xsi:type='xsd:int'>$arrayMax</ArrayMax>
                <Scroll xsi:type='xsd:int'>20</Scroll>
                <Username xsi:type='xsd:string'>$adminUsername</Username>
            </lrs:VPS_PrtList7>
        </soapenv:Body>
    </soapenv:Envelope>
"@

#try sending SOAP to retreive printer queues
try {
    Write-Output "Sending SOAP now" #for debugging
    #send SOAP using Invoke-WebRequest and capture response
    [xml]$response = Invoke-WebRequest -Uri $wsdlUrl -Method Post -ContentType "text/xml" -Body $soapTemplate

    #output the status code and response for debugging
    Write-Output "Body: ${response.StatusCode}"
    Write-Output "Response: ${response.OuterText}"
} catch {
    #output error if SOAP fail
    Write-Output "Error sending SOAP request for queues: $_" -ForegroundColor Red
    $response = $null
}

#check if the SOAP response contains a fault
if ($response.Envelope.Body.Fault) {
    #output the fault string if an error exists
    Write-Output "`n*****!!"$response.Envelope.Body.Fault.faultstring"!!*****" -ForegroundColor Red
    $response = $null
}

#get queues for admin
#for debugging
Write-Output "Response: ${response.Envelope.Body.VPS_PrtList7Response.TotalPrt.InnerText}"
Write-Output "Queues: ${$response.Envelope.Body.VPS_PrtList7Response.TotalPrt.InnerText}"

#check for a successfull response and extract the queues
if ($response -ne $null) {
    #calc the number of printers returned in the response
    $amountPrinters = $response.Envelope.Body.VPS_PrtList7Response.LastPrt.InnerText - $response.Envelope.Body.VPS_PrtList7Response.FirstPrt.InnerText
    #output the number of printers
    Write-Output "$amountPrinters"
    #loop through each printer in the response
    for($i = 0; $i -le ($amountPrinters - 1); ($i++)){
        #extract the printer name from the response
        $queue = $response.Envelope.Body.VPS_PrtList7Response.PrtList.item[$i].PrtName.InnerText
        #output the queue name
        Write-Output "Grabbed $queue"
        #append the queue name to the .csv
        $queue | Out-File -FilePath $outputFilePath -Encoding utf8 -Append
    }
} else {
    #output an error msg if the queues couldnt be retreived
    Write-Output "Failed to retrieve queues for the admin account"
    exit 1
}

#ensure queues.csv is not empty
if ((Get-Content -Path $outputFilePath).Length -le 1) {
    Write-Output "queues.csv is empty. Exiting..."
    exit 1
}

#output success and path
Write-Output "Data gathering complete. Output saved to $outputFilePath."
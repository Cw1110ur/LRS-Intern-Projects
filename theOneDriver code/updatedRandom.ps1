#Creators: Howard Morgenthaler, Sam Scoles

#Explanation:
#Parameters: The script takes a single parameter $queues, which is expected to be a comma-separated string of queue names. This is defined in the param block.
#Splitting Queues: The script splits the $queues string into an array of individual queue names using the -split operator. This allows the script to handle each queue as a separate element in the array.
#Print Jobs Path: The variable $printJobsPath is defined to specify the directory where print job files are located. This is assumed to be a folder named "Jobs" in the current directory.
#Random Selection Function: The script defines a function Get-RandomItem, which takes an array as input and returns a random element from that array. It uses the Get-Random cmdlet to generate a random index within the bounds of the array and handles any errors that might occur during this process.
#Selecting a Random Queue: The script uses the Get-RandomItem function to select a random queue from the array of queues. This selected queue is stored in the $selectedQueue variable.
#Filtering Print Jobs: The script retrieves all files from the print jobs directory using Get-ChildItem and then filters these files to include only those with specific extensions: .pdf, .doc, and .docx. This filtered list is stored in $filteredPrintJobs.
#Selecting a Random Print Job: From the filtered list of print jobs, the script uses the Get-RandomItem function again to select a random print job. The full path of this selected print job is stored in $selectedPrintJob.
#Output: Finally, the script outputs the selected queue and the full path of the selected print job using Write-Output. This output is typically captured and used by other parts of the program to proceed with the print job simulation.



#declare a param block for the script, allowing the passing of queues
param (
    [string]$queues
)

#check to see if the parameters are passed
#convert the , separated string of queues into an array
[string[]]$queues = $queues -split ','

#path to the folder containing print jobs
$printJobsPath = "Jobs/"

#function to select a random item from an array
function Get-RandomItem($array) {
    try {
        #get a random index w/i the bounds of the array
        $index = Get-Random -Minimum 0 -Maximum $array.Count
        #return the item at the random index
        return $array[$index]
    }
    catch {
        #output an error msg if there is an issue selecting a random item
        Write-Output "Error selecting random item: $_"
    }
}

#use the Get-RandomItem function to select a random queue from the list
$selectedQueue = Get-RandomItem $queues

#select random print job
#get all files int the print jobs dir
$printJobs = Get-ChildItem -Path $printJobsPath
#filter the list of files to include only those with specific extensions
$filteredPrintJobs = $printJobs | Where-Object { $_.Extension -in ".pdf", ".doc", ".docx" }
#use the Get-RandomItem function to select a random print job from the list
$selectedPrintJob = Get-RandomItem $filteredPrintJobs
#output the selected queue and the fill path of the selected print job
Write-Output "Selected Queue: $selectedQueue"
Write-Output "Selected Job: $($selectedPrintJob.FullName)"
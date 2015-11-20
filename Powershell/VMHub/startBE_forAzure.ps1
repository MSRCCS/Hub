#######################################################################################
## Copyright 2015, Microsoft.	All rights reserved    
## Author: Yuxiao Hu                                                  
## start Prajna Backend (recognition service)  on domain cluster and hook them with Azure VMs 
#######################################################################################

<#
.SYNOPSIS
    utilities for start Prajna Backend service on domain cluster
.DESCRIPTION
    start/stop/monitor Prajna Backend service on domain cluster
.PARAMETER action
    start or stop, whether you want to start or stop the Prajna Backend service on domain cluster, default is start
.PARAMETER dummy
    add this switch to also start the dummy classifiers, besides the regular classifiers specified with modeldir and model parameters
.PARAMETER infDir
    The folder containing the azure-all.inf file (and single VM.inf, e.g. imhub-westus.inf, etc.), which include all the VMs, default is \\yuxiao-z840\OneNet\cluster, which contains all VMs
.PARAMETER modeldir
    The folder containing the backend service (recognizer) models, default is \\yuxiao-z840\OneNet\data\models, which contains all available models
.PARAMETER depDir
    The folder containing the backend service (recognizer) binaries/asemblies, default is \\yuxiao-z840\OneNet\data\dependencies, which contains all required .exe and .dlls
.PARAMETER model
    by default it is empty, which will load all the models under modeldir. If you specify some model names, e.g. "Dog,Beijing", then only these models will be loaded.
.PARAMETER cluster
    The path to the cluster .inf file , , default is \\yuxiao-z840\OneNet\cluster\OneNet, which contains all prajna cluster nodes. you can replace it with other .inf files, (remove the .inf file extension)
.PARAMETER only
    The Prajna Hub gateway server URL to register these Prajna backend services, default is all VMs running on Azure, you can also specify part of them , e.g. "imhub-AusE,imhub-AusS"
.PARAMETER restartPrajnaClient
    use this switch if you want to restart the prajna client (daemon) on all cluster nodes, to have a clean environment
.PARAMETER extraFENode 
    add more machines which you want these Prajna backend service to register to (besides the VMs specified in above -only parameters), example: -extraFENode "-only OneNet11 -only OneNet12" will also register to OneNet11 and OneNet12
.PARAMETER instance
    specify the instance index number of the Prajna backend service instance, default is 0, so the instance name will look like "prajnarRecognitionServer_Beijing_0", you can also set it to 1, 2, or any other different numbers to avoid conflict
.PARAMETER mon
    after the Prajna backend services are started, start the VHubBackendMonitor, not supported for now
.PARAMETER verboseLevel 
    verbose level to show the debug information, default is 4

.EXAMPLE
    exp#1: start Prajna backend service on all cluster nodes, and register them to all the VMs
        .\startBE_forAzure.ps1
        or .\startBE_forAzure.ps1 action -start
    exp#2: stop Prajna backend service on all cluster nodes
        .\startBE_forAzure.ps1 action -stop
    exp#3: start Prajna backend service on all cluster nodes, before that, restart the prajna clients (to make sure there are no lingering PrajnaClientExt processes) 
        .\startBE_forAzure.ps1 -action start -restartPrajnaClient
    exp#4: start Prajna backend service on all cluster nodes, and also start the monitoring program
        .\startBE_forAzure.ps1 -action start -mon
    exp#5: start Prajna backend service on all cluster nodes, besides registering them to all the VMs, also register them to a local machine called yuxiao-z840
        .\startBE_forAzure.ps1 -action start -extraFENode "-only yuxiao-z840"
    exp#6: start Prajna backend service on all cluster nodes, only registering them to some of the VMs
        .\startBE_forAzure.ps1 -action start -only "imhub-westus,imhub-eastus"
.NOTES
    Author: Yuxiao Hu (yuxhu@microsoft.com)
    Date:   April 3, 2015    
#>

param(
	
        [ValidateSet("start","stop")] $action = "start",
        #[switch] $mon, #temporary disabled before SE monitor is available
        [switch] $dummy,
        [ValidateScript({Test-Path "$_.inf" -PathType 'Leaf'})][string] $cluster = "\\yuxiao-z840\OneNet\Cluster\OneNet_All",
	    [string] $only = "imhub-AusE,imhub-AusS,imhub-AusSE,imhub-BrazilS,imhub-CUS,imhub-EAsia,imhub-EastUS,imhub-EastUS2,imhub-europe,imhub-JapanE,imhub-JapanW,imhub-NCUS,imhub-NEurope,imhub-SCUS,imhub-SEAsia,imhub-WestUS", 
        [string] $extraFENode = "", #example: "-only OneNet11 -only OneNet12"
        [ValidateRange(0,9)] [Int] $instance = 0,
        #[ValidateRange(1000,1500)] [Int] $port = 1012,
        [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $modeldir = '\\yuxiao-z840\OneNet\data\models',
        [string] $models = '',
        [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $depdir = '\\yuxiao-z840\OneNet\data\dependencies',
        [switch]$restartPrajnaClient,
        [ValidateRange(0,6)] [Int] $verboseLevel = 4
)

#build VM list so that backend machines can hook up to
$VMs = $only.Split(",")
$VMURLs=@();
foreach ($VM in $VMs)
{  
    $VMURLs += ( " -only " + $VM + ".cloudapp.net") 
}
$VMURLList = $VMURLs  -join " "
$VMURLListOnlyString = $VMURLList 

# now start backend on domain clusters
Push-Location ..\..\Powershell\DomainClusters
if ( $CredentialFile ) { Remove-Variable CredentialFile -ErrorAction SilentlyContinue -scope 1}
if ( $port ) { Remove-Variable port  -ErrorAction SilentlyContinue -scope 1}
if ( $homein ) { Remove-Variable homein  -ErrorAction SilentlyContinue -scope 1}
if ( $targetSrcDir ) { Remove-Variable targetSrcDir  -ErrorAction SilentlyContinue -scope 1}
if ( $user ) { Remove-Variable user  -ErrorAction SilentlyContinue -scope 1}
Invoke-Expression .\config.ps1
Invoke-Expression .\parsecluster.ps1
Invoke-Expression .\getcred.ps1

if ($restartPrajnaClient.IsPresent)
{
    # restart prajna clients
    .\restartclient.ps1 -cluster $cluster
    write-Host ("sleep 10 sec to make sure all clients are restarted.")
    Start-Sleep 10
}
Pop-Location


# get model list for recognition services
$ModelList = iex 'if ($models.length -gt 0 ) {$models.Split(",")} else {Split-Path "$modeldir\*" -Leaf -Resolve} ' 

## start recognition service (Prajna Hub backend) on all nodes and register to the 5 gateway machines defined above: ONENET11,ONENET12,ONENET13,ONENET14,ONENET15
##..\..\VHub\PrajnaRecogServer.vHub\bin\Debug\PrajnaRecogServer.vHub.exe -start -only "ONENET11,ONENET12,ONENET13,ONENET14,ONENET15" -cluster \\yuxiao-z840\onenet\Cluster\OneNet_All.inf -rootdir F:\Src\SkyNet\IMHub\PrajnaRecogServerPrajna -con -instance 1
## start vHub recognition service on all nodes and register to local machine
##..\..\VHub\PrajnaRecogServer.vHub\bin\Debug\PrajnaRecogServer.vHub.exe -start -only $VMURLListOnlyString -cluster \\yuxiao-z840\OneNet\Cluster\OneNet_All.inf -rootdir F:\Src\SkyNet\IMHub\PrajnaRecogServerPrajna -con -instance 0

# start the dummy classifier
if($dummy.IsPresent)
{
    $DummyServicePath = "..\..\..\Services\samples\DummyRecogServer\bin\Debugx64\DummyRecogServer.exe"
    if( Test-Path -Path $DummyServicePath)
    {   
        $cmd = "$DummyServicePath -$action -task -cluster $cluster.inf -con -instance $instance $VMURLListOnlyString $extraFENode -verbose $verboseLevel"
        Write-Verbose $cmd
        Invoke-Expression ($cmd)
    }
    else
    {
        Write-Host("Error! File $DummyServicePath does not exist!! check your dummy service binary file")
    }
}

# start all normal backend service (recognizer) instances
foreach ($Model in $ModelList)
{
    $RecognizerPath = "..\..\samples\PrajnaRecogServer\bin\Debug\PrajnaRecogServer.exe"
    if( (Test-Path "$modeldir\$Model" -PathType 'Container')  -And (Test-Path $RecognizerPath -PathType 'Leaf'))
    {
        $cmd = "$RecognizerPath -$action -cluster $cluster.inf -con -instance $instance -modeldir $modeldir -model $Model -depdir $depdir $VMURLListOnlyString  $extraFENode -verbose $verboseLevel"
        Write-Verbose $cmd
        Invoke-Expression ($cmd)
    }
    else
    {
        Write-Host("Error! Executable file $RecognizerPath or data folder $modeldir\$Model\data does not exist!! check your -model parameter and make sure they are under $modeldir")
    }
}

if($mon.IsPresent)
{
    $MonitorPath = "..\..\bin\Debugx64\MonitorServiceEndpoint\MonitorServiceEndpoint.exe"
    if( Test-Path -Path $MonitorPath)
    {   
        Write-Verbose "Now start Prajna backend services monitor"
        $cmd = "$MonitorPath -cluster $cluster.inf"
        Write-Verbose $cmd
        Invoke-Expression ($cmd)
    }
    else
    {
        Write-Host("Error! File $MonitorPath does not exist!!")
    }    
}

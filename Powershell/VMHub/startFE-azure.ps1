#######################################################################################
## Copyright 2015, Microsoft.	All rights reserved    
## Author: Yuxiao Hu                                                  
## start PHub frontend(Gateway) servivces on all azure VMs
#######################################################################################

<#
.SYNOPSIS
    utilities for start PHub gateway service on Azure VMs
.DESCRIPTION
    start/stop/ping/monitor PHub gateway service on Azure VMs
.PARAMETER action
    start or stop, whether you want to start or stop the PHub gateway service on Azure VMs, default is start
.PARAMETER infDir
    The folder containing the azure-all.inf file (and single VM.inf, e.g. imhub-westus.inf, etc.), which include all the VMs, default is \\hub Repository\Powershell\VMHub\cluster, which contains all VMs
.PARAMETER rootdir
    The folder containing the required website files (i.e. the webroot folder), default is \\hub Repository\src\Toolkit\LaunchGateway
.PARAMETER restartPrajnaClient
    whether you want to restart the prajna client (daemon) on all VMs, to have a clean environment
.PARAMETER ping
    after the PHub gateway services are started, try to ping them to confirm they are alive
.PARAMETER mon
    after the PHub gateway services are started, start the PHubgatewayMonitor
.PARAMETER port
    the port used to start the prajna client, current default is 1082 for all users
.PARAMETER verboseLevel 
    verbose level to show the debug information, default is 4
.PARAMETER mode
    batch/single, whether start PHub gateway service in "batch", i.e. on all VMs, or only some of them. use "VM" parameter to list the specific VMs
.PARAMETER VM
    specify the single VMs you want to start PHub gateway, e.g. "imhub-AusE,imhub-AusS"
.EXAMPLE
    .\startFE-azure.ps1
    start PHub gateway service on all VMs (by default , action is start)
.EXAMPLE
    .\startFE-azure.ps1 action -start
    start PHub gateway service on all VMs
.EXAMPLE
    .\startFE-azure.ps1 action -stop
    stop PHub gateway service on all VMs
.EXAMPLE
    .\startFE-azure.ps1 -action start -restartPrajnaClient
    start PHub gateway service on all VMs, before that, restart the prajna clients (to make sure there are no lingering PrajnaClientExt processes) 
.EXAMPLE
    .\startFE-azure.ps1 -action start -mon
    start PHub gateway service on all VMs, and also start the monitoring program
.NOTES
    Author: Yuxiao Hu (yuxhu@microsoft.com)
    Date:   April 3, 2015    
#>

param(
	
        [ValidateSet("start","stop")] $action = "start",
        [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $infDir = ".\cluster",
	    [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $rootdir = '..\..\src\Toolkit\LaunchGateway',
        [switch]$restartPrajnaClient,
        [switch]$ping,
        [switch]$mon,
        [ValidateRange(1000,1500)] [Int] $port = 1082,
        [ValidateRange(0,6)] [Int] $verboseLevel = 4,
        [ValidateSet("test","prod", "all")][string] $target = "test",
        [ValidateSet("batch", "single")] $mode = "batch",
        [string] $VM = "imhub-AusE,imhub-AusS,imhub-AusSE,imhub-BrazilS,imhub-CUS,imhub-EAsia,imhub-EastUS,imhub-EastUS2,imhub-europe,imhub-JapanE,imhub-JapanW,imhub-NCUS,imhub-NEurope,imhub-SCUS,imhub-SEAsia,imhub-WestUS"
        
)


$VMs = $VM.Trim().Split(",")

$VM_INF = switch($target){"test"{"azure-test.inf"}"prod"{"azure-prod.inf"}"all"{"azure-all.inf"}}
    

if ($restartPrajnaClient.IsPresent)
{
    # restart prajna clients on VMs
    .\start-prajnaclient-azure.ps1 -target $target -port $port -kill -start -verboseLevel $verboseLevel  
}


$GatewayPath = "..\..\bin\Debugx64\LaunchGateway\LaunchGateway.exe"

#copy the Praja Vision related files to rootdir, so that they will be deployed too
if ($action -eq "start")
{
    $PrajnaVisionPath = "..\..\..\AppSuite\AppSuite.HTML5\WebRoot"
    $FromPath = $PrajnaVisionPath + "\*"
    $ToPath = $rootdir + "\WebRoot"
    if ((Test-Path "$PrajnaVisionPath" -PathType 'Container') -And (Test-Path "$ToPath" -PathType 'Container'))
    {
        Copy-Item  $FromPath $ToPath
    }
    else
    {
        Write-Host("Error! $PrajnaVisionPath or $ToPath does not exist!! check your Hub and AppSuite Repository")
    }
}

if ($mode -eq "batch")
{
    # in batch
    $cmd =  "$GatewayPath -$action -cluster $infDir\$VM_INF -con -verbose $verboseLevel -rootdir $rootdir"
    Invoke-Expression ($cmd)
}


if ($mode -eq "single")
{
    # each in a time
    foreach ($VM in $VMs)
    {
        $VMURL = "http://$VM.cloudapp.net" 
        $VMInf = "$infDir\$VM.inf"
        Write-Verbose ("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!! now $action web servers on $VMURL" )
        $cmd = "$GatewayPath -$action -cluster $VMInf -verbose $verboseLevel -rootdir $rootdir"
        Invoke-Expression ($cmd)
    }

}


#TODO:: add the image processing tool later
if($ping.IsPresent)
{     
    # ping all the PHub Gateways
    foreach ($VM in $VMs)
    {
        $VMURL = "http://" + $VM + ".cloudapp.net" 
        Write-Verbose ("ping Prajna Hub gateway at $VMURL : ")
        $cmd = "..\..\bin\Debugx64\ImageProcessing\ImageProcessing.exe -cmd List -VHub $VMURL"
        Invoke-Expression ($cmd)
    }
}


if($mon.IsPresent)
{
    $MonitorPath = "..\..\bin\Debugx64\MonitorGateway\MonitorGateway.exe"
    if( Test-Path -Path $MonitorPath)
    {   
        Write-Verbose "Now start Prajna Hub gateway monitor"
        $cmd = "$MonitorPath -cluster $infDir\$VM_INF"
        Write-Verbose $cmd
        Invoke-Expression ($cmd)
    }
    else
    {
        Write-Host("Error! File $MonitorPath does not exist!!")
    }    
}

#######################################################################################
## Copyright 2015, Microsoft.	All rights reserved    
## Author: Yuxiao Hu                                                  
## start vHub frontend(Gateway) servivces on all azure VMs
#######################################################################################

<#
.SYNOPSIS
    utilities for start VHub Frontend service on Azure VMs
.DESCRIPTION
    start/stop/ping/monitor VHub Frontend service on Azure VMs
.PARAMETER action
    start or stop, whether you want to start or stop the VHub Frontend service on Azure VMs, default is start
.PARAMETER infDir
    The folder containing the azure-all.inf file (and single VM.inf, e.g. imhub-westus.inf, etc.), which include all the VMs, default is \\yuxiao-z840\prajna\cluster, which contains all VMs
.PARAMETER rootdir
    The folder containing the required website files, default is \\yuxiao-z840\src\SkyNet\VHub\vHub.FrontEnd
.PARAMETER restartPrajnaClient
    whether you want to restart the prajna client (daemon) on all VMs, to have a clean environment
.PARAMETER ping
    after the VHub frontend services are started, try to ping them to confirm they are alive
.PARAMETER mon
    after the VHub frontend services are started, start the VHubFrontendMonitor
.PARAMETER port
    the port used to start the prajna client, current default is 1082 for all users
.PARAMETER verboseLevel 
    verbose level to show the debug information, default is 4
.PARAMETER mode
    batch/single, whether start VHub frontend service in "batch", i.e. on all VMs, or only some of them. use "VM" parameter to list the specific VMs
.PARAMETER VM
    specify the single VMs you want to start VHub frontend, e.g. "imhub-AusE,imhub-AusS"
.EXAMPLE
    .\startFE-azure.ps1
    start vHub frontend service on all VMs (by default , action is start)
.EXAMPLE
    .\startFE-azure.ps1 action -start
    start vHub frontend service on all VMs
.EXAMPLE
    .\startFE-azure.ps1 action -stop
    stop vHub frontend service on all VMs
.EXAMPLE
    .\startFE-azure.ps1 -action start -restartPrajnaClient
    start vHub frontend service on all VMs, before that, restart the prajna clients (to make sure there are no lingering PrajnaClientExt processes) 
.EXAMPLE
    .\startFE-azure.ps1 -action start -mon
    start vHub frontend service on all VMs, and also start the monitoring program
.NOTES
    Author: Yuxiao Hu (yuxhu@microsoft.com)
    Date:   April 3, 2015    
#>

param(
	
        [ValidateSet("start","stop")] $action = "start",
        [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $infDir = "\\yuxiao-z840\OneNet\cluster",
	    [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $rootdir = '\\yuxiao-z840\src\SkyNet\VHub\vHub.FrontEnd',
        [switch]$restartPrajnaClient,
        [switch]$ping,
        [switch]$mon,
        [ValidateRange(1000,1500)] [Int] $port = 1082,
        [ValidateRange(0,6)] [Int] $verboseLevel = 4,
        [ValidateSet("batch", "single")] $mode = "batch",
        # [string] $VM = "imhub-AusE,imhub-AusS,imhub-AusSE,imhub-BrazilS,imhub-CUS,imhub-EAsia,imhub-EastUS,imhub-EastUS2,imhub-europe,imhub-JapanE,imhub-JapanW,imhub-NCUS,imhub-NEurope,imhub-SCUS,imhub-SEAsia,imhub-WestUS"
        [string] $VM = "imhub-westus"
)


$VMs = $VM.Trim().Split(",")

if ($restartPrajnaClient.IsPresent)
{
    # restart prajna clients on VMs
    .\start-prajnaclient-azure.ps1 -port $port -kill -start -verboseLevel $verboseLevel  
}

if ($mode -eq "batch")
{
    # in batch
    $launchInf = $infDir + "\" + $VM + ".inf"
    write-host "Launch on " $launchInf
    $cmd = "..\..\src\Toolkit\LaunchGateway\bin\Releasex64\LaunchGateway.exe -$action -cluster $infDir\azure-all.inf -con -verbose $verboseLevel -rootdir $rootdir"
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
        $cmd = "..\vHub.FrontEnd\bin\Debug\vHub.FrontEnd.exe -$action -cluster $VMInf -verbose $verboseLevel -rootdir $rootdir"
        Invoke-Expression ($cmd)
    }

}

if($ping.IsPresent)
{     
    # ping all the vHub Gateways
    foreach ($VM in $VMs)
    {
        $VMURL = "http://" + $VM + ".cloudapp.net" 
        Write-Verbose ("ping vHub frontend at $VMURL : ")
        $cmd = "..\vHub.Clients\vHub.ImageProcessing\bin\Debug\vHub.ImageProcessing.exe -cmd List -vHub $VMURL"
        Invoke-Expression ($cmd)
    }
}

if($mon.IsPresent)
{
    Write-Verbose "Now start vHub frontend monitor"
    $cmd = "..\vHub.MonitorFrontEnd\bin\Debug\vHub.MonitorFrontEnd.exe -cluster $infDir\azure-all.inf"
    Invoke-Expression ($cmd)
}

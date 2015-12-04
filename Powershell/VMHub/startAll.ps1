#######################################################################################
## Copyright 2015, Microsoft.	All rights reserved    
#######################################################################################

<#
.SYNOPSIS
    utilities to deploy/start vHub Frontend and Backend
.DESCRIPTION
    deploy/start/monitor vHub
.PARAMETER target
    the target of the deployment, test VMs, or prod VMs. default is "test"
.PARAMETER user
    currently will use the default values for all parameters, which will deploy from shared folder to test Azure VMs (for vHub frontend) and Prajna Clusters (for vHub backend)
.EXAMPLE
    exp#1: deploy prajna client to all VMs (default deploy source folder is \\yuxiao-z840\src , target folder is c:\PrajnaDeployImhub), 
           if Prajna clients have been deployed to the target folder, prajna client will be started for fast copy
           if Prajna clients haven's been deployed to the target folder, the required binaries will be copied through vm copy, which is slower 
        .\startAll -deployFE
    exp#2: start/stop vHub front end on all VMs
  .\      .\startAll -restartFE
.NOTES
    Author: Yuxiao Hu (yuxhu@microsoft.com)
    Date:   May, 2015    
#>

param(
	## Machine name to deploy
        [switch] $deployFE,
        [switch] $restartFE,
        [switch] $deployBE,
        [switch] $restartBE,
        [switch] $mon,
        [ValidateSet("test","prod", "all")][string] $target = "test",
        [string] $passw = "",
        [ValidateRange(0,6)] [Int] $verboseLevel = 4
        
)

#restart vHub FE on VMs
if($deployFE.IsPresent)
{
    $cmd = ".\start-prajnaclient-azure.ps1 -target $target -kill -deploy -passw '$passw';  .\startFE-azure.ps1 -action start -target $target -passw '$passw'"
    Invoke-Expression $cmd

} 
elseif ($restartFE.IsPresent)
{
    $cmd = ".\start-prajnaclient-azure.ps1 -target $target -kill -start -passw '$passw'; .\startFE-azure.ps1 -action start -target $target -passw '$passw'"
    Invoke-Expression $cmd
}


#restart prajna and vHub BE on cluster
if($deployBE.IsPresent)
{
    $cmd = ".\startBE_forAzure.ps1 -action start -restartPrajnaClient " 
    Invoke-Expression $cmd
} elseif ($restartBE)
{
    $cmd = ".\startBE_forAzure.ps1 -action stop ; .\startBE_forAzure.ps1 -action start" 
    Invoke-Expression $cmd
}


#start monitoring tools
if( $mon.IsPresent)
{
    $cmd = "..\vHub.MonitorFrontEnd\bin\Debug\vHub.MonitorFrontEnd.exe -cluster C:\OneNet\Cluster\azure-all.inf"
    Invoke-Expression $cmd
    $cmd = "..\vHub.MonitorBackEnd\bin\Debug\vHub.MonitorBackEnd.exe -cluster C:\OneNet\Cluster\OneNet_All.inf"
    Invoke-Expression $cmd
}

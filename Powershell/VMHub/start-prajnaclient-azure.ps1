#######################################################################################
## Copyright 2015, Microsoft.	All rights reserved    
#######################################################################################

<#
.SYNOPSIS
    utilities for deploy prajna client on Azure VMs
.DESCRIPTION
    deploy/start/stop/check/reset/kill prajna client 
.PARAMETER user
    the account name used on VMs to start prajna services, default is imHub
.PARAMETER start
    start the (current deployed) prajna client on all VMs    
.PARAMETER stop
    stop the (current deployed) prajna client on all VMs
.PARAMETER deploy
    deploy the prajna client from your source code folder (speficiied in config.ps1) to all VMs, and start it
.PARAMETER list
    check whether prajna client is running on all VMs, in the result list, "TRUE" means we found a running "PrajnaClient.exe" instance on that VM
.PARAMETER kill 
    kill the prajna client related processes (including the containers) on all VMs
.PARAMETER reset
    delete the deployed prajna client, so that the next deploy will be from scratch (i.e. will use vmcopy, instead of prajna copy)
.PARAMETER port
    The port used for prajna client daemon, default is 1082 for all users
.PARAMETER target
    the target of the deployment, test VMs, or prod VMs. default is "test"
.PARAMETER dest
    the target folder of the deployment, default is \\VM\c$\PrajnaDeployImhub"
.PARAMETER infDir
    The folder containing the azure-all.inf file (and single VM.inf, e.g. imhub-westus.inf, etc.), which include all the VMs, default is \\yuxiao-z840\prajna\cluster, which contains all VMs
.PARAMETER verboseLevel 
    verbose level to show the debug information, default is 4
.EXAMPLE    
    .\start-prajnaclient-azure.ps1 -deploy
    deploy prajna client to all VMs (default deploy folder is c:\PrajnaDeployImhub), 
    if Prajna clients have been deployed to the target folder, prajna client will be started for fast copy
    if Prajna clients haven's been deployed to the target folder, the required binaries will be copied through vm copy, which is slower 
.EXAMPLE
    .\start-prajnaclient-azure.ps1 -start
    start prajna client on all VMs
.EXAMPLE    
    .\start-prajnaclient-azure.ps1 -stop
    stop prajna client on all VMs
.EXAMPLE
    .\start-prajnaclient-azure.ps1 -kill
    kill prajna client process and container process on all VMs (to make sure there is no lingering PrajnaClientExt processes)
.EXAMPLE
    .\start-prajnaclient-azure.ps1 -reset
    reseet VMs for a clean start (will delete the _current link and assume there is no earlier prajna deployment)
.NOTES
    Author: Yuxiao Hu (yuxhu@microsoft.com)
    Date:   April 3, 2015    
#>

param(
        [Parameter(HelpMessage = "user name (default User: imhub)")][string] $user = "Imhub",
        [ValidateRange(1000,1500)] [Int] $port = 1082,
        [Parameter(HelpMessage = 'deploy prajna client to Azure VMs')][switch] $deploy,
        [switch] $start,
        [switch] $stop,
        [switch] $list,
        [switch] $reset,
        [switch] $kill,
        [ValidateSet("test","prod", "all")][string] $target = "test",
        [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $dest = "c:\PrajnaDeploy$user",
        [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $infDir = "\\yuxiao-z840\OneNet\cluster",
        [ValidateRange(0,6)] [Int] $verboseLevel = 4,
        [string] $passw = ""
        
)


if( -not $deploy.IsPresent -and -not $start.IsPresent -and -not $stop.IsPresent -and -not $list.IsPresent -and -not $reset.IsPresent -and -not $kill.IsPresent)
{
    Write-Error "please specify an action to execute: deploy/start/stop/reset" -Category InvalidArgument
    exit -1
}

if ( -not $passw -or $passw.Length -le 0 ) 
{
    Write-Verbose ("Now get the password of of the account to get access the VMs")
    $readpassw = Read-Host "Please input password for user $user :" -AsSecureString
    #will switch to use stored credential later after it is supported by .\Batch-Remote-Exe.ps1
    $plain = (New-Object System.Management.AUtomation.PSCredential('dummy',$readpassw)).GetNetworkCredential().password
}
else 
{
    $plain = $passw
}


## deploy and/or restart Prajna client on Azure VMs 

# current folder (which contains this script) is root\VHub\Powershell 
Push-Location ..\DomainClusters
# now at ..\..\Powershell\DomainClusters folder
# override the homein server to a VM, because it cannot be a machine on domain cluster
# $homein = "imhub-CUS.cloudapp.net"
Invoke-Expression .\config.ps1
# currently the VM deploy script doesn't support load credential from file
#Invoke-Expression .\getcred.ps1
$source = $localSrcFolder;
Write-Verbose ("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!current port: " + $port)
Write-Verbose ("local source folder: " + $localSrcFolder )
Pop-Location
 
Push-Location ..\Azure
# now at root\Powershell\Azure

$VM_CSV = switch($target){"test"{".\vm-test.csv"}"prod"{".\vm-prod.csv"}"all"{".\vm.csv"}}

if($stop.IsPresent)
{
    # kill all prajna clients
    #.\vmlaunch.ps1 -exe '$dest\_current\bin\debugx64\prajnaclient\prajnaclient.exe' -session $session -kill $true ; Start-Sleep 10
    Write-Verbose ("Now start kill PrajnaClient.exe process on all the VMs")
    $cmd = "..\DomainClusters\config-Azure.ps1 ; .\vmlaunch.ps1 -exe ""$dest\_current\bin\debugx64\client\prajnaclient.exe"" -session `$Session -kill `$true"
    .\Batch-Remote-Exe.ps1 -Csv $VM_CSV -InfDir $infDir -Cmd $cmd -User $user -Password $plain
}

if($kill.IsPresent)
{
    $cmd = "Invoke-Command -Session `$Session { Get-Process | Where {`$_.Path -like ""*PrajnaClientExt_*.exe"" } | Stop-Process ; Get-Process | Where {`$_.Path -like ""*PrajnaClient.exe"" } | Stop-Process ; Get-Process | Where {`$_.Path -like ""*OnenetClient.exe"" } | Stop-Process; Get-Process | Where {`$_.Path -like ""*OnenetClient.exe"" } | Stop-Process}"
    .\Batch-Remote-Exe.ps1 -Csv $VM_CSV -Cmd $cmd  -User $user -Password $plain
    #/$cmd = "Invoke-Command -Session `$Session { Get-Process | Where {`$_.Path -like ""*PrajnaClient.exe"" } | Stop-Process }"
    #.\Batch-Remote-Exe.ps1 -Csv $VM_CSV -Cmd $cmd  -User $user -Password $plain
}

if($reset.IsPresent)
{
    # reset clients by remove the entire deploy folder, you may need to first manually stop the clients
    Write-Verbose -message "Now clean the existing prajna deployments" -Verbose
    $cmd = "Invoke-Command -Session `$Session { cmd /c ""rd /s /q $dest""}"
    .\Batch-Remote-Exe.ps1 -Csv $VM_CSV -Cmd $cmd  -User $user -Password $plain
}

if($start.IsPresent)
{
    # start client
    #.\vmlaunch.ps1 -exe '$dest\_current\bin\debugx64\prajnaclient\prajnaclient.exe' -session $session -argumentList @("-verbose", "$verboseLevel")
    Write-Verbose ("Now start start PrajnaClient.exe process on all the VMs (without deployment)")
    $cmd = "..\DomainClusters\config-Azure.ps1 ; .\vmlaunch.ps1 -exe ""$dest\_current\bin\debugx64\client\prajnaclient.exe"" -session `$Session -argumentList @(""-verbose"", $verboseLevel) -background `$true; Start-Sleep 15"
    .\Batch-Remote-Exe.ps1 -Csv $VM_CSV -InfDir $infDir -Cmd $cmd -User $user -Password $plain
}

if($deploy.IsPresent)
{
    # redeploy latest prajna codes to all VMs and restart clients 
    Write-Verbose ("Now start deploy the VMs and start PrajnaClient.exe process")
    .\deploycluster.ps1 -Csv $VM_CSV -InfDir $infDir -Source $source -Dest $dest -User $user -Password $plain -Config ..\DomainClusters\config-Azure.ps1
}

if($list.IsPresent)
{
    # list clients:
    Write-Verbose ("Now scan the VMs for PrajnaClient.exe process")
    $cmd = "Invoke-Command -Session `$Session { `$p = Get-Process | Where {`$_.Path -like ""*PrajnaClient.exe"" }; if (!`$p) { throw } else{ Write-Verbose (`$p | Format-Table | Out-String )} }"
    .\Batch-Remote-Exe.ps1 -Csv $VM_CSV -Cmd $cmd  -User $user -Password $plain
}


Pop-Location

#######################################################################
## Copyright 2013, Microsoft.	All rights reserved    
## Author: Jin Li                                                  
## Enable firewall on a particular client
##############################################################################
param(
	## Machine name to deploy
	[string] $machineName, 
    [string] $clusterLst, 
    [string] $cluster
)

Push-Location ..\..\Powershell\DomainClusters
Invoke-Expression .\config.ps1
Invoke-Expression .\parsecluster.ps1
Invoke-Expression .\getcred.ps1
Pop-Location


## $cmd1 = 'New-NetFirewallRule -Enabled true -Program "C:\PrajnaSource\bin\Debugx64\PrajnaClient\PrajnaClient.exe” -Action Allow -Profile Private, Public, Domain -DisplayName "Allow PrajnaClient" -Description "Allow PrajnaClient" -Direction Inbound -LocalPort 1082 -Protocol TCP'
## $cmd1 = 'Get-NetFirewallProfile | Set-NetFirewallProfile –Enabled False'
$cmd1 = 'New-NetFirewallRule -display "port 80" -Direction Inbound -LocalPort 80-81 -Protocol TCP -Action Allow'
$sb1=$executioncontext.InvokeCommand.NewScriptBlock( $cmd1 )
$cmd2 = 'netsh http add urlacl url=http://+:80/vHub user=Redmond\yuxhu'
$sb2=$executioncontext.InvokeCommand.NewScriptBlock( $cmd2 )
$cmd3 = 'netsh http delete urlacl url=http://+:80/ImGatewayService'
$sb3=$executioncontext.InvokeCommand.NewScriptBlock( $cmd3 )
foreach ($mach in $machines ) 
{
	Invoke-Command -ComputerName $mach -ScriptBlock $sb1 -Credential $cred -Authentication CredSSP
	write-host $cmd1 "to machine " $mach
	
	Invoke-Command -ComputerName $mach -ScriptBlock $sb3 -Credential $cred -Authentication CredSSP
	write-host $cmd3 "to machine " $mach

	Invoke-Command -ComputerName $mach -ScriptBlock $sb2 -Credential $cred -Authentication CredSSP
	write-host $cmd2 "to machine " $mach
}



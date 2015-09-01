#######################################################################################
## Copyright 2015, Microsoft.	All rights reserved    
## Author: Yuxiao Hu                                                  
## start vHub (prajna client, recognition service, vHub gateway) on verari 
#######################################################################################
param(
	## Machine name to deploy
        [string] $cluster,
	[string] $only
)

#e.g. -cluster C:\onenet\Cluster\Verari_150130_all
#     -only "VERARI-G10-510,VERARI-G10-601,VERARI-G10-605,VERARI-G11-102,VERARI-G11-103" 

Push-Location ..\..\Powershell\DomainClusters

Invoke-Expression .\config.ps1
Invoke-Expression .\parsecluster.ps1
Invoke-Expression .\getcred.ps1

## restart clients
.\restartclient.ps1 -cluster C:\onenet\Cluster\Verari_150130_all  
Pop-Location

## start vHub Gateway on 5 of the verari machines: VERARI-G10-510,VERARI-G10-601,VERARI-G10-605,VERARI-G11-102,VERARI-G11-103
..\..\vHub\vHub.FrontEnd\bin\Debug\vHub.FrontEnd.exe -start -cluster C:\OneNet\Cluster\Verari_150316_vHubGateway.inf -log C:\log\VHub\a.log -con -rootdir F:\Src\SkyNet\IMHub\IMHub

## start vHub recognition service on all machines, and register to the 5 gateway machines defined above: VERARI-G10-510,VERARI-G10-601,VERARI-G10-605,VERARI-G11-102,VERARI-G11-103
..\..\VHub\PrajnaRecogServer.vHub\bin\Debug\PrajnaRecogServer.vHub.exe -start -only "VERARI-G10-510,VERARI-G10-601,VERARI-G10-605,VERARI-G11-102,VERARI-G11-103" -cluster C:\onenet\Cluster\Verari_150130_all.inf -rootdir F:\Src\SkyNet\IMHub\PrajnaRecogServerPrajna -con


## ..\..\vHub\SampleRecogServerFSharp\bin\Debug\SampleRecogServerFSharp.exe -start -cluster C:\OneNet\Cluster\OneNet_JinL_6to10.inf -only jinl4 -only onenet06 -only onenet07 -only onenet08 -only onenet09 -only onenet10 -con
## ..\..\vHub\PrajnaRecogServer.vHub\bin\Debug\PrajnaRecogServer.vHub.exe -start -cluster C:\OneNet\Cluster\OneNet_JinL_6to10.inf -only jinl4 -only onenet06 -only onenet07 -only onenet08 -only onenet09 -only onenet10 -con


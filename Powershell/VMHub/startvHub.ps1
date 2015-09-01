#######################################################################################
## Copyright 2015, Microsoft.	All rights reserved    
## Author: Yuxiao Hu                                                  
## start vHub (prajna client, recognition service, vHub gateway) on prajna cluster
#######################################################################################


param(
	## Machine name to deploy
        [ValidateSet("start","stop")] $action = "start",
        [ValidateScript({Test-Path "$_.inf" -PathType 'Leaf'})][string] $cluster = "C:\onenet\Cluster\onenet_All",
	    [string] $only = "ONENET11,ONENET12,ONENET13,ONENET14,ONENET15",
        [ValidateRange(0,9)] [Int] $instance = 0,
        [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $modeldir = 'C:\onenet\data\models',
        [string] $models = '',
        [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $depdir = 'C:\onenet\data\dependencies',
        [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $rootdir = 'F:\Src\SkyNet\IMHub\IMHub',
        [switch]$restartPrajnaClient,
        [switch]$startFE
        

)

# usage:
# -action [start|stop]: start or stop the vHub backend recognizers, by default, will start FE
# -cluster: cluster file to specify the target nodes where vHub Backend will be running on, e.g. C:\onenet\Cluster\OneNet_All, or  C:\onenet\Cluster\Verari_All_20150323
# -only: the machine list to specify the target nodes where vHub Frontend will be running on, e.g. "ONENET11,ONENET12,ONENET13,ONENET14,ONENET15" 
# -instance: instance label, which can be digits 0~9
# -modeldir: folder contains all the models for Backend recognizers, e.g. 'C:\OneNet\data\models', which should contains, [dog|cat]\data\models\dog\*
# _models: string to specify which model(s) to start or stop, e.g. 'dog,beijing,office', if not provided, all the models under modeldir will be processed
# -depdir: folder contains all the binaries/executables for Backend recognizers, e.g.C:\OneNet\data\dependencies
# -rootdir: folder contains data files for vHub frontend webroot
# -restartPrajnaClient: switch to restart the prajna clients on the target cluster, before starting the BE/FE
# -startFE: switch to start the vHub frontend


Push-Location ..\..\Powershell\DomainClusters

Invoke-Expression .\config.ps1
Invoke-Expression .\parsecluster.ps1
Invoke-Expression .\getcred.ps1

if ($restartPrajnaClient.IsPresent)
{
    ## restart prajna clients
    .\restartclient.ps1 -cluster $cluster
}

Pop-Location


## start vHub recognition service on all nodes and register to the 5 gateway machines defined above: ONENET11,ONENET12,ONENET13,ONENET14,ONENET15
##..\..\VHub\PrajnaRecogServer.vHub\bin\Debug\PrajnaRecogServer.vHub.exe -start -only "ONENET11,ONENET12,ONENET13,ONENET14,ONENET15" -cluster $cluster.inf -rootdir F:\Src\SkyNet\IMHub\PrajnaRecogServerOneNet -con -instance 1
## start vHub recognition service on all nodes and register to local machine
##..\..\VHub\PrajnaRecogServer.vHub\bin\Debug\PrajnaRecogServer.vHub.exe -start -only yuxiao-z840 -cluster $cluster.inf -rootdir F:\Src\SkyNet\IMHub\PrajnaRecogServerPrajna -con -instance 0

$ModelList = iex 'if ($models.length -gt 0 ) {$models.Split(",")} else {Split-Path "$modeldir\*" -Leaf -Resolve} ' 


foreach ($Model in $ModelList)
{
    if(Test-Path "$modeldir\$Model\data" -PathType 'Container')
    {
        $cmd = "..\..\VHub\PrajnaRecogServer.vHub\bin\Debug\PrajnaRecogServer.vHub.exe -$action -only ""$only"" -cluster $cluster.inf -con -instance $instance -modeldir $modeldir\$Model\data -depdir $depdir"
        Write-Host ("now starting vHub frontend for model $Model with cmd: $cmd")
        Invoke-Expression ($cmd)
    }
    else
    {
        Write-Host("Error! folder $modeldir\$Model\data does not exist!! check your -model parameter and make sure they are under $modeldir")
    }
}

Write-Host ("Backend started on $cluster!")

if ($startFE.IsPresent)
{
    
    ## start vHub Gateway on some of the nodes: ONENET11,ONENET12,ONENET13,ONENET14,ONENET15
    ##..\..\vHub\vHub.FrontEnd\bin\Debug\vHub.FrontEnd.exe -start -cluster $cluster.inf -log C:\log\VHub\a.log -con -rootdir F:\Src\SkyNet\IMHub\IMHub
    ## start vHub Gateway on local machine: $only
    #Invoke-Expression ("..\..\vHub\vHub.FrontEnd\bin\Debug\vHub.FrontEnd.exe -start -only ""$only"" -rootdir $rootdir")
    $cmd = "..\..\vHub\vHub.FrontEnd\bin\Debug\vHub.FrontEnd.exe -start -cluster C:\OneNet\Cluster\OneNet_YuxHu_11to15.inf -rootdir $rootdir -con" 
    Write-Host ("now starting vHub frontend with cmd: $cmd")
    Invoke-Expression ($cmd)
    Write-Host ("Frontend started on $cluster!")

}


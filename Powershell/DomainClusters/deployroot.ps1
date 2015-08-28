##############################################################################
## Copyright 2013, Microsoft.	All rights reserved  
## Author: Jin Li
## Date: Aug. 2013                                                    
## Copy files to a root folder to be deployed to client
##############################################################################
Invoke-Expression ./config.ps1

write-host $localSrcFolder
write-host $rootSrcFolder

##############################################################################
## Force a build in $localSrcFolder

Push-Location -Path $localSrcFolder

$tmpfile = [System.IO.Path]::GetTempFileName()
& .\build.cmd R > $tmpfile 2>&1

if ($LASTEXITCODE -ne 0)
{
    Pop-Location
    throw "Fail to build! Please check build logs at $localSrcFolder"
}

Pop-Location


##############################################################################
write-host "Additional Deploy" $AdditionalDeploy
Invoke-Expression $AdditionalDeploy

robocopy $localSrcFolder'\bin\Releasex64\Client' c:\OneNet /s /R:0
write-host "Robocopy " $localSrcFolder'\samples\bin\Debugx64' c:\OneNet /s /R:0
robocopy $localSrcFolder'\samples\DistributedKMeans\bin\Releasex64' c:\OneNet /s /R:0
robocopy $localSrcFolder'\samples\DistributedLogAnalysis\bin\Releasex64' c:\OneNet /s /R:0
robocopy $localSrcFolder'\samples\DistributedSort\bin\Releasex64' c:\OneNet /s /R:0
robocopy $localSrcFolder'\samples\DistributedWebCrawler\bin\Releasex64' c:\OneNet /s /R:0
robocopy $localSrcFolder'\samples\DKVCopy\bin\Releasex64' c:\OneNet /s /R:0
robocopy $localSrcFolder'\samples\ImageBrowser\bin\Releasex64' c:\OneNet /s /R:0
robocopy $localSrcFolder'\samples\MonitorService\bin\Releasex64' c:\OneNet /s /R:0
robocopy $localSrcFolder'\bin\Releasex64\Controller' c:\OneNet /s /R:0
remove-item $rootSrcFolder -Force -Recurse
robocopy $localSrcFolder'\bin\Releasex64\Client ' $rootSrcFolder'\bin\Releasex64\Client' /s /mir
robocopy $localSrcFolder'\bin\Releasex64\ClientExt ' $rootSrcFolder'\bin\Releasex64\ClientExt' /s /mir
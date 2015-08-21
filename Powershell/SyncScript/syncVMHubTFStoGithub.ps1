##############################################################################
## sync code for VM Hub from TFS repository to github
## Author:
## 	Jin Li, Partner Research Manager
## Remark:
##	This code should be executed in the directory above Prajna, AppSuite, Hub, Services, 
##############################################################################
param(
    ## Machine name to deploy
    [bool] $codeonly = $true,
    [string] $rootTFS = "..\tfs.p2plab\OneNet\",
    [string] $robocopyFlag = "  ",
    [bool] $toGithub = $true
)

if ( Test-Path -Path $rootTFS )
{
    if ( $toGithub )
    {
        Write-Host "Sync Repository VM Hub (TFS) to Git hub ...."
    }
    else
    {
        Write-Host "Sync Repository Git hub to VM Hub (TFS) ...."
    }

    if ( Test-Path -Path Hub ) 
    {
        copy .\syncVMHubTFStoGithub.ps1 .\Hub\Powershell\SyncScript
        write-host copy .\syncVMHubTFStoGithub.ps1 .\Hub\Powershell\SyncScript
    }

    if ( Test-Path -Path AppSuite )
    {
        if ( $toGithub )
        {
            
            $fromDir = $rootTFS + "VHub\ClientSuite" 
            $toDir = ".\AppSuite\AppSuite.Net"
            write-host robocopy $fromDir $toDir /s $robocopyFlag 
            robocopy $fromDir $toDir *.* /s $robocopyFlag 
        }
    }
    

    if ( Test-Path -Path Services )
    {
        $libFolders = "vHub.RecogService"
        foreach ( $folder in $libFolders )
        {
            if ( $toGithub )
            {
                $fromDir = $rootTFS + "VHub\" + $folder + "\ "
                $toDir = ".\Services\src\ServiceLib\" + $folder + "\ "
                write-host robocopy $fromDir $toDir /s $robocopyFlag 
                robocopy $fromDir $toDir * /s $robocopyFlag 
            }
        }


        $serviceFolders = "PrajnaRecogServer.vHub", "SampleRecogServerCSharp", "SampleRecogServerFSharp", "vHub.DummyRecogServer", "vHub.MonitorBackEnd", "vHub.RecogService"
        foreach ( $folder in $serviceFolders )
        {
            if ( $toGithub )
            {
                $fromDir = $rootTFS + "VHub\" + $folder + "\ "
                $toDir = ".\Services\samples\" + $folder + "\ "
                write-host robocopy $fromDir $toDir /s $robocopyFlag 
                robocopy $fromDir $toDir * /s $robocopyFlag 
            }
        }
    }
}
else
{
    Write-Host "Please move the script to a folder above Prajna repository for it to function"
}

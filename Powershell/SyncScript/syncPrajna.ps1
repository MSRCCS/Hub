##############################################################################
## sync code from Prajna repository to other repository (AppSuite, Hub, Services)
## Author:
## 	Jin Li, Partner Research Manager
## Remark:
##	This code should be executed in the directory above Prajna, AppSuite, Hub, Services, 
##############################################################################
param(
    ## Machine name to deploy
    [bool] $rebuild = $true
)

if ( Test-Path -Path Prajna )
{
    if ($rebuild )
    {
        Write-Host "Build Release code and prepare Nuget packages from Repository Prajna ...."
        Write-Host "Changing directory to Prajna ..."
        Push-Location -Path Prajna
        .\build.cmd R
        .\build.cmd NuGet
        Write-Host "Popping directory ..."
        Pop-Location
    }
    else
    {
        Write-Host "Sync Repository Prajna, assuming build already done ...."
    }

    if ( Test-Path -Path AppSuite ) 
    {
        copy Prajna\src\ServiceLib\BasicServiceData\MonitorServiceData.cs AppSuite\AppSuite.Net\BasicServiceData\MonitorServiceData.cs
        write-host copy Prajna\src\ServiceLib\BasicServiceData\MonitorServiceData.cs AppSuite\AppSuite.Net\BasicServiceData\MonitorServiceData.cs
    }

    if ( Test-Path -Path Services )
    {
        copy Prajna\bin\*.nupkg .\Services\local-packages
        write-host copy Prajna\bin\*.nupkg .\Services\local-packages
        if ( Test-Path -Path .\Services\packages ) 
        {
            remove-item .\Services\packages -Recurse
            Push-Location -Path Services
            .\.paket\paket.exe update
            Pop-Location
        }
        else
        {
            Push-Location -Path Services
            .\.paket\paket.exe install
            Pop-Location
        }
    }

    if ( (Test-Path -Path Services) -And (Test-Path -Path Hub) )
    {
        if ($rebuild )
        {
            Write-Host "Build Release code and prepare Nuget packages from Repository Services ...."
            Write-Host "Changing directory to Services ..."
            Push-Location -Path Services
            .\build.cmd R
            .\build.cmd NuGet
            Write-Host "Popping directory ..."
            Pop-Location
        }
        else
        {
            Write-Host "Sync Repository Services, assuming build already done ...."
        }


        copy Services\bin\*.nupkg .\Hub\local-packages
        write-host copy Services\bin\*.nupkg .\Hub\local-packages

    }

    if ( Test-Path -Path Hub )
    {
        copy syncPrajna.ps1 .\Hub\Powershell\SyncScript\syncPrajna.ps1
        write-host copy syncPrajna.ps1 .\Hub\Powershell\SyncScript\syncPrajna.ps1
        copy Prajna\bin\*.nupkg .\Hub\local-packages
        write-host copy Prajna\bin\*.nupkg .\Hub\local-packages

        if ( Test-Path -Path .\Hub\packages ) 
        {
            remove-item .\Hub\packages -Recurse
            Push-Location -Path Hub
            .\.paket\paket.exe update
            Pop-Location
        }
        else
        {
            Push-Location -Path Hub
            .\.paket\paket.exe install
            Pop-Location
        }
    }
}
else
{
    Write-Host "Please move the script to a folder above Prajna repository for it to function"
}


#$cmd  = ".\startBE_forAzure.ps1 -action start -only imhub-eastus -extraFENode ""-only yuxiao-z840"" -cluster \\yuxiao-z840\OneNet\Cluster\OneNet_21 -restartPrajnaClient"
$cmd  = ".\startBE_forAzure.ps1 -action start -only imhub-eastus -cluster \\yuxiao-z840\OneNet\Cluster\OneNet_21_30 -restartPrajnaClient"

while($true)
{
    Write-Verbose $cmd
    Invoke-Expression ($cmd)
    Sleep 120
}

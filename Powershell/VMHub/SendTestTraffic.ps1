
#######################################################################################
## Copyright 2015, Microsoft.	All rights reserved    
#######################################################################################

<#
.SYNOPSIS
    utilities for sending test traffic to Hub frontend
.DESCRIPTION
    start the testing traffic to hit some specific classifiers registered to the Hub
.PARAMETER mode
    single: send a single test image file (specified by -imageFile parameter)
    batch:  send batch of test image files (specified by -imageDir parameter)
    loop:   send batch of test image files (specified by -imageDir parameter), and then repeat, until the script is stopped manually by Ctrl+C
.PARAMETER guid
    dummy: The guid of the target classifier, by default is the "dummy" classifier, which always return "dummy:0.99" in 500ms.
    random: send the test traffic to random classifiers available in backend
    GUID: send the test traffic to a specific classifier, denoted by its GUID
.PARAMETER vHub
    the gateway server to hit, default is http://imhub-westus.cloudapp.net
.PARAMETER imageFile
    The image file used to test the classifer, default is \\yuxiao-z840\OneNet\data\images\sample-big.jpg
.PARAMETER imageDir
    The folder containing the images files for batch test, default is \\yuxiao-z840\OneNet\data\images\Office-bigset, which contains ~1000 images
.PARAMETER verboseLevel 
    verbose level to show the debug information, default is 4
.EXAMPLE    
    .\SendTestTraffic.ps1
    send one test image to http://imhub-westus.cloudapp.net, hit the dummy classifier
.EXAMPLE
    .\SendTestTraffic.ps1 -mode batch
    send ~1000 images to http://imhub-westus.cloudapp.net, hit the dummy classifier, typically this will take about 1000*600ms = ~10 min to finish
.EXAMPLE    
    .\SendTestTraffic.ps1 -mode batch -guid random
    send ~1000 images to http://imhub-westus.cloudapp.net, hit random classifiers available
.EXAMPLE    
    .\SendTestTraffic.ps1 -mode loop -guid random
    send images to http://imhub-westus.cloudapp.net, hit random classifiers available, and then repeat, until Ctrl-C
.NOTES
    Author: Yuxiao Hu (yuxhu@microsoft.com)
    Date:   June 3, 2015    
#>

param(
        [ValidateSet("batch", "single", "loop")][string] $mode = "single",
        [string]$guid = "dummy",
        [string]$vHub = "http://imhub-westus.cloudapp.net",
        [ValidateScript({Test-Path $_ -PathType 'Container'})][string] $imageDir = "\\yuxiao-z840\OneNet\data\images\Office-bigset",
        [ValidateScript({Test-Path $_ -PathType 'Leaf'})][string] $imageFile = "\\yuxiao-z840\OneNet\data\images\sample-big.jpg",
        [ValidateRange(0,6)] [Int] $verboseLevel = 4
)


if ($guid.ToLower().CompareTo("dummy") -eq 0)
{
    $guid = "ca99a8b9-0de2-188b-6c14-747619a2ada8"
}
elseif ($guid.ToLower().CompareTo("random") -eq 0) 
{
    $guid = "Random"
}


if ($mode.ToLower().CompareTo("batch")  -eq  0 )
{
    $cmd = "..\vHub.Clients\vHub.ImageProcessing\bin\Debug\vHub.ImageProcessing.exe -cmd Batch -Vhub $vHub -rootdir $imageDir -serviceGUID $guid -verbose $verboseLevel"
    Write-Verbose $cmd
    Invoke-Expression ($cmd)
}

if ($mode.ToLower().CompareTo("batchAsync")  -eq  0 )
{
    $cmd = "..\vHub.Clients\vHub.ImageProcessing\bin\Debug\vHub.ImageProcessing.exe -cmd BatchAsync -Vhub $vHub -rootdir $imageDir -serviceGUID $guid -verbose $verboseLevel"
    Write-Verbose $cmd
    Invoke-Expression ($cmd)
}


if ($mode.ToLower().CompareTo("single")  -eq  0)
{
    $cmd = "..\vHub.Clients\vHub.ImageProcessing\bin\Debug\vHub.ImageProcessing.exe -cmd Recog -Vhub $vHub -file $imageFile  -serviceGUID $guid -verbose $verboseLevel"
    Write-Verbose $cmd
    Invoke-Expression ($cmd)
}

if ($mode.ToLower().CompareTo("loop")  -eq  0)
{
    $cmd = "..\vHub.Clients\vHub.ImageProcessing\bin\Debug\vHub.ImageProcessing.exe -cmd Batch -Vhub $vHub -rootdir $imageDir -serviceGUID $guid -verbose $verboseLevel"
    while($true)
    {
        Write-Verbose $cmd
        Invoke-Expression ($cmd)
    }
}

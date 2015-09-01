Push-Location ..\..\Powershell\DomainClusters
.\restartclient.ps1
Pop-Location
..\..\vHub\vHub.FrontEnd\bin\Debug\vHub.FrontEnd.exe -start -cluster C:\OneNet\Cluster\OneNet_JinL_6to10.inf -con
..\..\vHub\SampleRecogServerFSharp\bin\Debug\SampleRecogServerFSharp.exe -start -cluster C:\OneNet\Cluster\OneNet_JinL_6to10.inf -only jinl4 -only OneNet06 -only OneNet07 -only OneNet08 -only OneNet09 -only OneNet10 -con
..\..\vHub\PrajnaRecogServer.vHub\bin\Debug\PrajnaRecogServer.vHub.exe -start -cluster C:\OneNet\Cluster\OneNet_JinL_6to10.inf -only jinl4 -only OneNet06 -only OneNet07 -only OneNet08 -only OneNet09 -only OneNet10 -con


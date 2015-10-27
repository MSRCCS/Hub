This sample demonstrates how to wrap the MLLR sample into an Azure worker role

1. Requirements

The development machine needs to have
* Visual Studio 2015
* Azure SDK 2.7 or later 
* Azure Powershell

2. Azure Worker Role

* Read https://azure.microsoft.com/en-us/documentation/articles/cloud-services-dotnet-get-started/, which shows the basics on how to create an
  Azure Cloud Service, which can include web roles, and worker roles.
* In this sample, the cloud service "MLLRAzureWorker" contains a single worker role called "WorkerRole". The code for the 
  worker role is located at samples\mllrazureworker\workerrole
* The worker role simply wraps the code from MLLR sample with some small modifications. 
  > The code is mainly at samples\mllrazureworker\workerrole\MLLR.cs
  > And is invoked from samples\mllrazureworker\workerrole\wokerrole.cs

3. Azure Diagnostics

* Azure Diagnostics is enabled following the instruction here: https://azure.microsoft.com/en-us/documentation/articles/cloud-services-dotnet-diagnostics/
* In samples\mllrazureworker\workerrole\logger.cs, 
  > a "PrajnaEventSourceWriter" is created by inheriting "EventSource"
  > a "LoggerProvider" implements the interface "Prajna.Tools.LoggerProvider" using ""PrajnaEventSourceWriter"
* The configuration file is at samples\mllrazureworker\workerrole\wadconfig.xml, which specifies 
  "PrajnaEventSourceWriter" as an "EtwEventSourceProvider" and defines only one table "PrajnaLogs"
* In samples\mllrazureworker\workerrole\workerrole.cs, at OnStart, it installs "LoggerProvider" for Prajna
* As a result, all Prajna logs are injected into the Azure Diagnostics table "PrajnaLogs". For user level logging, they
  can either use Prajna's Logging API or "PrajnaEventSourceWriter" directly.

4. Deployment

* In Visual Studio, select the "MLLRAzureWorker" cloud project, right click, select "Publish". 
  A deployment wizard appears to guide the deployment process.
  * User will be asked to create a new cloud service or using an existing one
  * User will be asked to provide a storage account. It can be either a new one or an existing one.
* Once the deployment completes, proceed to enable the diagnostics for the worker role (this only needs to be done once)
  * Use samples\MLLRAzureWorker\WorkerRole\InstallDiagnostics.ps1
  * Fill in values for 
    > $storage_name : a storage account's name (it can be the same or a different one from the storage account used in deployment)
    > $key : the storage account's key
    > $config_path : the absolute path to samples\mllrazureworker\workerrole\wadconfig.xml
    > $service_name : the name of the cloud service deployed
    > $worker_role_name : the name of the worker role. In this project it is called "WorkerRole"
  * Execute the script with Azure Powershell (assume the Azure Powershell has been setup properly to use user's subscription)
  * Once the script executes successfully, the diagnostics should be enabled.
  * After a while, can start to check the log
    > The log is stored in the storage account specified in the script
    > They are in a table called either "PrajnaLogs" or "WADPrajnaLogs"



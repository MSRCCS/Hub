(*---------------------------------------------------------------------------
    Copyright 2014 Microsoft

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

    Unless required by applicable law or agreed to in writing, software
    distributed under the License is distributed on an "AS IS" BASIS,
    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    See the License for the specific language governing permissions and
    limitations under the License.                                                     

    File: 
        Launch.fs
  
    Description: 
        Launch VHub Gateway. 

    Author:																	
        Jin Li, Principal Researcher
        Microsoft Research, One Microsoft Way
        Email: jinl at microsoft dot com
    Date:
        Nov. 2014
    
 ---------------------------------------------------------------------------*)
open System
open System.Net
open System.IO
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open System.ServiceModel
open System.ServiceModel.Description
open System.ServiceModel.Web
open System.Web

open Prajna.Tools
open Prajna.Tools.StringTools
open Prajna.Tools.FSharp


open Prajna.Core
open Prajna.Service.ServiceEndpoint
open Prajna.Service.Gateway
open Prajna.Service.CoreServices
open Prajna.Api.FSharp
open Prajna.Api.FSharp

open Prajna.WCFTools
open VMHub.Data
open VMHub.ServiceEndpoint
open VMHub.Gateway
open VMHub.GatewayWebService

open Prajna.Service.FSharp


let Usage = "
    Usage: Launch an intance of PrajnaRecogServer. The application is intended to run on any machine. It will home in to an image recognition hub gateway for service\n\
    Command line arguments: \n\
        -start       Launch a Prajna Recognition service in cluster\n\
        -stop        Stop existing Prajna Recognition service in cluster\n\
        -exe         Execute in exe mode \n\
        -cluster     CLUSTER_NAME    Name of the cluster for service launch \n\
        -port        PORT            Port used for service \n\
        -node        NODE_Name       Launch the recognizer on the node of the cluster only (note that the cluster parameter still need to be provided \n\
        -rootdir     Root_Directory  include data\Models & dependencies \n\
        -modeldir    Model_Directory Directory of recognition model\n\
        -depdir      Dependencies dir Directory of DLLs that the project will use\n\
        -model       MODEL_NAME      Model Name \n\
        -gateway     SERVERURI       ServerUri\n\
        -only        SERVERURI       Only register with this server, disregard default. \n\
        -saveimage   DIRECTORY       Directory where recognized image is saved \n\
        -timeout     Seconds         Timeout at backend (in seconds ) \n\
        -monitor     Gateways        Gateways to be monitored \n\
        -table       TABLE_FILE      A table of gateway ports (e.g., RDP port) that are used to monitor the life of gateay VM \n\
    "


// A native recognization client
//
let recognizeClusterTask (serviceInfo:VHubRequestInfo) ( name:string, imgBuf:byte[] ) =
    let t1 = (DateTime.UtcNow.Ticks)
    let func = ContractStore.ImportFunctionTask<VHubExtendedRequest, RecogReply>(null, VHubWebHelper.RecogObjectContractName )
    let recogObject = RecogRequest( Data = imgBuf, AuxData = null )
    let req = VHubExtendedRequest( Info = serviceInfo, RequestObject=recogObject ) 
    let contFunc ( ta: Task<RecogReply> ) =
        let result = 
            if ta.IsCompleted then 
                let reply = ta.Result
                reply.Description + "(" + reply.PerfInformation + ")"
            else
                sprintf "Recog task failed with exception %A" ta.Exception
        let t2 = (DateTime.UtcNow.Ticks)
        name, ( result, imgBuf.Length, t2-t1 )
    let ta = func( req ) 
    ta.ContinueWith( contFunc )

type CrossDomainServiceAuthorizationManager() =
    inherit ServiceAuthorizationManager()
    override this.CheckAccessCore(operationContext: OperationContext) = 
        // https://praneeth4victory.wordpress.com/2011/09/29/405-method-not-allowed/
        let prop = new Channels.HttpResponseMessageProperty()
        prop.Headers.Add("Access-Control-Allow-Origin", "*");
        prop.Headers.Add("Access-Control-Allow-Method", "OPTIONS, POST, GET");
        prop.Headers.Add("Access-Control-Allow-Headers", "Content-type, Accept");
        operationContext.OutgoingMessageProperties.Add(Channels.HttpResponseMessageProperty.Name, prop);
        true;

[<EntryPoint>]
let main argv = 
    
    let logFile = sprintf @"c:\Log\VHub\Launch_%s.log" (VersionToString( (DateTime.UtcNow) ))
    let inputargs =  Array.append [| "-log"; logFile; "-verbose"; ( int LogLevel.MildVerbose).ToString() |] argv 
    let orgargs = Array.copy inputargs
    let parse = ArgumentParser(orgargs)
    
    let PrajnaClusterFile = parse.ParseString( "-cluster", "" )
    let usePort = parse.ParseInt( "-port", VHubSetting.RegisterServicePort )
    let nodeName = parse.ParseString( "-node", "" )
    let curfname = Process.GetCurrentProcess().MainModule.FileName
    let defaultRootdir = 
        try
            let curdir = Directory.GetParent( curfname ).FullName
            let upperdir = Directory.GetParent( curdir ).FullName
            let upper2dir = Directory.GetParent( upperdir ).FullName                        
            upper2dir
        with
        | e -> 
            null
    Logger.LogF( LogLevel.MildVerbose, ( fun _ -> sprintf "Default Root Directory === %s" defaultRootdir ))
    let rootdir = parse.ParseString( "-rootdir", defaultRootdir )
    let bStart = parse.ParseBoolean( "-start", false )
    let bStop = parse.ParseBoolean( "-stop", false )
    let recogDKV = parse.ParseString( "-recog", null )
    let bExe = parse.ParseBoolean( "-exe", true )
    let gatewayServers = parse.ParseStrings( "-gateway", [||] )
    let monitorServers = parse.ParseStrings( "-monitor", [|"imhubr.trafficmanager.net"|] )
    let onlyServers = parse.ParseStrings( "-only", [||] )
    let monitorURL = parse.ParseString( "-URL", "web/info" )
    let monitorTable = parse.ParseString( "-table", "..\..\..\..\samples\MonitorService\monitor_port.txt" )
    let monitorPort = 80
    let serviceName = "VHubFrontEnd"
    let nTimeout = parse.ParseInt( "-timeout", 0 )

    Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Program %s started  ... " (Process.GetCurrentProcess().MainModule.FileName) ) )
    Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Execution param %A" inputargs ))
    
    let bAllParsed = parse.AllParsed Usage
    if bAllParsed then 
        Cluster.Start( null, PrajnaClusterFile )
        if not (StringTools.IsNullOrEmpty( nodeName )) then 
            let cluster = Cluster.GetCurrent().GetSingleNodeCluster( nodeName ) 
            if Utils.IsNull cluster then 
                failwith (sprintf "Can't find node %s in cluster %s" nodeName PrajnaClusterFile)
            else
                Cluster.Current <- Some cluster
        
        if bExe then 
            JobDependencies.DefaultTypeOfJobMask <- JobTaskKind.ApplicationMask
        let curJob = JobDependencies.setCurrentJob "VHubFrontEnd"
        if bStart then 
            // JobDependencies.DefaultTypeOfJobMask <- JobTaskKind.ApplicationMask
            // add other file dependencies
            // allow mapping local to different location in remote in case user specifies different model files
            let mutable bNullService = StringTools.IsNullOrEmpty( rootdir )
            try 
                if bNullService then 
                    bNullService <- true
                    CacheService.Start()
                else
                    let exeName = System.Reflection.Assembly.GetExecutingAssembly().Location
                    let exePath = Path.GetDirectoryName( exeName )
                    let txts = curJob.AddDataDirectory( rootdir, VHubSetting.FilenameDefaultProviderList, SearchOption.TopDirectoryOnly )
                    let webs = curJob.AddDataDirectoryWithPrefix( null, Path.Combine( rootdir, VHubSetting.WebRootName ), VHubSetting.WebRootName, "*", SearchOption.AllDirectories )
                    let monitorParam = MonitorWebServiceParam( monitorURL, 20 )
                    let startParam = VHubFrontendStartParam()
                    // Only gateway behind traffic manager is monitored. 
                    for monitorServer in monitorServers do 
                        monitorParam.ServerInfo.AddServerBehindTrafficManager( monitorServer, monitorPort)
                    if not (StringTools.IsNullOrEmpty monitorTable) then 
                        let tableFile = 
                            if Path.IsPathRooted( monitorTable ) then 
                                monitorTable
                            else
                                let curPath = Path.GetDirectoryName( Process.GetCurrentProcess().MainModule.FileName )
                                Path.Combine( curPath, monitorTable )                        
                        if File.Exists( tableFile ) then 
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Load table %s" tableFile ) )
                            monitorParam.LoadServerTable( tableFile )   

                    if nTimeout > 0 then 
                        startParam.TimeOutRequestInMilliSecond <- nTimeout * 1000 
                        Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Set timeout at Frontend to %d seconds" nTimeout ) )

                    let onStartFrontEnd param = 
                        try 
                            let webService = typeof<VHubFrontEndWebService>
                            Logger.LogF( LogLevel.MildVerbose, ( fun _ -> sprintf "Launch VHubFrontEndWebService Service ... " ))
                            let monitorUri = VHubSetting.GenerateMonitorServiceUrl 
                            Logger.LogF( LogLevel.MildVerbose, ( fun _ -> sprintf "Bind Monitoring Service to %s ... " monitorUri ))
                            let host1 = new WebServiceHost( webService, ([| Uri(monitorUri) |] ) )
                            // Increased allowed receive message size
                            let binding = new WebHttpBinding();
                            binding.MaxReceivedMessageSize <- (1L <<< 20);
                            binding.MaxBufferSize <- (1 <<< 20);
                            let ep1 = host1.AddServiceEndpoint( typeof<VHubFrontEndWebService>, binding, "" )
                            ep1.Behaviors.Add( new WebHttpBehavior() )
                            
                            let smb1 = new ServiceMetadataBehavior()
                            smb1.HttpGetEnabled <- true
                            smb1.MetadataExporter.PolicyVersion <- PolicyVersion.Policy15
                            host1.Description.Behaviors.Add( smb1 )

                            // enable cross origin javascript access
                            // http://stackoverflow.com/questions/6308394/wcf-web-api-restful-is-not-allowed-by-access-control-allow-origin
                            host1.Authorization.ServiceAuthorizationManager <- new CrossDomainServiceAuthorizationManager()

                            host1.Open()
                            /// holding reference
                            VHubWebHelper.SvcHost <- host1 
                            VHubWebHelper.SvcEndPoint <- ep1
                            VHubWebHelper.SvcBehavior <- smb1 
                            true
                        with 
                        | e -> 
                            Logger.LogF( LogLevel.Error, ( fun _ -> sprintf "Start web service fail with exception %A" e ))
                            false

                    let onStopFrontEnd() = 
                        Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Stop Web Instance has been called ........................ " ))
                        VHubWebHelper.SvcHost.Close()
                        VHubWebHelper.ReleaseAll()

                    let VHubLaunches() = 
                        let app = VHubWebHelper.Init() 
                        app.OnStartFrontEnd.Add( FrontEndOnStartFunction( onStartFrontEnd  ) ) 
                        app.OnStopFrontEnd.Add( UnitAction( onStopFrontEnd ) )
                        app 

                    let cur = Cluster.GetCurrent()
                        
                    /// Any Gateway, considered as traffic manager, needs repeated DNS resolve
                        
                    if Utils.IsNull cur then 
                        Directory.SetCurrentDirectory( rootdir )
                        RemoteInstance.StartLocal( MonitorWebServiceParam.ServiceName, monitorParam, fun _ -> MonitorInstance() )
                        RemoteInstance.StartLocal( serviceName, startParam, VHubLaunches)
                        while RemoteInstance.IsRunningLocal( serviceName ) do 
                            if Console.KeyAvailable then 
                                let cki = Console.ReadKey( true ) 
                                if cki.Key = ConsoleKey.Enter then 
                                    RemoteInstance.StopLocal( MonitorWebServiceParam.ServiceName )
                                    RemoteInstance.StopLocal( serviceName )
                                else
                                    Thread.Sleep(10)
                    else
                        RemoteInstance.Start( MonitorWebServiceParam.ServiceName, monitorParam, ( fun _ -> MonitorInstance() ) )
                        RemoteInstance.Start( serviceName, startParam, VHubLaunches )
            with 
            | e -> 
                Logger.Log( LogLevel.Info, ( sprintf "Recognizer fail to load because of exception %A. " e ))
                bNullService <- true
                CacheService.Stop()
              
            if bNullService then 
                Logger.Log( LogLevel.Info, ( sprintf "A null service is launched for testing. " ))
        elif bStop then 
            RemoteInstance.Stop( MonitorWebServiceParam.ServiceName )
            RemoteInstance.Stop( serviceName )
        elif not (StringTools.IsNullOrEmpty recogDKV) then 
            let t1 = (DateTime.UtcNow)
            let curDKV = DSet<string*byte[]>( Name = recogDKV ) |> DSet.loadSource
            let serviceInfo = VHubRequestInfo( ServiceID=VHubBackEndInstance<_>.ServiceIDForDomain("#test") )
            let recogDKV = curDKV |> DSet.parallelMap ( recognizeClusterTask serviceInfo )
            // add search pattern first. 
            let recogResult = recogDKV.ToSeq() 
            let numFiles = ref 0
            let total = ref 0L
            let totalRecogMS = ref 0L
            recogResult 
            |> Seq.iter( fun ( name, tuple ) -> 
                                let recogResult, orgSize, recogTicks = tuple 
                                let recogMs = recogTicks / TimeSpan.TicksPerMillisecond
                                numFiles := !numFiles + 1
                                total := !total + int64 orgSize
                                totalRecogMS := !totalRecogMS + int64 recogMs
                                Logger.Log( LogLevel.Info, ( sprintf "Image %s is recognized as %s (org %dB, use %d ms) " name recogResult orgSize recogMs))
                        )
            let t2 = (DateTime.UtcNow)
            let elapse = t2.Subtract(t1)
            Logger.Log( LogLevel.Info, ( sprintf "Processed %d Files with total %d MB in %f sec, throughput = %f MB/s, avg recogtime = %d ms" 
                                                   !numFiles (!total>>>20) elapse.TotalSeconds ((float !total)/elapse.TotalSeconds/1000000.) 
                                                   (if !numFiles>0 then (!totalRecogMS/(int64 !numFiles)) else -1L ) ))
            ()
    0

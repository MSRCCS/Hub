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
        PrajnaRecogServer.fs
  
    Description: 
        An instance of Prajna Recognition Server. For fault tolerance, we will expect multiple Recognition Servers to be launched. Each recognition server
    will cross register with all gateway services. The service will be available as long as at least one gateway server and one recognition server is online. 

    Author:																	
        Jin Li, Principal Researcher
        Microsoft Research, One Microsoft Way
        Email: jinl at microsoft dot com
    Date:
        Nov. 2014
    
 ---------------------------------------------------------------------------*)
open Prajna.Tools
open Prajna.Tools.StringTools
open Prajna.Tools.FSharp
open Prajna.Service.FSharp


open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Collections.Generic
open System.Collections.Concurrent

open Prajna.Core
open Prajna.Api.FSharp
open Prajna.Api.FSharp
open Prajna.Service.ServiceEndpoint
open vHub.Data
open vHub.RecogService

open CaffeWrapper

let Usage = "
    Usage: Launch an intance of PrajnaRecogServer. The application is intended to run on any machine. It will home in to an image recognition hub gateway for service\n\
    Command line arguments: \n\
        -start       Launch a Prajna Recognition service in cluster\n\
        -stop        Stop existing Prajna Recognition service in cluster\n\
        -exe         Execute in exe mode \n\
        -progname    Name of the Prajna program container \n\
        -instance    #_OF_INSTANCES  Number of insances to start on each node \n\ 
        -cluster     CLUSTER_NAME    Name of the cluster for service launch \n\
        -port        PORT            Port used for service \n\
        -node        NODE_Name       Launch the recognizer on the node of the cluster only (note that the cluster parameter still need to be provided \n\
        -rootdir     Root_Directory  include data\Models & dependencies \n\
        -modeldir    Model_Directory Directory of recognition model\n\
        -depdir      Dependencies dir Directory of DLLs that the project will use\n\
        -models      MODEL_NAME      Model Name \n\
        -gateway     SERVERURI       ServerUri\n\
        -only        SERVERURI       Only register with this server, disregard default. \n\
        -saveimage   DIRECTORY       Directory where recognized image is saved \n\
        -statis      Seconds         Get backend cluster statistics. The value of statis is the time interval (in second) that the statistics is quered. \n\

    "

let queryFunc _ = 
    VHubRecogResultHelper.FixedClassificationResult( "Unknown Object", "I don't recognize the object" )

type CaffeFilePredictor( saveImgDir: string, predictorModelDir: string, myCaffe: CaffePredictor) = 
    static member val CachedFile = ConcurrentDictionary<_,bool>(StringComparer.OrdinalIgnoreCase) with get
    static member val DirectoryCreated = ref 0 with get
    static member val EVDirectoryCreatedEvent = new ManualResetEvent(false) with get
    member x.ExistDirectory( filename ) = 
        // Confirm directory available 
        if Interlocked.CompareExchange( CaffeFilePredictor.DirectoryCreated, 1, 0)=0 then 
            // Create Directory 
            let dir = Path.GetDirectoryName( filename ) 
            FileTools.DirectoryInfoCreateIfNotExists dir |> ignore
            CaffeFilePredictor.EVDirectoryCreatedEvent.Set() |> ignore
        CaffeFilePredictor.EVDirectoryCreatedEvent.WaitOne() |> ignore
    member x.PredFunc (  reqID:Guid, timeBudget:int, req:RecogRequest )  = 
        /// Save image to file, and then apply recognizer
        let imgBuf = req.Data
        let imgType = System.Text.Encoding.UTF8.GetBytes( "jpg" )
        let imgID = BufferCache.HashBufferAndType( imgBuf, imgType ) 
        let imgFileName = imgID.ToString() + ".jpg"
        let filename = if StringTools.IsNullOrEmpty( saveImgDir) then imgFileName else Path.Combine( saveImgDir, imgFileName ) 
        let writeFile imgBuf filename =
            x.ExistDirectory filename 
            FileTools.WriteBytesToFileConcurrent filename imgBuf 
            true    
        CaffeFilePredictor.CachedFile.GetOrAdd( filename, writeFile imgBuf ) |> ignore
        let curWorkingDir = Directory.GetCurrentDirectory()
        let filename = Path.Combine(curWorkingDir, filename)
        Directory.SetCurrentDirectory(predictorModelDir)
        let resultString = myCaffe.Predict(filename, Math.Min(myCaffe.GetCateNum(), 5))
        Directory.SetCurrentDirectory(curWorkingDir)
        VHubRecogResultHelper.FixedClassificationResult( resultString, resultString ) 

let depDirName = "dependencies"
let modelDirName = "models"

/// <summary>
/// Using VHub, the programmer need to define two classes, the instance class, and the start parameter class 
/// The instance class is instantiated at remote machine, it is not serialized.
/// </summary>
type PrajnaRecogInstance(models:string[], saveimgdir: string ) as x = 
    inherit VHubBackEndInstance<VHubBackendStartParam>("Prajna")
    do 
        x.OnStartBackEnd.Add( new BackEndOnStartFunction<VHubBackendStartParam>( x.InitializePrajna) )
    let mutable appInfo = Unchecked.defaultof<_>
    let mutable bSuccessInitialized = false
    /// Programmer will need to extend BackEndInstance class to fill in OnStartBackEnd. The jobs of OnStartBackEnd are: 
    /// 1. fill in ServiceCollection entries. Note that N parallel thread will be running the Run() operation. However, OnStartBackEnd are called only once.  
    /// 2. fill in BufferCache.Current on CacheableBuffer (constant) that we will expect to store at the server side. 
    ///     Both 1. 2 get done when RegisterClassifier
    /// 3. fill in MoreParseFunc, if you need to extend beyond standard message exchanged between BackEnd/FrontEnd
    ///         Please make sure not to use reserved command (see list in the description of the class BackEndInstance )
    ///         Network health and message integrity check will be enforced. So when you send a new message to the FrontEnd, please use:
    ///             health.WriteHeader (ms)
    ///             ... your own message ...
    ///             health.WriteEndMark (ms )
    /// 4. Setting TimeOutRequestInMilliSecond, if need
    /// 5. Function in EncodeServiceCollectionAction will be called to pack all service collection into a stream to be sent to the front end. 
    member x.InitializePrajna( param ) = 
        try
                let depdir = depDirName
                let modeldir = Path.Combine("..", modelDirName)
                let mutable bExecute = false
                /// <remarks>
                /// To implement your own image recognizer, please obtain a connection Guid by contacting jinl@microsoft.com
                /// </remarks> 
                x.RegisterAppInfo( Guid("5F2E5F01-D5A1-4CF3-B198-BC6980819208"), "0.0.0.1" )
                /// <remark>
                /// Initialize Recognizer here. 
                /// </remark>

                if true then 
                    // Try to register all models 
                    let dirInfo = DirectoryInfo( modeldir ) 

                    let load_models = 
                        if Utils.IsNull models || models.Length = 0 then 
                            let find_models = dirInfo.GetDirectories() |> Array.map ( fun dir -> dir.Name )
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Find in directory %s the following models %A" modeldir find_models ))
                            find_models
                        else
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Load from directory %s the following specified models %A " modeldir models ))
                            models

                    for model in load_models do
                        let myCaffe = new CaffePredictor();
                        let net_proto = Path.Combine( dirInfo.FullName, model, model + ".caffenet.prototxt" )
                        let trained_net = Path.Combine( dirInfo.FullName, model, model + ".caffemodel" )
                        let label_map = Path.Combine( dirInfo.FullName, model, model + ".Label.txt" )
                        let modelImage = Path.Combine( dirInfo.FullName, model, "Test.jpg" )
                        let mutable bAllExist = true
                        if not(File.Exists( net_proto )) then 
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Model %s, Can't find net_proto : %s" model net_proto ))
                            bAllExist <- false

                        if not(File.Exists( trained_net )) then 
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Model %s, Can't find trained_net : %s" model trained_net ))
                            bAllExist <- false

                        if not(File.Exists( label_map )) then 
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Model %s, Can't find label_map : %s" model label_map ))
                            bAllExist <- false

                        if not(File.Exists( modelImage )) then 
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Model %s, Can't find model_image : %s" model modelImage ))
                            bAllExist <- false
                            
                        if bAllExist then 
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Load model net_proto : %s, trained_net %s, label_map %s, representative image %s" 
                                                                               net_proto trained_net label_map modelImage ))

                            // initialize the detector
                            let curWorkingDir = Directory.GetCurrentDirectory()
                            let predictorModelDir = Path.Combine(dirInfo.FullName, model)
                            Directory.SetCurrentDirectory(predictorModelDir)
                            myCaffe.Init(net_proto, trained_net, label_map, 0)
                            Directory.SetCurrentDirectory(curWorkingDir)
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Model %s loaded, classifier initialized " 
                                                                               model ))
                            let recogClient = CaffeFilePredictor( saveimgdir, predictorModelDir, myCaffe )
                            /// To implement your own image recognizer, please register each classifier with a domain, an image, and a recognizer function. 
                            /// </remarks> 
                            x.RegisterClassifier( "#"+model, modelImage, 100, recogClient.PredFunc ) |> ignore
                            Logger.Log( LogLevel.Info, ("classifier registered"))
                    bSuccessInitialized <- true 

                bSuccessInitialized
        with
        | e -> 
            Logger.LogF( LogLevel.Error, ( fun _ -> sprintf "!!! Unexpected exception !!! Message: %s, %A" e.Message e ))
            bSuccessInitialized <- false            
            bSuccessInitialized

//and PrajnaServiceParam(models, saveimgdir) as x = 
//    inherit VHubBackendStartParam()
//    do 
//        // Important: If this function is not set, nothing is going to run 
//        x.NewInstanceFunc <- fun _ -> PrajnaRecogInstance( models, saveimgdir) :> WorkerRoleInstance
//


[<EntryPoint>]
let main argv = 
    let logFile = sprintf @"c:\Log\ImHub\PrajnaRecogServer_%s.log" (VersionToString( (DateTime.UtcNow) ))
    let inputargs =  Array.append [| "-log"; logFile; "-verbose"; ( int LogLevel.MildVerbose).ToString()|] argv 
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
    let defaultModelDir = if Utils.IsNull rootdir then null else Path.Combine( rootdir, modelDirName)
    let modeldir = parse.ParseString( "-modeldir", defaultModelDir )
    let defaultDepDir = if Utils.IsNull rootdir then null else Path.Combine( rootdir, depDirName )
    let depdir = parse.ParseString( "-depdir", defaultDepDir )

    let defaultModel = 
        let modelSubDirs = Directory.EnumerateDirectories(modeldir, "*", SearchOption.TopDirectoryOnly)
        let subDirs = modelSubDirs |> Seq.toArray |> Array.map (fun t -> Path.GetFileName t)
        match subDirs.Length with
            | 0 -> 
                Logger.LogF( LogLevel.Error, ( fun _ -> sprintf "There is no sub folder under %s. No models can be loaded" defaultModelDir))
                [||]
            | 1 ->
                subDirs
            | _ -> 
                Logger.LogF( LogLevel.Warning, ( fun _ -> sprintf "There are more than one sub folders under %s: %A, will use the first one as instance name: %s" defaultModelDir subDirs subDirs.[0]))
                subDirs

    let models = parse.ParseStrings( "-model", defaultModel )
    let bStart = parse.ParseBoolean( "-start", false )
    let bStop = parse.ParseBoolean( "-stop", false )
    let nStatistics = parse.ParseInt( "-statis", 0 )
    let bExe = parse.ParseBoolean( "-exe", true )
    let saveimagedir = parse.ParseString( "-saveimage", @"." )
    let gatewayServers = parse.ParseStrings( "-gateway", [||] )
    let onlyServers = parse.ParseStrings( "-only", [||] )
    let progName = parse.ParseString( "-progname", "PrajnaRecognitionService" )
    let instanceNum = parse.ParseInt( "-instance", 0 )
    let serviceName = "PrajnaRecogEngine"

    Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Program %s started  ... " (Process.GetCurrentProcess().MainModule.FileName) ) )
    Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Execution param %A" inputargs ))

    let enterKey() =     
        if Console.KeyAvailable then 
            let cki = Console.ReadKey( true ) 
            let bEnter = ( cki.Key = ConsoleKey.Enter )
            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Pressed Key %A, Enter is %A" cki.Key bEnter ))
            bEnter
        else
            false
            

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
        // getserviceName for each instance
            
        if bStart then 
            // JobDependencies.DefaultTypeOfJobMask <- JobTaskKind.ApplicationMask
            // add other file dependencies
            // allow mapping local to different location in remote in case user specifies different model files
            let mutable bNullService = Utils.IsNull modeldir || Utils.IsNull depdir 
            try 
                    let InstanceName = if (models.Length > 0 ) then models.[0] else defaultModel.[0]
                    let pgName = progName + "_" + InstanceName + "_" + instanceNum.ToString()
                    let svName = serviceName + "_" + InstanceName + "_" + instanceNum.ToString()
                    let curJob = JobDependencies.setCurrentJob pgName
                    if bNullService then 
                        bNullService <- true
                        CacheService.Start()
                    else
                        let exeName = System.Reflection.Assembly.GetExecutingAssembly().Location
                        let exePath = Path.GetDirectoryName( exeName )
                        
                        curJob.EnvVars.Add(DeploymentSettings.EnvStringSetJobDirectory, depDirName )
                        for m in models do
                            curJob.AddDataDirectoryWithPrefix( null, Path.Combine ( modeldir, m )  , Path.Combine( modelDirName, m), "*", SearchOption.AllDirectories ) |> ignore
                        let dlls = curJob.AddDataDirectoryWithPrefix( null, depdir, depDirName, "*", SearchOption.AllDirectories )
    //                    let managedDlls = curJob.AddDataDirectoryWithPrefix( null, exePath, "dependencies", "*", SearchOption.AllDirectories )
                        let startParam = VHubBackendStartParam()
                        /// Any Gateway, considered as traffic manager, needs repeated DNS resolve
                        for gatewayServer in gatewayServers do 
                            if not (StringTools.IsNullOrEmpty( gatewayServer)) then 
                                startParam.AddOneTrafficManager( gatewayServer, usePort  )
                        /// Single resolve server
                        for onlyServer in onlyServers do 
                            if not (StringTools.IsNullOrEmpty( onlyServer)) then 
                                startParam.AddOneServer( onlyServer, usePort  )

                        let curCluster = Cluster.GetCurrent()
                        if Utils.IsNull curCluster then 
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Start a local recognizer at directory %s, press ENTER to terminate the recognizer" depdir ))
                            Directory.SetCurrentDirectory( depdir )
                            RemoteInstance.StartLocal( serviceName, startParam, (fun _ -> PrajnaRecogInstance( models, saveimagedir ) ) )
                            while RemoteInstance.IsRunningLocal(serviceName) do 
                                if enterKey() then 
                                        RemoteInstance.StopLocal(serviceName)
                                    else
                                        Thread.Sleep(10)
                        else
                            RemoteInstance.Start( svName, startParam, fun _ -> PrajnaRecogInstance( models, saveimagedir ) )
                with 
                | e -> 
                    Logger.Log( LogLevel.Info, ( sprintf "Recognizer fail to load because of exception %A. " e ))
                    bNullService <- true
                    CacheService.Start()
              
            if bNullService then 
                Logger.Log( LogLevel.Info, ( sprintf "A null service is launched for testing. " ))
        elif bStop then 
                let svName = serviceName + defaultModel.[0] + "_" + instanceNum.ToString()
                RemoteInstance.Stop( svName )
        elif nStatistics > 0 then 
            // The following is needed here to get the same task signature, will be deprecated later. 
            let exeName = System.Reflection.Assembly.GetExecutingAssembly().Location
            let exePath = Path.GetDirectoryName( exeName )
            let pgName = progName + defaultModel.[0] + "_" + (instanceNum-1).ToString()
            let curJob = JobDependencies.setCurrentJob pgName
            curJob.EnvVars.Add(DeploymentSettings.EnvStringSetJobDirectory, depDirName )
            let modelDirAtRemote = curJob.AddDataDirectoryWithPrefix( null, modeldir, modelDirName, "*", SearchOption.AllDirectories )
            let dlls = curJob.AddDataDirectoryWithPrefix( null, depdir, depDirName, "*", SearchOption.AllDirectories )
            //let managedDlls = curJob.AddDataDirectoryWithPrefix( null, exePath, "dependencies", "*", SearchOption.AllDirectories )

            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Start a continuous statistics loop. Please press ENTER to terminate the statistics." ))
            let mutable bTerminateStatistics = false
            while not bTerminateStatistics do 
                let t1 = (DateTime.UtcNow)
                if enterKey() then 
                    bTerminateStatistics <- true
                else
                    let perfDKV0 = DSet<string*float>(Name="NetworkPerf" )
                    let perfDKV1 = perfDKV0.Import(null, (BackEndInstance<_>.ContractNameActiveFrontEnds))
                    let foldFunc (lst:List<_>) (kv) = 
                        let retLst = 
                            if Utils.IsNull lst then 
                                List<_>()
                            else
                                lst
                        retLst.Add( kv ) 
                        retLst
                    let aggrFunc (lst1:List<_>) (lst2:List<_>) = 
                        lst1.AddRange( lst2 ) 
                        lst1
                    let aggrNetworkPerf = perfDKV1 |> DSet.fold foldFunc aggrFunc null
                    if not (Utils.IsNull aggrNetworkPerf ) then 
                        aggrNetworkPerf |> Seq.iter ( fun tuple -> let path, msRtt = tuple
                                                                   Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "%s: RTT %.2f(ms)" path msRtt ) ))
                    else
                        Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "no active front ends ... " ))

                    let t2 = (DateTime.UtcNow)
                    Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "get network performance statistics in %.2f ms" 
                                                                       (t2.Subtract(t1).TotalMilliseconds) ))
                    if enterKey() then 
                        bTerminateStatistics <- true
                    else
                        let queryDSet0 = DSet<string*Guid*string*int*int>(Name="QueryStatistics" )
                        let queryDSet1 = queryDSet0.Import(null, (BackEndInstance<_>.ContractNameRequestStatistics))
                        let aggrStatistics = queryDSet1.Fold(BackEndQueryStatistics.FoldQueryStatistics, BackEndQueryStatistics.AggregateQueryStatistics, null)
                        if Utils.IsNotNull aggrStatistics then 
                            aggrStatistics.ShowStatistics()
                        else
                            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "no active queries in the epoch ... " ))

                        let t3 = (DateTime.UtcNow)
                        Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "get query statistics in %.2f ms" 
                                                                           (t3.Subtract(t2).TotalMilliseconds) ))

                let mutable bNotWait = bTerminateStatistics 
                while not bNotWait do
                    if enterKey() then 
                        bTerminateStatistics <- true
                        bNotWait <- true
                    else
                        let elapse = (DateTime.UtcNow).Subtract(t1).TotalMilliseconds
                        let sleepMS = nStatistics * 1000 - int elapse
                        if ( sleepMS > 10 ) then 
                            Threading.Thread.Sleep( 10 )
                        else
                            bNotWait <- true
                            
                        
    0

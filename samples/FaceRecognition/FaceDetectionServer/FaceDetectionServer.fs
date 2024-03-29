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
        FaceDetectionServer.fs
  
    Description: 
        An instance of Prajna Recognition Server. For fault tolerance, we will expect multiple Recognition Servers to be launched. Each recognition server
    will cross register with all gateway services. The service will be available as long as at least one gateway server and one recognition server is online. 

    Author:																	
        Jin Li, Principal Researcher
        Microsoft Research, One Microsoft Way
        Email: jinl at microsoft dot com
    Date:
        Nov. 2014

    Modified by:
        Lei Zhang (leizhang@microsoft.com), May 2015
    
 ---------------------------------------------------------------------------*)
open Prajna.Tools
open Prajna.Tools.StringTools
open Prajna.Tools.FSharp

open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open System.Collections.Concurrent
open System.Runtime.Serialization.Json
open System.Xml

open Prajna.Core
open Prajna.Api.FSharp
open Prajna.Api.FSharp
open Prajna.Service.ServiceEndpoint
open Prajna.Service.FSharp

open VMHub.ServiceEndpoint
open VMHub.Data

open System.Drawing
open FaceSdk

let depDirName = "dependencies"
let modelDirName = "models"

type FaceDetector(modelFile) = 
    let detector = new FaceDetectionJDA(new Model(modelFile))
    member x.PredFunc (  reqID:Guid, timeBudget:int, req:RecogRequest )  = 
        let imgBuf = req.Data
        let grayImage = ImageUtility.LoadImageFromBitmapAsGray(new Bitmap(new MemoryStream(imgBuf)))
        let result = detector.DetectAndAlign(grayImage);

        // fill in the required structure RecogResult[]
        let recogResult = 
            result |> 
            Array.map(fun x -> 
                        let r = x.FaceRect
                        let recog = new RecogResult(Rect = new RecogResult.Rectangle(X = r.Left, Y = r.Top, Width = r.Width, Height = r.Height), 
                                                    Confidence = 0.0, AuxResult = String.Empty, CategoryResult = null )
                        recog
                     )
        // serialize to json
        let resultString = recogResult.SerializeToJson()

        VHubRecogResultHelper.FixedClassificationResult( resultString, resultString ) 

/// <summary>
/// Using VHub, the programmer need to define two classes, the instance class, and the start parameter class 
/// The instance class is instantiated at remote machine, it is not serialized.
/// </summary>
type FaceDetectionInstance() as x = 
    inherit VHubBackEndInstance<VHubBackendStartParam>("Prajna")
    do 
        x.OnStartBackEnd.Add( new BackEndOnStartFunction<VHubBackendStartParam>( x.InitializeFaceDetection) )
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
    member x.InitializeFaceDetection( param ) = 
        try
                /// <remarks>
                /// To implement your own image recognizer, please obtain a connection Guid by contacting jinl@microsoft.com
                /// </remarks> 
                x.RegisterAppInfo( Guid("5F2E5F01-D5A1-4CF3-B198-BC6980819208"), "0.0.0.1" )
                /// <remark>
                /// Initialize Recognizer here. 
                /// </remark>

                /// To implement your own image recognizer, please register each classifier with a domain, an image, and a recognizer function. 
                /// </remarks> 
                let modeldir = Path.Combine("..", modelDirName)

                let recogClient = FaceDetector(Path.Combine(modeldir, "FaceSdkModelFile\ProductCascadeJDA27ptsWithLbf.mdl") )
                x.RegisterClassifier( "#FaceDetection", Path.Combine(modeldir, "FaceDetectionLogo.png"), 100, recogClient.PredFunc ) |> ignore
                Logger.Log( LogLevel.Info, ( "classifier registered" ))
                bSuccessInitialized <- true 
                bSuccessInitialized
        with
        | e -> 
            Logger.LogF( LogLevel.Error, ( fun _ -> sprintf "Exception! Message: %s, %A" e.Message e ))
            bSuccessInitialized <- false            
            bSuccessInitialized

module FaceDetectionServer =
    let Usage = "
        Usage: Launch an intance of FaceDetectionServer. The application is intended to run on any machine. It will home in to an image recognition hub gateway for service\n\
        Command line arguments: \n\
            -start       Launch a FaceDetection service in cluster\n\
            -stop        Stop existing FaceDetection service in cluster\n\
            -exe         Execute in exe mode \n\
            -progname    Name of the FaceDetection program container \n\
            -instance    #_OF_INSTANCES  Number of insances to start on each node \n\ 
            -cluster     CLUSTER_NAME    Name of the cluster for service launch \n\
            -port        PORT            Port used for service \n\
            -node        NODE_Name       Launch the recognizer on the node of the cluster only (note that the cluster parameter still need to be provided \n\
            -rootdir     Root_Directory  include models & dependencies subfolders\n\
            -gateway     SERVERURI       ServerUri\n\
            -only        SERVERURI       Only register with this server, disregard default. \n\
            -statis      Seconds         Get backend cluster statistics. The value of statis is the time interval (in second) that the statistics is quered. \n\

        "

    let queryFunc _ = 
        VHubRecogResultHelper.FixedClassificationResult( "Unknown Object", "I don't recognize the object" )

    [<EntryPoint>]
    let main argv = 
        let logFile = sprintf @"c:\Log\ImHub\FaceDetectionServer_%s.log" (VersionToString( (DateTime.UtcNow) ))
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
        let modeldir = Path.Combine( rootdir, modelDirName)
        let depdir = Path.Combine( rootdir, depDirName )

        let bStart = parse.ParseBoolean( "-start", false )
        let bStop = parse.ParseBoolean( "-stop", false )
        let nStatistics = parse.ParseInt( "-statis", 0 )
        let bExe = parse.ParseBoolean( "-exe", true )
        let gatewayServers = parse.ParseStrings( "-gateway", [|"vm-hubr.trafficmanager.net"|] )
        let onlyServers = parse.ParseStrings( "-only", [||] )
        let progName = parse.ParseString( "-progname", "FaceDetectionService" )
        let instanceNum = parse.ParseInt( "-instance", 0 )
        let serviceName = "FaceDetectionEngine"

        Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Program %s started  ... "  (Process.GetCurrentProcess().MainModule.FileName) ) )
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
                let mutable bNullService = false 
                try 
                        let pgName = progName + "_" + instanceNum.ToString()
                        let svName = serviceName + "_" + instanceNum.ToString()
                        let curJob = JobDependencies.setCurrentJob pgName
                        if bNullService then 
                            bNullService <- true
                            CacheService.Start() 
                        else
                            let exeName = System.Reflection.Assembly.GetExecutingAssembly().Location
                            let exePath = Path.GetDirectoryName( exeName )

                            let curCluster = Cluster.GetCurrent()

                            curJob.EnvVars.Add(DeploymentSettings.EnvStringSetJobDirectory, depDirName )
                            
                            if Utils.IsNotNull curCluster then
                                // copy model files to the remote model folder
                                let modelFiles = curJob.AddDataDirectoryWithPrefix( null, modeldir, modelDirName, "*", SearchOption.AllDirectories )
                                // copy native dlls to the remote dependency folder
                                curJob.AddDataDirectoryWithPrefix( null, depdir, depDirName, "*", SearchOption.AllDirectories ) |> ignore
                                
                        
                            let startParam = VHubBackendStartParam()
                            /// Any Gateway, considered as traffic manager, needs repeated DNS resolve
                            for gatewayServer in gatewayServers do 
                                if not (StringTools.IsNullOrEmpty( gatewayServer)) then 
                                    startParam.AddOneTrafficManager( gatewayServer, usePort  )
                            /// Single resolve server
                            for onlyServer in onlyServers do 
                                if not (StringTools.IsNullOrEmpty( onlyServer)) then 
                                    startParam.AddOneServer( onlyServer, usePort  )

                            if Utils.IsNull curCluster then 
                                Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Start a local recognizer, press ENTER to terminate the recognizer" ))
                                // set depdir as the current dir, same as in remote mode
                                Directory.SetCurrentDirectory( depdir )
                                RemoteInstance.StartLocal( serviceName, startParam, fun _ -> FaceDetectionInstance() ) 
                                while RemoteInstance.IsRunningLocal(serviceName) do 
                                    if enterKey() then 
                                            RemoteInstance.StopLocal(serviceName)
                                        else
                                            Thread.Sleep(10)
                            else
                                RemoteInstance.Start( svName, startParam, (fun _ -> FaceDetectionInstance() ) )
                    with 
                    | e -> 
                        Logger.Log( LogLevel.Info, ( sprintf "Recognizer fail to load because of exception %A. " e ))
                        bNullService <- true
                        CacheService.Stop() 
              
                if bNullService then 
                    Logger.Log( LogLevel.Info, ( sprintf "A null service is launched for testing. " ))
            elif bStop then 
                    let svName = serviceName + "_" + instanceNum.ToString()
                    RemoteInstance.Stop( svName )
            elif nStatistics > 0 then 
                // The following is needed here to get the same task signature, will be deprecated later. 
                let exeName = System.Reflection.Assembly.GetExecutingAssembly().Location
                let exePath = Path.GetDirectoryName( exeName )
                let pgName = progName + "_" + (instanceNum-1).ToString()
                let curJob = JobDependencies.setCurrentJob pgName
                let logoImage = curJob.AddDataDirectoryWithPrefix( null, Path.Combine(rootdir, "image"), "image", "*", SearchOption.AllDirectories )

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
                            if not (Utils.IsNull aggrStatistics) then 
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

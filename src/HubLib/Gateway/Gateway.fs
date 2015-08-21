(*---------------------------------------------------------------------------
    Copyright 2015 Microsoft

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
        vHubFrontEnd.fs
  
    Description: 
        FrontEndLib class of the VHub

    Author:																	
        Jin Li, Principal Researcher
        Microsoft Research, One Microsoft Way
        Email: jinl at microsoft dot com
    Date:
        Feb. 2015
    
 ---------------------------------------------------------------------------*)
namespace VMHub.Gateway

open System
open System.Collections.Generic
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Prajna.Tools
open Prajna.Tools.StringTools
open Prajna.Tools.FSharp
open Prajna.Tools.Network
open Prajna.Core
open Prajna.Service.ServiceEndpoint
open Prajna.Service.Gateway
open VMHub.ServiceEndpoint
open VMHub.Data

open Prajna.Service.Gateway
/// <summary>
/// VHubFrontendStartParam: this will be used to launch a VHubInstance at remote
/// </summary>
/// <param name = "fqdn"> Fully qualified domain name, use to construct absolute URL </param>
/// <example>
/// See SampleRecogServer on how the classifier is instantiated. 
/// </example>
[<AllowNullLiteral>]
type VHubFrontendStartParam( ) as x = 
    inherit FrontEndServiceParam()
    do 
        x.DecodeServiceCollectionFunction <- DecodeCollectionFunction( VHubServiceInstance.DecodeCollections )
    member val WebRootName = "WebRoot" with get, set

/// <summary>
/// This class track information related to one BackEnd server. 
/// </summary>
type VHubBackEndPerformance() = 
    inherit BackEndPerformance( NetworkPerformance.RTTSamples, 
            SinglePerformanceConstructFunction( fun _ -> SingleQueryPerformance()) ) 
    member val AppInfo : VHubAppInfo = null with get, set

[<AllowNullLiteral; Serializable>]
type VHubRequestInfo() = 
    member val ServiceID = Guid.Empty with get, set
    member val DistributionPolicy = Guid.Empty with get, set
    member val AggregationPolicy = Guid.Empty with get, set

[<AllowNullLiteral>]
type VHubExtendedRequest() = 
    member val Info : VHubRequestInfo = null with get, set
    member val RequestObject : RecogRequest = null with get, set
/// <summary>
/// This class represent a backend query instance. The developer needs to extend BackEndInstance class, to implement the missing functions.
/// The following command are reserved:
///     List, Buffer : in the beginning, to list Guids that are used by the current backend instance. 
///     Read, Buffer : request to send a list of missing Guids. 
///     Write, Buffer: send a list of missing guids. 
///     Echo, QueryReply : Keep alive, send by front end periodically
///     EchoReturn, QueryReply: keep alive, send to front end periodically
///     Set, QueryReply: Pack serviceInstance information. 
///     Get, QueryReply: Unpack serviceInstance information 
///     Request, QueryReply : FrontEnd send in a request (reqID, serviceID, payload )
///     Reply, QueryReply : BackEnd send in a reply
///     TimeOut, QueryReply : BackEnd is too heavily loaded, and is unable to serve the request. 
///     NonExist, QueryReply : requested BackEnd service ID doesn't exist. 
/// Programmer will need to extend BackEndInstance class to fill in OnStartBackEnd. The jobs of OnStartBackEnd are: 
/// 1. fill in ServiceCollection entries. Note that N parallel thread will be running the Run() operation. However, OnStartBackEnd are called only once.  
/// 2. fill in BufferCache.Current on CacheableBuffer (constant) that we will expect to store at the server side. 
/// 3. fill in MoreParseFunc, if you need to extend beyond standard message exchanged between BackEnd/FrontEnd
///         Please make sure not to use reserved command (see list in the description of the class BackEndInstance )
///         Network health and message integrity check will be enforced. So when you send a new message to the FrontEnd, please use:
///             health.WriteHeader (ms)
///             ... your own message ...
///             health.WriteEndMark (ms )
/// 4. Setting TimeOutRequestInMilliSecond, if need
/// 5. Function in EncodeServiceCollectionAction will be called to pack all service collection into a stream to be sent to the front end. 
/// </summary>
[<AllowNullLiteral>]
type VHubFrontEndInstance<'StartParam when 'StartParam :> VHubFrontendStartParam >() as x = 
    inherit FrontEndInstance< 'StartParam >() 
    do 
        x.OnStartFrontEnd.Add( FrontEndOnStartFunction(x.StartVHub))
        x.OnParse.Add( FrontEndParseFunction( x.ParseVHub))
        x.BackEndPerformanceConstructionDelegate <- 
            BackEndPerformanceConstructFunction( fun _ -> VHubBackEndPerformance() :> BackEndPerformance )
        x.OnAddServiceInstance(Action<_>( x.AddVHubService ), fun _ -> sprintf "Add Vhub" )
        
    static member NullVHubBlob() = 
        VHubBlob( Data = null, TypeInfo = null )
    member x.StartVHub param = 
        let domain = 
            try 
                System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName
            with 
            | e -> 
                null
        let fqdn = if StringTools.IsNullOrEmpty( domain) then RemoteExecutionEnvironment.MachineName + ".cloudapp.net" else
                        RemoteExecutionEnvironment.MachineName + "." + domain
        VHubSetting.FQDN <- fqdn
        VHubSetting.GlobalVisibleMonitorServiceUrl  <- VHubSetting.GlobalVisibleMonitorServiceUrlFunc fqdn    
        VHubSetting.WebRootName <- param.WebRootName
        WebCache.addRoot( VHubSetting.WebRoot, "*", System.IO.SearchOption.AllDirectories ) 
        VHubProviderList.init( ".", VHubSetting.FilenameDefaultProviderList ) 

        JobDependencies.InstallSerializer<RecogRequest>( VHubRecogRequestHelper.RecogRequestCodecGuid,  VHubRecogRequestHelper.Pack )
        JobDependencies.InstallDeserializer<RecogReply>( VHubRecogResultHelper.RecogReplyCodecGuid, VHubRecogResultHelper.Unpack )
        true
    member x.ParseVHub( queue, ha, cmd, ms) = 
        match cmd.Verb, cmd.Noun with 
        | ControllerVerb.Open, ControllerNoun.QueryReply ->
            let health = ha :?> VHubBackEndPerformance
            health.AppInfo <- VHubAppInfo.Unpack( ms ) 
            true, null
        | _ -> 
            false, null 
    member x.AddVHubService (remoteSignature, serviceInstance ) =
        match serviceInstance with 
        | :? VHubServiceInstance as vHubInstance -> 
            /// This is the mega instance, corresponding to a common domain that serves by all active recognizer. 
            let megaGuid = HashStringToGuid( vHubInstance.RecogDomain )            
            if megaGuid<> vHubInstance.ServiceID then 
                // A new mega service is created. 
                let megaInstance = VHubServiceInstance()
                megaInstance.ServiceID <- VHubServiceInstance.ConstructServiceID( vHubInstance.RecogDomain, null ) 
                megaInstance.EngineName <- "" // Indicating as mega service. 
                megaInstance.LeastSmallestDimension <- vHubInstance.LeastSmallestDimension
                megaInstance.MaxConcurrentItems <- vHubInstance.MaxConcurrentItems
                megaInstance.RecogDomain <- vHubInstance.RecogDomain
                megaInstance.SampleImageGuid <- vHubInstance.SampleImageGuid
                x.AddServiceInstanceGuid( remoteSignature, megaGuid, megaInstance ) |> ignore
        | _ -> 
            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "AddVHubService is called with serviceInstance %A, but we expect a vHubInstance class. Something is wrong. " serviceInstance ))
    /// List all service instances that are available
    member x.ListVHubServices() = 
        x.ServiceCollectionByID |> Seq.collect ( fun pair -> pair.Value.Values |> Seq.map( fun inst -> pair.Key, inst ) )
                                |> Seq.choose( fun (serviceID, inst) -> match inst with 
                                                                        | :? VHubServiceInstance as vHubInstance -> 
                                                                            Some ( serviceID, vHubInstance.ServiceID, vHubInstance.RecogDomain )
                                                                        | _ -> 
                                                                            None )

        
    /// Get a list of providers that are online
    member x.GetActiveProviders( ) = 
        let allProviders = x.BackEndHealth |> Seq.map( fun pair -> (pair.Value :?> VHubBackEndPerformance).AppInfo )
        let recogEngine = Dictionary<Guid, RecogEngine>()
        for provider in allProviders do 
            let providerInfo = VHubProviderList.Current.GetProvider( provider.ProviderID ) 
            if not (Utils.IsNull providerInfo) then 
                recogEngine.Item( providerInfo.RecogEngineID ) <-
                                 RecogEngine( RecogEngineID = providerInfo.RecogEngineID, 
                                               RecogEngineName=providerInfo.RecogEngineName, 
                                               InstituteName=providerInfo.InstituteName, 
                                               InstituteURL=providerInfo.InstituteURL, 
                                               ContactEmail=providerInfo.ContactEmail ) 
        recogEngine.Values |> Seq.toArray
        
    
    /// Get a list of providers that are online
    member x.GetActiveBackends( ) = 
        let allBackends = x.BackEndHealth |> Seq.map( fun pair -> (pair.Key, pair.Value :?> VHubBackEndPerformance) )
        allBackends
    member x.GetBackendPerformance() = 
        let backendPerfs = x.BackEndHealth |> Seq.choose( fun pair ->   let name = LocalDNS.GetShowInfo(LocalDNS.Int64ToIPEndPoint(pair.Key))
                                                                        let health = pair.Value :?> VHubBackEndPerformance 
                                                                        let app = health.AppInfo
                                                                        if Utils.IsNull app then 
                                                                            None
                                                                        else
                                                                            Some( RemoteExecutionEnvironment.MachineName + ":" + app.HostName, health.GetRtt(), health.ExpectedLatencyInfo(), health.QueueInfo() )
                                                                        )
        backendPerfs


    /// Get a list of active vHubInstance (including megaService), in the form of
    /// Guid * VHubServiceInstance * seq{int64}, the last is a list of remote endpoints that are providing the service. 
    member x.GetActiveInstances( ) = 
        try
            let allInstances = x.ServiceCollectionByID 
                               // Filter out metaService if there is only a single provider 
                               |> Seq.choose( fun pair -> VHubFrontEndInstance<_>.FilterOutSingleProvider pair ) 
            allInstances
        with 
        | e -> 
            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "GetActiveInstances failed with exception %A" e ))
            Seq.empty




    static member FilterOutSingleProvider pair = 
        let megaID = pair.Key
        let serviceCollection = pair.Value
        VHubFrontEndInstance<_>.GetValidMegaService megaID serviceCollection


    /// <summary> 
    /// Get a Service Instance, filter out mega Service which has only 1 provider. 
    /// </summary>
    member x.GetValidServiceInstanceByID( id ) = 
        let bExist, serviceCollection = x.ServiceCollectionByID.TryGetValue( id ) 
        if bExist then 
            VHubFrontEndInstance<_>.GetValidMegaService id serviceCollection 
        else
            None
    /// <summary> 
    /// Get a Service Instance by its ID
    /// </summary>
    member x.GetServiceInstanceByID( id ) = 
        try
            let bExist, serviceCollection = x.ServiceCollectionByID.TryGetValue( id ) 
            if bExist then 
                let en = serviceCollection.Values.GetEnumerator()
                if en.MoveNext() then 
                    // We don't use Seq.head to deal with empty collection. 
                    let serviceInstance = en.Current :?> VHubServiceInstance
                    Some ( id, serviceInstance, serviceCollection.Keys :> seq<_> )
                else
                    None
            else
                None
        with 
        | e -> 
            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "GetServiceInstanceByID failed with exception %A" e ))
            None
    /// <summary> 
    /// Get the AppInfo by name 
    /// </summary> 
    member x.GetAppInfoByBackend( remoteSignatures ) = 
        let mutable bFind = false
        let mutable appInfo = null
        for remoteSignature in remoteSignatures do
            let bExist, health = x.BackEndHealth.TryGetValue( remoteSignature )
            if bExist then 
                let curApp = ( health :?> VHubBackEndPerformance ).AppInfo
                if Utils.IsNull appInfo then 
                    appInfo <- curApp
                elif appInfo.ProviderVersion <=  curApp.ProviderVersion then 
                    appInfo <- curApp
        appInfo
    /// <summary> 
    /// Get a Recognition Instance by its ID
    /// </summary>
    member x.GetRecogInstance( id ) = 
        match x.GetServiceInstanceByID(id ) with 
        | Some ( serviceID, serviceInstance, remoteSignatures ) ->
            let appInfo = x.GetAppInfoByBackend( remoteSignatures ) 
            let version = if Utils.IsNull appInfo then 0u else appInfo.ProviderVersion
            let recogInstance = RecogInstance( ServiceID = serviceID, 
                                                Name = serviceInstance.RecogDomain, 
                                                EngineName = serviceInstance.EngineName, 
                                                EntityGuid = [| serviceInstance.SampleImageGuid |], 
                                                Version = version, 
                                                Parameter = null )
            recogInstance 
        | None -> 
            null            

    /// <summary> 
    /// Get a Service Instance by its
    /// </summary>
    member x.GetBlobByID( id ) = 
        try
            let buf = OwnershipTracking.Current.GetCacheableBuffer( id ) 
            VHubBlob( Data = buf.Buffer, TypeInfo = buf.TypeInfo )
        with 
        | e -> 
            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "GetBlobByID failed with exception %A" e ))
            VHubFrontEndInstance<_>.NullVHubBlob() 
    /// <summary> 
    /// Get a Service Instance by its
    /// </summary>
    member x.GetBlobByIDAsync( id ) = 
        try
            let task = OwnershipTracking.Current.GetCacheableBufferAsync( id ) 
            let blobTask (inpTask: Task<CacheableBuffer> ) = 
                let buf = inpTask.Result
                VHubBlob( Data = buf.Buffer, TypeInfo = buf.TypeInfo )
            task.ContinueWith( blobTask )
        with 
        | e -> 
            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "GetBlobByID failed with exception %A" e ))
            new Task<_>( fun _ -> VHubFrontEndInstance<_>.NullVHubBlob()  )
    static member GetValidMegaService megaID serviceCollection = 
        let mutable vHubInstance = Unchecked.defaultof<_>
        let mutable bUniqueService = true
        let mutable seenServiceGuid = Guid.Empty
        for pair in serviceCollection do 
            if seenServiceGuid <> pair.Value.ServiceID then 
                if seenServiceGuid=Guid.Empty then 
                    seenServiceGuid <- pair.Value.ServiceID
                    vHubInstance <- pair.Value :?> VHubServiceInstance
                else
                    // We have found more then one service ID. The megaService entry should not be filtered out. 
                    bUniqueService <- false
        if bUniqueService then  
            if seenServiceGuid<>Guid.Empty then 
                if not(StringTools.IsNullOrEmpty( vHubInstance.EngineName )) then 
                    // Service collection is non empty, and has a non-null Engine name, this is a valid instance, and should not be filtered out. 
                    bUniqueService <- false
        if bUniqueService then 
            None 
        else
            Some ( megaID, vHubInstance, serviceCollection.Keys :> seq<_> )
    /// <summary> 
    /// Search Instances, if any of the string match, we consider that the instance matches)
    /// </summary>
    member x.SearchInstancesByString( searchString: string ) = 
        x.GetActiveInstances( ) |> Seq.filter( fun tuple -> let id, vHubInstance, s = tuple 
                                                            let bFind = vHubInstance.EngineName.IndexOf( searchString, StringComparison.OrdinalIgnoreCase ) >=0 ||
                                                                           vHubInstance.RecogDomain.IndexOf( searchString, StringComparison.OrdinalIgnoreCase ) >=0 
                                                            bFind )

    /// Get a list of active vHubInstance to be returned to the WebAPI
    member x.GetWorkingInstances (engineName) = 
            let allInstances = x.GetActiveInstances()
            let lst = List<_>()
            for tuple in allInstances do 
                let serviceID, serviceInstance, ep = tuple 
                if Utils.IsNotNull ep && Seq.length ep > 0 then 
                    if StringTools.IsNullOrEmpty engineName ||
                        String.Compare( engineName, serviceInstance.EngineName, StringComparison.OrdinalIgnoreCase )=0 then 
                            lst.Add( RecogInstance( ServiceID=serviceID, 
                                                    Name = serviceInstance.RecogDomain, 
                                                    EngineName = serviceInstance.EngineName )
                                    )
                ()
            lst.ToArray()
//    override x.OnStart param = 
//        base.OnStart param 
//    override x.OnStop() = 
//        base.OnStop()
//    override x.IsRunning() = 
//        base.IsRunning()
//    override x.Run() = 
//        base.Run()
    /// <summary>
    /// Receive an incoming request. The following information are needed:
    /// reqID: Guid to identify the request. (no request cache is implemented in the moment, but we could cache reply based on guid. 
    /// serviceID: Guid that identify the serviceID that will be requested. 
    /// distributionPolicy: Guid that identify the distribution service to be used
    /// aggregationPolicy: Guid that identify the aggregation service to be used
    /// ticksSentByClient: Client can use the ticks to estimate network RTT of the request. 
    /// RTTSentByClient: network RTT of the request, 
    /// IPAddress, port: IP 
    /// </summary> 
    member x.RecogObject( recog: VHubExtendedRequest ) = 
        let reqID = System.Guid.NewGuid() 
        let ticksCur = (DateTime.UtcNow.Ticks)
        // ReqID is the only one that is not in the reqHolder, it is being used like a key
        let reqHolder = RequestHolder( ServiceID = recog.Info.ServiceID, 
                                        DistributionPolicy = recog.Info.DistributionPolicy, 
                                        AggregationPolicy = recog.Info.AggregationPolicy, 
                                        ReqObject = recog.RequestObject, 
                                        TicksSentByClient = ticksCur, 
                                        RTTSentByClient = 0, 
                                        AddressByClient = "localhost", 
                                        PortByClient = 0, 
                                        TicksRequestReceived = ticksCur, 
                                        TicksRequestTimeout = if x.TimeOutTicks=Int64.MaxValue then DateTime.MaxValue.Ticks else ticksCur + x.TimeOutTicks )
        let taskSource = TaskCompletionSource<RecogReply>()
        x.ProcessReqeust reqID reqHolder (Action<_>(x.RecogObjectReply taskSource)) (fun _ -> sprintf "Reply of request %A" reqID )
        taskSource.Task
    member x.RecogObjectReply taskSource (perfQ, result) =
        match result with 
        | :? RecogReply as reply -> 
            reply.PerfInformation <- perfQ.FrontEndInfo()
            taskSource.SetResult( reply )
        | _ -> 
            taskSource.SetResult( null )

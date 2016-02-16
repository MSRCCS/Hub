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
        vHubMonitorBackEnd.fs
  
    Description: 
        Monitor a cluster of backend, show statistics information on both network performance and real-time query statistics. 

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

open Prajna.Tools.Network

open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Collections.Generic
open System.Collections.Concurrent
open System.ComponentModel
open System.IO
open System.Windows
open System.Windows.Data
open System.Windows.Media
open System.Windows.Media.Imaging
open System.Windows.Controls
open System.Windows.Threading
open Prajna.WPFTools


open Prajna.Core
open Prajna.Api.FSharp
open Prajna.Api.FSharp
open Prajna.Service.ServiceEndpoint
open Prajna.Service.Gateway
open VMHub.Data
open VMHub.ServiceEndpoint
open VMHub.Gateway
open VMHub.GatewayWebService


let Usage = "
    Usage:  Monitor a cluster of backend, show statistics information on both network performance and real-time query statistics.\n\
    -cluster    Cluster to be monitored. \n\
    -progname   Program container of the backend. \n\
    "       

[<AllowNullLiteral; Serializable>]
type ClientRequestsFrontEnd() = 
    member val RequestCollection = ConcurrentDictionary<_,_>( StringComparer.Ordinal ) with get 
    static member CalculateClientRequests ( arr: (string*int*int64*string)[] ) = 
        if Utils.IsNull arr then 
            null
        else
            let t1 = (DateTime.UtcNow.Ticks)
            let hostmachine = RemoteExecutionEnvironment.MachineName
            let aggRequest = ClientRequestsFrontEnd()
            for item in arr do 
                let clientAddr, rttClient, ticksClient, info = item
                let bParsed, addr = System.Net.IPAddress.TryParse( clientAddr ) 
                let clientName = 
                    if bParsed then 
                        LocalDNS.GetNameForIPAddress( addr ) 
                    else
                        "Unknown"
                let useName = RemoteExecutionEnvironment.MachineName + ":" + clientName + ":" + info
                aggRequest.RequestCollection.AddOrUpdate( useName, (fun _ -> 1), (fun k v -> v + 1) ) |> ignore 
            Logger.LogF( LogLevel.MildVerbose, ( fun _ -> let t2 = (DateTime.UtcNow.Ticks)
                                                          sprintf "calculate ClientRequests from %d data points yield %d entries using %dms" 
                                                                   arr.Length aggRequest.RequestCollection.Count
                                                                   ((t2-t1)/TimeSpan.TicksPerMillisecond) ))
            Array.create 1 aggRequest
    /// Fold may give a null variable. 
    static member Aggregate (x:ClientRequestsFrontEnd) (y:ClientRequestsFrontEnd) = 
        if Utils.IsNull y then 
            x 
        elif Utils.IsNull x then 
            y
        else
            for pair in y.RequestCollection do 
                x.RequestCollection.AddOrUpdate( pair.Key, pair.Value, fun k vx -> vx + pair.Value ) |> ignore 
            x

[<AllowNullLiteral>]
type ClientRequestWindow( name ) = 
    inherit GridWithStringContent(name ) 
    member x.UpdateClientRequests( qRequest: ClientRequestsFrontEnd ) = 
        if Utils.IsNotNull qRequest then 
            // Only place where we formatting data. 
            let columnsDic = Dictionary<_,_>( StringComparer.Ordinal ) 
            for info in qRequest.RequestCollection.Keys do 
                let nameArr = info.Split( [|':'|] )
                // front end in columns 
                if nameArr.Length >= 3 then 
                    columnsDic.Item( nameArr.[2] ) <- 0
            let columns = columnsDic.Keys |> Seq.sort |> Seq.toArray
            columnsDic.Clear() 
            // Lookup table for frontend name -> columns
            for i = 0 to columns.Length - 1 do 
                columnsDic.Item( columns.[i] ) <- i
            let rowDic = ConcurrentDictionary<_,(_)[]>(StringComparer.Ordinal)
            for pair in qRequest.RequestCollection do 
                let cnt = pair.Value
                let nameArr = pair.Key.Split( [|':'|] )
                if nameArr.Length >= 3 then 
                    let frontend = nameArr.[0]
                    let client = nameArr.[1] 
                    let info = nameArr.[2]
                    let useName = frontend + ":" + client
                    let rowArr = rowDic.GetOrAdd( useName, fun _ -> Array.create columns.Length "" )
                    let idx = columnsDic.Item( info ) 
                    rowArr.[idx] <- cnt.ToString()
            let contentarr = rowDic |> Seq.map ( fun pair -> Array.append (pair.Key.Split([|':'|])) pair.Value ) 
                                    |> Seq.toArray
            if contentarr.Length > 0 then 
                x.SetContent( contentarr, Array.append [| "FrontEnd"; "Client" |] columns )
            else
                x.SetContent( Array.create 1 [| "None"; "" |] , [| "FrontEnd"; "Client" |]  )
        else
            x.SetContent( Array.create 1 [| "None"; "" |], [| "FrontEnd"; "Client" |]  )


[<AllowNullLiteral; Serializable>]
type ServiceCollection() = 
    member val ServiceIDToDomain = ConcurrentDictionary<_,_>( ) with get 
    member val ServiceCollection = ConcurrentDictionary<_,_>( StringTComparer<Guid>(StringComparer.Ordinal) ) with get 
    static member FoldServiceCollection (y:ServiceCollection) (tuple:Guid*Guid*string) = 
        let x = if Utils.IsNull y then ServiceCollection() else y
        if true then 
            // Aggregate by container:
            let megaID, serviceID, domainName= tuple
            let name = RemoteExecutionEnvironment.MachineName 
            if megaID = serviceID then 
                x.ServiceCollection.AddOrUpdate( (name, serviceID), ( 1, domainName ),
                    fun _ tuple -> let cnt, dName = tuple
                                   cnt+1, dName ) |> ignore 
            x.ServiceIDToDomain.Item( megaID ) <- (domainName, megaID = serviceID)
        x
    /// Fold may give a null variable. 
    static member Aggregate (x:ServiceCollection) (y:ServiceCollection) = 
        if Utils.IsNull y then 
            x 
        elif Utils.IsNull x then 
            y
        else
            for pair in y.ServiceCollection do 
                x.ServiceCollection.AddOrUpdate( pair.Key, pair.Value, fun k tuple -> let cnt1, dName1 = tuple
                                                                                      let cnt2, dName2 = pair.Value 
                                                                                      cnt1 + cnt2, dName1 ) |> ignore 
            for pair in y.ServiceIDToDomain do 
                x.ServiceIDToDomain.Item( pair.Key ) <- pair.Value
            x

[<AllowNullLiteral; Serializable>] 
type QueryPerformance() = 
    member val AvgInProcessing = 0 with get, set
    member val MediumInProcessing = 0 with get, set        
    member val Pct90InProcessing = 0 with get, set        
    member val Pct999InProcessing = 0 with get, set
    member val AvgInQueue = 0 with get, set
    member val MediumInQueue = 0 with get, set        
    member val Pct90InQueue = 0 with get, set        
    member val Pct999InQueue = 0 with get, set
    member val AvgInNetwork = 0 with get, set
    member val MediumInNetwork = 0 with get, set        
    member val Pct90InNetwork = 0 with get, set        
    member val Pct999InNetwork = 0 with get, set
    member val AvgInAssignment = 0 with get, set
    member val MediumInAssignment = 0 with get, set        
    member val Pct90InAssignment = 0 with get, set        
    member val Pct999InAssignment = 0 with get, set
    static member Average (x:QueryPerformance) (y:QueryPerformance) = 
        let avg x y = 
            ( x + y ) / 2
        // Medium, Pct90 and Pct99 should not be averaged. The operation will actually never executes 
        QueryPerformance( AvgInProcessing = avg x.AvgInProcessing y.AvgInProcessing,
                          MediumInProcessing = avg x.MediumInProcessing y.MediumInProcessing, 
                          Pct90InProcessing = avg x.Pct90InProcessing y.Pct90InProcessing, 
                          Pct999InProcessing = avg x.Pct999InProcessing y.Pct999InProcessing, 
                          AvgInQueue = avg x.AvgInQueue y.AvgInQueue, 
                          MediumInQueue = avg x.MediumInQueue y.MediumInQueue, 
                          Pct90InQueue = avg x.Pct90InQueue y.Pct90InQueue, 
                          Pct999InQueue = avg x.Pct999InQueue y.Pct999InQueue,
                          AvgInNetwork = avg x.AvgInNetwork y.AvgInNetwork, 
                          MediumInNetwork = avg x.MediumInNetwork y.MediumInNetwork, 
                          Pct90InNetwork = avg x.Pct90InNetwork y.Pct90InNetwork, 
                          Pct999InNetwork = avg x.Pct999InNetwork y.Pct999InNetwork,
                          AvgInAssignment = avg x.AvgInAssignment y.AvgInAssignment, 
                          MediumInAssignment = avg x.MediumInAssignment y.MediumInAssignment, 
                          Pct90InAssignment = avg x.Pct90InAssignment y.Pct90InAssignment, 
                          Pct999InAssignment = avg x.Pct999InAssignment y.Pct999InAssignment 
                          )  

[<AllowNullLiteral; Serializable>]
type QueryPerformanceCollection() = 
    member val PerformanceCollection = ConcurrentDictionary<_,_>( StringTComparer<Guid>(StringComparer.Ordinal) ) with get 
    static member CalculateQueryPerformance ( arr: (Guid*int64*Guid*string*SingleRequestPerformance)[] ) = 
        if Utils.IsNull arr then 
            null
        else
            let t1 = (DateTime.UtcNow.Ticks)
            let hostmachine = RemoteExecutionEnvironment.MachineName
            let dic = ConcurrentDictionary<_,List<_>>( StringTComparer<Guid>(StringComparer.Ordinal) )
            let pct length percentile = 
                let idx = int (round( float length * percentile ))
                if idx >= length then length - 1 else idx
            for item in arr do 
                let _, _, serviceID, infoAdd, qPerf = item
                let perf = 
                    if Utils.IsNull qPerf then 
                       null
                    elif StringTools.IsNullOrEmpty qPerf.Message then 
                        qPerf
                    else
                        null
                let useMachineName = hostmachine
                if not (Utils.IsNull perf) then 
                    let lst = dic.GetOrAdd( (useMachineName, serviceID), fun _ -> List<_>() )   
                    lst.Add( perf )
            let x = QueryPerformanceCollection()
            for pair in dic do 
                let perfItem = QueryPerformance()
                let lst = pair.Value
                let sortArrProcessing =  lst.ToArray() |> Array.sortBy( fun perf -> perf.InProcessing )
                let sumProcessing = sortArrProcessing |> Array.sumBy( fun perf -> int64 perf.InProcessing )
                perfItem.AvgInProcessing <- int ( sumProcessing / int64 sortArrProcessing.Length )
                perfItem.MediumInProcessing <- sortArrProcessing.[sortArrProcessing.Length / 2 ].InProcessing
                perfItem.Pct90InProcessing <- sortArrProcessing.[pct sortArrProcessing.Length 0.9].InProcessing
                perfItem.Pct999InProcessing <- sortArrProcessing.[pct sortArrProcessing.Length 0.999].InProcessing
                let sortArrQueue =  lst.ToArray() |> Array.sortBy( fun perf -> perf.InQueue )
                let sumQueue = sortArrQueue |> Array.sumBy( fun perf -> int64 perf.InQueue )
                perfItem.AvgInQueue <- int ( sumQueue / int64 sortArrQueue.Length )
                perfItem.MediumInQueue <- sortArrQueue.[sortArrQueue.Length / 2 ].InQueue
                perfItem.Pct90InQueue <- sortArrQueue.[pct sortArrQueue.Length 0.9].InQueue
                perfItem.Pct999InQueue <- sortArrQueue.[pct sortArrQueue.Length 0.999].InQueue
                let sortArrNetwork =  lst.ToArray() |> Array.sortBy( fun perf -> perf.InNetwork )
                let sumNetwork = sortArrNetwork |> Array.sumBy( fun perf -> int64 perf.InNetwork )
                perfItem.AvgInNetwork <- int ( sumNetwork / int64 sortArrNetwork.Length )
                perfItem.MediumInNetwork <- sortArrNetwork.[sortArrNetwork.Length / 2 ].InNetwork
                perfItem.Pct90InNetwork <- sortArrNetwork.[pct sortArrNetwork.Length 0.9].InNetwork
                perfItem.Pct999InNetwork <- sortArrNetwork.[pct sortArrNetwork.Length 0.999].InNetwork
                let sortArrAssignment =  lst.ToArray() |> Array.sortBy( fun perf -> perf.InAssignment )
                let sumAssignment = sortArrAssignment |> Array.sumBy( fun perf -> int64 perf.InAssignment )
                perfItem.AvgInAssignment <- int ( sumAssignment / int64 sortArrAssignment.Length )
                perfItem.MediumInAssignment <- sortArrAssignment.[sortArrAssignment.Length / 2 ].InAssignment
                perfItem.Pct90InAssignment <- sortArrAssignment.[pct sortArrAssignment.Length 0.9].InAssignment
                perfItem.Pct999InAssignment <- sortArrAssignment.[pct sortArrAssignment.Length 0.999].InAssignment
                x.PerformanceCollection.Item( pair.Key ) <- perfItem
            Logger.LogF( LogLevel.MildVerbose, ( fun _ -> let t2 = (DateTime.UtcNow.Ticks)
                                                          sprintf "calculate QueryPerformance for frontend from %d data points using %dms" 
                                                                   arr.Length 
                                                                   ((t2-t1)/TimeSpan.TicksPerMillisecond) ))
            Array.create 1 x
    /// Fold may give a null variable. 
    static member Aggregate (x:QueryPerformanceCollection) (y:QueryPerformanceCollection) = 
        if Utils.IsNull y then 
            x 
        elif Utils.IsNull x then 
            y
        else
            for pair in y.PerformanceCollection do 
                x.PerformanceCollection.AddOrUpdate( pair.Key, pair.Value, fun k vx -> QueryPerformance.Average vx pair.Value ) |> ignore 
            x


[<AllowNullLiteral>]
type QueryPerformanceWindow( name, firstcolumn, mapFunc ) = 
    inherit GridWithStringContent(name ) 
    member x.UpdateQueryPerformance( perfLst : List<string*float*string*string> ) = 
        if Utils.IsNotNull perfLst then 
            // Only place where we formatting data. 
            let columnsDic = Dictionary<_,_>( StringComparer.Ordinal ) 
            for tuple in perfLst do 
                let name, _, _, _ = tuple
                let hostinfo = name.Split( [|':'|] )
                // front end in columns 
                if hostinfo.Length >= 3 then 
                    columnsDic.Item( hostinfo.[0] ) <- 0
            let columns = columnsDic.Keys |> Seq.sort |> Seq.toArray
            columnsDic.Clear() 
            // Lookup table for frontend name -> columns
            for i = 0 to columns.Length - 1 do 
                columnsDic.Item( columns.[i] ) <- i
            let rowDic = ConcurrentDictionary<_,(_)[]>(StringComparer.Ordinal)
            for tuple in perfLst do 
                let name, _, _, _ = tuple
                let hostinfo = name.Split( [|':'|] )
                if hostinfo.Length >= 3 then 
                    let frontend = hostinfo.[0]
                    let backend = hostinfo.[1] 
                    let containerName = hostinfo.[2]
                    let containerDigits =   
                        if StringTools.IsNullOrEmpty containerName then 
                            null
                        else
                            let mutable lastNonDigitPos = containerName.Length - 1
                            let mutable bFindNonDigit = false
                            while not bFindNonDigit && lastNonDigitPos >= 0 do 
                                if Char.IsDigit (containerName.[lastNonDigitPos]) then 
                                    lastNonDigitPos <- lastNonDigitPos - 1   
                                else
                                    bFindNonDigit <- true
                            containerName.Substring( lastNonDigitPos + 1 )
                    let usebackendName = if Utils.IsNull containerDigits then backend else backend + "-" + containerDigits
                    let rowArr = rowDic.GetOrAdd( usebackendName, fun _ -> Array.create columns.Length "" )
                    let idx = columnsDic.Item( frontend ) 
                    let showPerf = mapFunc tuple
                    rowArr.[idx] <- showPerf
            let contentarr = rowDic |> Seq.map ( fun pair -> Array.append [| pair.Key |] pair.Value ) 
                                    |> Seq.toArray
            if contentarr.Length > 0 then 
                x.SetContent( contentarr, Array.append [| firstcolumn |] columns )
            else
                x.SetContent( Array.create 1 [| "None" |] , [| firstcolumn |]  )
        else
            x.SetContent( Array.create 1 [| "None" |], [| firstcolumn |]  )

[<AllowNullLiteral>]
type QueryStatisticsWindow( name, perfFunc: QueryPerformance -> int ) = 
    inherit GridWithStringContent(name ) 
    member x.UpdateQueryPerformance( svc: ServiceCollection, qPerf: QueryPerformanceCollection ) = 
        if Utils.IsNotNull svc && Utils.IsNotNull qPerf then 
            // Only place where we formatting data. 
            let nameDic = Dictionary<_,_>( StringComparer.OrdinalIgnoreCase)
            svc.ServiceIDToDomain 
            |> Seq.iter( fun pair -> let name, bIsMega = pair.Value 
                                     if bIsMega then 
                                        nameDic.Item( name ) <- 0
                       )
            let columns = nameDic.Keys |> Seq.sort |> Seq.toArray
            for idx = 0 to columns.Length - 1 do 
                nameDic.Item( columns.[idx] ) <- idx
            let columns = nameDic.Keys |> Seq.toArray
            let guidToIdx = Dictionary<_,_>( )
            for pair in svc.ServiceIDToDomain  do     
                let name, _ = pair.Value
                guidToIdx.Item( pair.Key ) <- nameDic.Item( name ) 
            let header = columns
            let rowDic = ConcurrentDictionary<_,(_)[]>(StringComparer.Ordinal)
            for pair in qPerf.PerformanceCollection do 
                let info, serviceID = pair.Key
                let onePerformance = pair.Value
                let name = info
                let rowArr = rowDic.GetOrAdd( name, fun _ -> Array.create columns.Length "" )
                let idx = guidToIdx.Item( serviceID ) 
                let showPerf = perfFunc onePerformance
                rowArr.[idx] <- sprintf "%d" showPerf
            let contentarr = rowDic |> Seq.map ( fun pair -> let infoarr = [| pair.Key |]
                                                             Array.append infoarr pair.Value ) 
                                    |> Seq.toArray
            if contentarr.Length > 0 then 
                x.SetContent( contentarr, Array.append [| "FrontEnd" |] header )
            else
                x.SetContent( Array.create 1 [| "None" |] , [| "FrontEnd" |]  )
        else
            x.SetContent( Array.create 1 [| "None" |], [| "FrontEnd" |]  )

[<AllowNullLiteral>]
type ServiceCollectionWindow( ) = 
    inherit GridWithStringContent("Service Collection" ) 
    member x.UpdateServiceCollection( svc: ServiceCollection ) = 
        if Utils.IsNotNull svc then 
            // Only place where we formatting data. 
            let nameDic = Dictionary<_,_>( StringComparer.OrdinalIgnoreCase)
            svc.ServiceIDToDomain 
            |> Seq.iter( fun pair -> let name, bIsMega = pair.Value 
                                     if bIsMega then 
                                        nameDic.Item( name ) <- 0
                       )
            let columns = nameDic.Keys |> Seq.sort |> Seq.toArray
            for idx = 0 to columns.Length - 1 do 
                nameDic.Item( columns.[idx] ) <- idx
            let columns = nameDic.Keys |> Seq.toArray
            let guidToIdx = Dictionary<_,_>( )
            for pair in svc.ServiceIDToDomain  do     
                let name, _ = pair.Value
                guidToIdx.Item( pair.Key ) <- nameDic.Item( name ) 
            let header = columns
            let rowDic = ConcurrentDictionary<_,(_)[]>(StringComparer.Ordinal)
            for pair in svc.ServiceCollection do 
                let info, serviceID = pair.Key
                let cnt, domainName = pair.Value
                let name = info
                let rowArr = rowDic.GetOrAdd( name, fun _ -> Array.create columns.Length "" )
                let idx = guidToIdx.Item( serviceID ) 
                rowArr.[idx] <- sprintf "%d" cnt 
            let contentarr = rowDic |> Seq.map ( fun pair -> let infoarr = [| pair.Key |]
                                                             Array.append infoarr pair.Value ) 
                                    |> Seq.toArray
            if contentarr.Length > 0 then 
                x.SetContent( contentarr, Array.append [| "FrontEnd" |] header )
            else
                x.SetContent( Array.create 1 [| "None" |], [| "FrontEnd" |] )
        else
            x.SetContent( Array.create 1 [| "None" |], [| "FrontEnd" |] )


/// launchWindow needs to be started in ApartmentState.STA, hence always should be started in a function
type LaunchWindow(ev:ManualResetEvent) = 
    member val ClusterCollection = ConcurrentDictionary<_,_>(StringComparer.Ordinal) with get
    member val CurrentServiceCollection = null with get, set
    member val CurrentQueryPerformance = null with get, set
    member val NetworkPerfWin : QueryPerformanceWindow = null with get, set
    member val LatencyInfoWin : QueryPerformanceWindow = null with get, set
    member val QueueInfoWin : QueryPerformanceWindow = null with get, set
    member val ClientRequestWin : ClientRequestWindow = null with get, set
    member val ServiceCollectionWin : ServiceCollectionWindow = null with get, set
    member val AvgProcessingWin : QueryStatisticsWindow = null with get, set
    member val MediumProcessingWin : QueryStatisticsWindow = null with get, set
    member val Pct90ProcessingWin : QueryStatisticsWindow = null with get, set
    member val Pct999ProcessingWin : QueryStatisticsWindow = null with get, set
    member val AvgQueueWin : QueryStatisticsWindow = null with get, set
    member val MediumQueueWin : QueryStatisticsWindow = null with get, set
    member val Pct90QueueWin : QueryStatisticsWindow = null with get, set
    member val Pct999QueueWin : QueryStatisticsWindow = null with get, set
    member val AvgNetworkWin : QueryStatisticsWindow = null with get, set
    member val MediumNetworkWin : QueryStatisticsWindow = null with get, set
    member val Pct90NetworkWin : QueryStatisticsWindow = null with get, set
    member val Pct999NetworkWin : QueryStatisticsWindow = null with get, set
    member val AvgAssignmentWin : QueryStatisticsWindow = null with get, set
    member val MediumAssignmentWin : QueryStatisticsWindow = null with get, set
    member val Pct90AssignmentWin : QueryStatisticsWindow = null with get, set
    member val Pct999AssignmentWin : QueryStatisticsWindow = null with get, set

    member val MonitorIntervalInMS = 1000 with get, set
    member x.AddCluster( clusterFileName:string ) = 
        let cl = Prajna.Core.Cluster( clusterFileName )
        if not (Utils.IsNull cl) then 
            x.ClusterCollection.GetOrAdd( clusterFileName, cl )  |> ignore
    member x.GetCluster() = 
        Seq.singleton (Cluster.GetCombinedCluster( x.ClusterCollection.Values ))
    member x.MonitorAll() = 
        x.MonitorClusterNetwork() 
        x.MonitorClientRequests() 
        x.MonitorClusterServiceCollection()
        x.MonitorClusterQuery() 
    member x.MonitorClusterNetwork() = 
        for cl in x.GetCluster() do 
            // Monitor
            let t1 = (DateTime.UtcNow.Ticks)
            let perfDKV0 = DSet<string*float*string*string>(Name="FrondEndPerf", Cluster = cl )
            let perfDKV1 = perfDKV0.Import(null, (VHubWebHelper.GetBackendPerformanceContractName))
            let foldFunc (lst:List<_>) (kv) = 
                let retLst = 
                    if Utils.IsNull lst then 
                        List<_>()
                    else
                        lst
                retLst.Add( kv ) 
                Logger.LogF( LogLevel.ExtremeVerbose, ( fun _ -> sprintf "Machine %s %d records now" RemoteExecutionEnvironment.MachineName retLst.Count ))
                retLst
            let aggrFunc (lst1:List<_>) (lst2:List<_>) = 
                Logger.LogF( LogLevel.ExtremeVerbose, ( fun _ -> sprintf "Machine %s aggregate %d + %d records" RemoteExecutionEnvironment.MachineName lst1.Count lst2.Count ))
                lst1.AddRange( lst2 ) 
                lst1
            let aggrNetworkPerf = perfDKV1 |> DSet.fold foldFunc aggrFunc null
            x.NetworkPerfWin.UpdateQueryPerformance( aggrNetworkPerf ) 
            x.QueueInfoWin.UpdateQueryPerformance( aggrNetworkPerf ) 
            x.LatencyInfoWin.UpdateQueryPerformance( aggrNetworkPerf ) 
            
            Logger.LogF( LogLevel.Info, ( fun _ -> let t2 = (DateTime.UtcNow.Ticks)
                                                   sprintf "get frontend-backend performance statistics for cluster %s in %.2f ms" 
                                                               cl.Name (TimeSpan(t2-t1).TotalMilliseconds) ))
    member x.MonitorClientRequests() = 
        for cl in x.GetCluster() do 
            let perfDKV0 = DSet<_>(Name="ClientRequests", Cluster = cl )
            let perfDKV1 = perfDKV0.Import(null, (VHubWebHelper.ListClientContractName))
            let perfDKV2 = perfDKV1.RowsReorg Int32.MaxValue
            let perfDKV3 = perfDKV2.MapByCollection ClientRequestsFrontEnd.CalculateClientRequests
            let aggrQueryPerf = perfDKV3 |> DSet.fold ClientRequestsFrontEnd.Aggregate ClientRequestsFrontEnd.Aggregate null
            x.ClientRequestWin.UpdateClientRequests( aggrQueryPerf ) 
    member x.MonitorClusterServiceCollection() = 
        for cl in x.GetCluster() do 
            // Monitor
            let t1 = (DateTime.UtcNow.Ticks)
            let serviceDSet0 = DSet<_>(Name="ServiceCollection", Cluster = cl )
            let serviceDSet1 = serviceDSet0.Import(null, ( VHubWebHelper.ListVHubServices ))
            let serviceCollection = serviceDSet1 |> DSet.fold (ServiceCollection.FoldServiceCollection) (ServiceCollection.Aggregate) null
            x.CurrentServiceCollection <- serviceCollection
            x.ServiceCollectionWin.UpdateServiceCollection( serviceCollection )
    member x.MonitorClusterQuery() = 
        for cl in x.GetCluster() do 
            let perfDSet0 = DSet<_>(Name="FrontEndQueryPerf", Cluster = cl )
            let perfDSet1 = perfDSet0.Import(null, (FrontEndInstance<_>.ContractNameFrontEndRequestStatistics))
            let perfDSet2 = perfDSet1.RowsReorg Int32.MaxValue
            let perfDSet3 = perfDSet2.MapByCollection QueryPerformanceCollection.CalculateQueryPerformance
            let aggrQueryPerf = perfDSet3 |> DSet.fold QueryPerformanceCollection.Aggregate QueryPerformanceCollection.Aggregate null
            x.CurrentQueryPerformance <- aggrQueryPerf
            x.AvgProcessingWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.MediumProcessingWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.Pct90ProcessingWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.Pct999ProcessingWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )

            x.AvgQueueWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.MediumQueueWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.Pct90QueueWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.Pct999QueueWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )

            x.AvgNetworkWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.MediumNetworkWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.Pct90NetworkWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.Pct999NetworkWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )

            x.AvgAssignmentWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.MediumAssignmentWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.Pct90AssignmentWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )
            x.Pct999AssignmentWin.UpdateQueryPerformance( x.CurrentServiceCollection, aggrQueryPerf )

    member x.Launch() = 
        // need to initialize in STA thread
        x.NetworkPerfWin <- QueryPerformanceWindow( "Network", "Rtt(ms)", fun tuple -> let _, rtt, _, _ = tuple
                                                                                       sprintf "%.2f" rtt )
        x.LatencyInfoWin <- QueryPerformanceWindow( "Exp Latency", "Query Latency(ms)", fun tuple -> let _, _, info, _ = tuple
                                                                                                     info )
        x.QueueInfoWin <- QueryPerformanceWindow( "Queue", "Queue (ms)", fun tuple -> let _, _, _, info = tuple
                                                                                      info )

        x.ClientRequestWin <- ClientRequestWindow ( "ClientRequest" )
        x.ServiceCollectionWin <- ServiceCollectionWindow() 
        let curfname = Process.GetCurrentProcess().MainModule.FileName
        let defaultRootdir = 
            try
                let curdir = Directory.GetParent( curfname ).FullName
                let upperdir = Directory.GetParent( curdir ).FullName
                let upper2dir = Directory.GetParent( upperdir ).FullName                        
                Path.Combine( upper2dir, "image" )
            with
            | e -> 
                null
        let app = new Application()
        SynchronizationContext.SetSynchronizationContext( new DispatcherSynchronizationContext( Dispatcher.CurrentDispatcher));
        let cv = TabWindowWithLog( 1400., 1000. ) 
        use monitorTimer = new Threading.Timer((fun _ -> Logger.Log(LogLevel.Info, ("Timer for Monitoring Backend Clusters"))
                                                         x.MonitorAll()),
                                                null, x.MonitorIntervalInMS, x.MonitorIntervalInMS)
        /// Add Network Perf window
        cv.AddTab( "Network", x.NetworkPerfWin )
        cv.AddTab( "Exp Latency", x.LatencyInfoWin )
        cv.AddTab( "Queue", x.QueueInfoWin ) 
        cv.AddTab( "ClientRequest", x.ClientRequestWin )
        cv.AddTab( "Service Collection", x.ServiceCollectionWin )

        x.AvgProcessingWin <- QueryStatisticsWindow( "Avg Processing", fun qPerf -> qPerf.AvgInProcessing ) 
        x.MediumProcessingWin <- QueryStatisticsWindow( "Medium Processing", fun qPerf -> qPerf.MediumInProcessing ) 
        x.Pct90ProcessingWin <- QueryStatisticsWindow( "90 Processing", fun qPerf -> qPerf.Pct90InProcessing ) 
        x.Pct999ProcessingWin <- QueryStatisticsWindow( "999 Processing", fun qPerf -> qPerf.Pct999InProcessing ) 
        cv.AddTab( "Avg Processing", x.AvgProcessingWin ) 
        cv.AddTab( "Medium Processing", x.MediumProcessingWin ) 
        cv.AddTab( "90 Processing", x.Pct90ProcessingWin ) 
        cv.AddTab( "999 Processing", x.Pct999ProcessingWin ) 

        x.AvgQueueWin <- QueryStatisticsWindow( "Avg Queue", fun qPerf -> qPerf.AvgInQueue ) 
        x.MediumQueueWin <- QueryStatisticsWindow( "Medium Queue", fun qPerf -> qPerf.MediumInQueue ) 
        x.Pct90QueueWin <- QueryStatisticsWindow( "90 Queue", fun qPerf -> qPerf.Pct90InQueue ) 
        x.Pct999QueueWin <- QueryStatisticsWindow( "999 Queue", fun qPerf -> qPerf.Pct999InQueue ) 
        cv.AddTab( "Avg Queue", x.AvgQueueWin ) 
        cv.AddTab( "Medium Queue", x.MediumQueueWin ) 
        cv.AddTab( "90 Queue", x.Pct90QueueWin ) 
        cv.AddTab( "999 Queue", x.Pct999QueueWin ) 

        x.AvgNetworkWin <- QueryStatisticsWindow( "Avg Network", fun qPerf -> qPerf.AvgInNetwork ) 
        x.MediumNetworkWin <- QueryStatisticsWindow( "Medium Network", fun qPerf -> qPerf.MediumInNetwork ) 
        x.Pct90NetworkWin <- QueryStatisticsWindow( "90 Network", fun qPerf -> qPerf.Pct90InNetwork ) 
        x.Pct999NetworkWin <- QueryStatisticsWindow( "999 Network", fun qPerf -> qPerf.Pct999InNetwork ) 
        cv.AddTab( "Avg Network", x.AvgNetworkWin ) 
        cv.AddTab( "Medium Network", x.MediumNetworkWin ) 
        cv.AddTab( "90 Network", x.Pct90NetworkWin ) 
        cv.AddTab( "999 Network", x.Pct999NetworkWin ) 

        x.AvgAssignmentWin <- QueryStatisticsWindow( "Avg Assignment", fun qPerf -> qPerf.AvgInAssignment ) 
        x.MediumAssignmentWin <- QueryStatisticsWindow( "Medium Assignment", fun qPerf -> qPerf.MediumInAssignment ) 
        x.Pct90AssignmentWin <- QueryStatisticsWindow( "90 Assignment", fun qPerf -> qPerf.Pct90InAssignment ) 
        x.Pct999AssignmentWin <- QueryStatisticsWindow( "999 Assignment", fun qPerf -> qPerf.Pct999InAssignment ) 
        cv.AddTab( "Avg Assignment", x.AvgAssignmentWin ) 
        cv.AddTab( "Medium Assignment", x.MediumAssignmentWin ) 
        cv.AddTab( "90 Assignment", x.Pct90AssignmentWin ) 
        cv.AddTab( "999 Assignment", x.Pct999AssignmentWin ) 

        let mainMenu = Menu( Width = 1425., Height = 100., Margin = Thickness(0.,0.,0.,0.),
                                HorizontalAlignment=HorizontalAlignment.Left,
                                VerticalAlignment=VerticalAlignment.Top, IsMainMenu = true )
        let imagefilename1 = Path.Combine(defaultRootdir, "data-center.png")
        let uri1 = System.Uri(imagefilename1, UriKind.Absolute )
        Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "data-center Icon = %s, Uri = %A " imagefilename1 uri1))
        let imagefilename2 = Path.Combine(defaultRootdir, "computer.png")
        let uri2 = System.Uri(imagefilename2, UriKind.Absolute )

        let img1 = Image( Width=100., Height=100., Source = new BitmapImage( uri1 ) )
        let addClusterMenu = MenuItem( Header = img1 )
        mainMenu.Items.Add( addClusterMenu ) |> ignore
        let img2 = Image( Width=100., Height=100., Source = new BitmapImage( uri2 ) )
        let changeNodeMenu = MenuItem( Header = img2 )
        mainMenu.Items.Add( changeNodeMenu ) |> ignore

        let stackpanel = WindowWithMenu( mainMenu, cv, 1425., 1130. )
        // Note that the initialiazation will complete in another thread
        let win = new Window( Title = sprintf "Monitoring vHub FrontEnd", 
                                Width = 1425., 
                                Height = 1130. )
    //            Dispatcher.CurrentDispatcher.BeginInvoke( Action<_>( fun _ -> win.Content <- cv), DispatcherPriority.Render, [| win :> Object; cv :> Object |] ) |> ignore           
        win.Content <- stackpanel
        win.SizeChanged.Add( fun arg -> 
                                Dispatcher.CurrentDispatcher.BeginInvoke( Action<_>(fun _ ->    stackpanel.ChangeSize( arg.NewSize.Width, arg.NewSize.Height )
                                                                                                cv.ChangeSize( arg.NewSize.Width - 25., arg.NewSize.Height - 130. )), [| cv :> Object |] ) |> ignore )
        win.Closed.Add( fun _ -> ev.Set() |> ignore 
                                 Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background)
                                 Application.Current.Shutdown() )
        app.MainWindow <- win
        win.Show()   
        app.Run() |> ignore


[<EntryPoint>]
[<STAThread>]
let main argv = 
    let logFile = sprintf @"c:\Log\vHub\vHub.MonitorFrontEnd_%s.log" (VersionToString( (DateTime.UtcNow) ))
    let inputargs =  Array.append [| "-log"; logFile |] argv 
    let orgargs = Array.copy inputargs
    let parse = ArgumentParser(orgargs)
    
    let PrajnaClusterFiles = parse.ParseStrings( "-cluster", [||] )

    Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Program %s started  ... " (Process.GetCurrentProcess().MainModule.FileName) ) )
    Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "Execution param %A" inputargs ))

    let bAllParsed = parse.AllParsed Usage
    if bAllParsed then 
        let clusterLst = ConcurrentQueue<_>()
        for file in PrajnaClusterFiles do 
            let cl = Prajna.Core.Cluster( file )
            if cl.NumNodes > 0 then 
                clusterLst.Enqueue( cl ) 
        let ev = new ManualResetEvent( false )
        let x = LaunchWindow( ev )
        for file in PrajnaClusterFiles do 
            x.AddCluster( file )

        let threadStart = new ThreadStart( fun _ -> Logger.Log( LogLevel.Info, ( "Main Thread for vHub Backend monitoring" ))
                                                    x.Launch())
        let thread = new Thread(threadStart)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        ev.WaitOne() |> ignore
        thread.Join()
    else
        parse.PrintUsage( Usage )

    0

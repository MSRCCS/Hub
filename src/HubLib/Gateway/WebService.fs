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
		vHub.WebAPI.fs
  
	Description: 
		WebAPI helper for VHub

	Author:																	
 		Jin Li, Principal Researcher
 		Microsoft Research, One Microsoft Way
 		Email: jinl at microsoft dot com
    Date:
        Feb. 2015
	
 ---------------------------------------------------------------------------*)
namespace VMHub.GatewayWebService

open System
open System.Text
open System.IO
open System.Text
open System.Collections.Generic
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open System.ServiceModel
open System.ServiceModel.Web
open Prajna.Tools
open Prajna.Tools.StringTools
open Prajna.Tools.FSharp
open Prajna.Tools.Network
open Prajna.Core
open Prajna.Service.ServiceEndpoint
open Prajna.Service.Gateway
open VMHub.Data
open VMHub.ServiceEndpoint
open VMHub.Gateway
open Prajna.Tools.HttpBuilderHelper
open Prajna.WCFTools
open Prajna.WCFTools.SafeHttpBuilderHelper
open Prajna.Service.FSharp
open Prajna.Service.CoreServices
open Prajna.Service.CoreServices.Data


[<ServiceBehavior(ConcurrencyMode=ConcurrencyMode.Multiple,InstanceContextMode=InstanceContextMode.Single)>]
[<ServiceContract(Namespace = @"VMHub.GatewayWebService", ConfigurationName = "VHubFrontEndWebService") >]
type VHubFrontEndWebService() =
    let mutable contractGetGatewayPerformance : (unit -> IEnumerable<OneServerMonitoredStatus>) option = None
    member x.VHub with get() = VHubWebHelper.Current

    /// <summary>
    /// Perform a classification.
    /// </summary>
    [<OperationContract>]
    [<WebInvoke(UriTemplate = "/VHub/Classify/{idString}/{distribution}/{aggregation}/{ticks}/{rtt}",
        RequestFormat = WebMessageFormat.Json,
        ResponseFormat = WebMessageFormat.Json,
        BodyStyle = WebMessageBodyStyle.Bare)>]
    member x.VHubClassifyAsync(idString:string, distribution:string, aggregation:string, ticks:string, rtt:string, stream: Stream) =
        try
            let b1, serviceID = Guid.TryParse( idString )
            let b2, distributionID = Guid.TryParse( distribution ) 
            let b3, aggregationID = Guid.TryParse( aggregation )
            let b4, rttInMs = Int32.TryParse( rtt )
            let y = x.VHub
            if b1 && Utils.IsNotNull y then 
                let imageStream = new MemoryStream()
                stream.CopyTo( imageStream )
                let bufLen = int imageStream.Position
                let buf = imageStream.GetBuffer()
                let req = RecogRequest( Data = Array.sub buf 0 bufLen, 
                                        AuxData = null )
                let taskSource = TaskCompletionSource<RecogReply>()
                let address, port = VHubWebHelper.GetRemoteEndpoint()
                y.ReceiveRequest( Guid.NewGuid(), Guid.Empty, Guid.Empty, Guid.Empty, serviceID, distributionID, aggregationID, req, 
                                        (DateTime.UtcNow.Ticks), rttInMs, address, port, Action<_>( x.ProcessVHubReply taskSource) )
                taskSource.Task
            else
                new Task<_> ( fun _ -> null )   
        with 
        | e -> 
            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "!!! Exception !!! classifyImageWithSourceAsync fail with %A" e ))
            let msg = sprintf "Exception %A" e
            new Task<_> ( fun _ -> null )   
    /// <summary>
    /// Perform a classification.
    /// </summary>
    [<OperationContract>]
    [<WebInvoke(UriTemplate = "/VHub/ServiceAPI/{provideID}/{inSchema}/{outSchema}/{idString}/{distribution}/{aggregation}/{customer}/{ticks}/{rtt}/{secret}",
        RequestFormat = WebMessageFormat.Json,
        ResponseFormat = WebMessageFormat.Json,
        BodyStyle = WebMessageBodyStyle.Bare)>]
    member x.VHubServiceAsync( provider: string, inSchemaString: string, outSchemaString: string, domain:string, distribution:string, aggregation:string, 
                                    customer:string, ticks:string, rtt:string, secret:string, stream: Stream) =
        try
            VHubWebHelper.RegisterClientActivity( x.VHub, rtt, "ServiceAPI" )
            let b1, providerID = Guid.TryParse( provider )
            let b2, inputSchemaID = Guid.TryParse( inSchemaString )
            let b2b, outputSchemaID = Guid.TryParse( outSchemaString )
            let b3, domainID = Guid.TryParse( domain )
            let b4, distributionID = Guid.TryParse( distribution ) 
            let b5, aggregationID = Guid.TryParse( aggregation )
            let b6, rttInMs = Int32.TryParse( rtt )
            let y = x.VHub
            let errorMsg = StringBuilder()
            if Utils.IsNull y then
                errorMsg.Append( "VM Hub has not been initialized" ).Append( Environment.NewLine ) |> ignore
            if not b1 then 
                errorMsg.Append( sprintf "Fail to pass provider ID %s" provider ).Append( Environment.NewLine ) |> ignore
            if not b2 then 
                errorMsg.Append( sprintf "Fail to pass input schema ID %s" inSchemaString ).Append( Environment.NewLine ) |> ignore
            if not b2b then 
                errorMsg.Append( sprintf "Fail to pass output schema ID %s" outSchemaString ).Append( Environment.NewLine ) |> ignore
            if not b3 then 
                errorMsg.Append( sprintf "Fail to pass domainID ID %s" domain ).Append( Environment.NewLine ) |> ignore
            let errMsg = errorMsg.ToString()
            if errMsg.Length <= 0  then 
                let imageStream = new MemoryStream()
                stream.CopyTo( imageStream )
                let bufLen = int imageStream.Position
                let buf = imageStream.GetBuffer()
                let req = RecogRequest( Data = Array.sub buf 0 bufLen, 
                                        AuxData = null )
                let taskSource = TaskCompletionSource<RecogReply>()
                let address, port = VHubWebHelper.GetRemoteEndpoint()
                y.ReceiveRequest( Guid.NewGuid(), providerID, inputSchemaID, outputSchemaID, domainID, distributionID, aggregationID, req, 
                    (DateTime.UtcNow.Ticks), rttInMs, address, port, Action<_>( x.ProcessVHubReply taskSource) )
                taskSource.Task
            else
                let ret = RecogReply( Description = "Request parsing error : " + Environment.NewLine + errMsg, 
                                        Confidence = 0.0, 
                                        PerfInformation = "", 
                                        Result = null, 
                                        AuxData = null
                                        )
                new Task<_> ( fun _ -> ret )   
        with 
        | e -> 
            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "!!! Exception !!! classifyImageWithSourceAsync fail with %A" e ))
            let msg = sprintf "Exception %A" e
            new Task<_> ( fun _ -> null )      
    /// --------------------------------------------------------
    /// Jin Li: The following is set of Web Interface for VHub
    /// --------------------------------------------------------
    /// Get Active Gateways
    [<OperationContract>]
    [<WebGet(UriTemplate = "/VHub/GetActiveGateways/{customer}/{ticks}/{rtt}/{secret}",
        RequestFormat = WebMessageFormat.Json,
        ResponseFormat = WebMessageFormat.Json,
        BodyStyle = WebMessageBodyStyle.Bare)>]
    member x.GetActiveGateways( customer: string, ticks: string, rtt: string, secret: string) = 
        if Utils.IsNull x.VHub then 
            Array.empty 
        else
            VHubWebHelper.RegisterClientActivity( x.VHub, rtt, "GetActiveGateways" )
            if Option.isNone contractGetGatewayPerformance then 
                contractGetGatewayPerformance <- Some( ContractStore.ImportSeqFunction<_>( null, MonitorWebServiceParam.GetServicePerformanceContractName ) )
            match contractGetGatewayPerformance with
            | None -> 
                Array.empty
            | Some func -> 
                let gatewayInfo = func()
                if Utils.IsNull gatewayInfo then 
                    Array.empty
                else
                    gatewayInfo |> Seq.toArray

    /// <summary>
    /// Perform a classification.
    /// </summary>
    [<OperationContract>]
    [<WebInvoke(UriTemplate = "/VHub/Process/{provider}/{schema}/{domain}/{distribution}/{aggregation}/{customer}/{ticks}/{rtt}/{secret}",
        RequestFormat = WebMessageFormat.Json,
        ResponseFormat = WebMessageFormat.Json,
        BodyStyle = WebMessageBodyStyle.Bare)>]
    member x.VHubProcess(provider:string, schema:string, domain: string, distribution:string, aggregation:string, 
                            customer: string, ticks: string, rtt: string, secret: string, stream: Stream) =
        try
            VHubWebHelper.RegisterClientActivity( x.VHub, rtt, "Process" )
            let b1, providerID = Guid.TryParse( provider )
            let b2, schemaID = Guid.TryParse( schema )
            let b3, domainID = Guid.TryParse( domain )
            let b4, distributionID = Guid.TryParse( distribution ) 
            let b5, aggregationID = Guid.TryParse( aggregation )
            let b6, rttInMs = Int32.TryParse( rtt )
            let y = x.VHub
            let errorMsg = StringBuilder()
            if Utils.IsNull y then
                errorMsg.Append( "VM Hub has not been initialized" ).Append( Environment.NewLine ) |> ignore
            if not b1 then 
                errorMsg.Append( sprintf "Fail to pass provider ID %s" provider ).Append( Environment.NewLine ) |> ignore
            if not b2 then 
                errorMsg.Append( sprintf "Fail to pass schema ID %s" schema ).Append( Environment.NewLine ) |> ignore
            if not b3 then 
                errorMsg.Append( sprintf "Fail to pass domainID ID %s" domain ).Append( Environment.NewLine ) |> ignore
            let errMsg = errorMsg.ToString()
            if errMsg.Length <= 0  then 
                let imageStream = new MemoryStream()
                stream.CopyTo( imageStream )
                let bufLen = int imageStream.Position
                let buf = imageStream.GetBuffer()
                let req = RecogRequest( Data = Array.sub buf 0 bufLen, 
                                        AuxData = null )
                let taskSource = TaskCompletionSource<RecogReply>()
                let address, port = VHubWebHelper.GetRemoteEndpoint()
                y.ReceiveRequest( Guid.NewGuid(), providerID, Guid.Empty, Guid.Empty, domainID, distributionID, aggregationID, req, 
                    (DateTime.UtcNow.Ticks), rttInMs, address, port, Action<_>( x.ProcessVHubReply taskSource) )
                taskSource.Task
            else
                let ret = RecogReply( Description = "Request parsing error : " + Environment.NewLine + errMsg, 
                                        Confidence = 0.0, 
                                        PerfInformation = "", 
                                        Result = null, 
                                        AuxData = null
                                        )
                new Task<_> ( fun _ -> ret )   
        with 
        | e -> 
            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "!!! Exception !!! classifyImageWithSourceAsync fail with %A" e ))
            let msg = sprintf "Exception %A" e
            new Task<_> ( fun _ -> null )   


    [<OperationContract>]
    [<WebGet(UriTemplate = "/VHub/GetActiveProviders/{customer}/{ticks}/{rtt}/{secret}",
        RequestFormat = WebMessageFormat.Json,
        ResponseFormat = WebMessageFormat.Json,
        BodyStyle = WebMessageBodyStyle.Bare)>]
    member x.GetActiveProviders( customer: string, ticks: string, rtt: string, secret: string) = 
        if Utils.IsNull x.VHub then 
            null 
        else
            VHubWebHelper.RegisterClientActivity( x.VHub, rtt, "GetActiveProviders" )
            x.VHub.GetActiveProviders()

    [<OperationContract>]
    [<WebGet(UriTemplate = "/VHub/GetWorkingInstances/{engineName}/{customer}/{ticks}/{rtt}/{secret}",
        RequestFormat = WebMessageFormat.Json,
        ResponseFormat = WebMessageFormat.Json,
        BodyStyle = WebMessageBodyStyle.Bare)>]
    member x.GetWorkingInstances( engineName:string, customer: string, ticks: string, rtt: string, secret: string) = 
        if Utils.IsNull x.VHub then 
            null 
        else
            VHubWebHelper.RegisterClientActivity( x.VHub, rtt, "GetWorkingInstances" )
            x.VHub.GetWorkingInstances(engineName)

    [<OperationContract>]
    [<WebGet(UriTemplate = "/VHub/GetAllServiceGuids/{ticks}/{rtt}",
        RequestFormat = WebMessageFormat.Json,
        ResponseFormat = WebMessageFormat.Json,
        BodyStyle = WebMessageBodyStyle.Bare)>]
    member x.GetAllServiceGuids( ticks: string, rtt: string) = 
        if Utils.IsNull x.VHub then 
            Array.empty 
        else
            VHubWebHelper.RegisterClientActivity( x.VHub, rtt, "GetAllServiceGuids" )
            x.VHub.GetActiveInstances() |> Seq.map ( fun tuple -> let id, _, _ = tuple
                                                                  id ) |> Seq.toArray

    /// The following is set of Web Interface for VHub
    [<OperationContract>]
    [<WebGet(UriTemplate = "/VHub/GetServiceByGuids/{serviceIDString}/{ticks}/{rtt}",
        RequestFormat = WebMessageFormat.Json,
        ResponseFormat = WebMessageFormat.Json,
        BodyStyle = WebMessageBodyStyle.Bare)>]
    member x.GetServicesByID( serviceIDString: string, ticks: string, rtt: string) = 
        if Utils.IsNull x.VHub then 
            null
        else
            VHubWebHelper.RegisterClientActivity( x.VHub, rtt, "GetServiceByGuids" )
            let bSuccess, serviceID = Guid.TryParse( serviceIDString )
            if bSuccess then 
                x.VHub.GetRecogInstance( serviceID ) 
            else
                Logger.LogF( LogLevel.MildVerbose, ( fun _ -> sprintf "Incorrectly Formattted GetServiceByGuids with service ID: %s, ticks %s, rtt %s" 
                                                                       serviceIDString ticks rtt ))
                null
    
    /// Obtain the VHubBlob in synchronous fashion. 
//    [<OperationContract>]
//    [<WebGet(UriTemplate = "/VHub/GetBlobByID/{guidID}/{ticks}/{rtt}",
//        RequestFormat = WebMessageFormat.Json,
//        ResponseFormat = WebMessageFormat.Json,
//        BodyStyle = WebMessageBodyStyle.Bare)>]
//    member x.GetBlobByID( guidID: string, ticks: string, rtt: string) = 
//        if Utils.IsNull x.VHub then 
//            VHubBlob( Data = null, TypeInfo = null )
//        else
//            VHubWebHelper.RegisterClientActivity( x.VHub, rtt, "GetBlobByID" )
//            let bSuccess, blobID = Guid.TryParse( guidID )
//            if bSuccess then 
//                x.VHub.GetBlobByID( blobID ) 
//            else
//                Logger.LogF(LogLevel.MildVerbose, ( fun _ -> sprintf "Incorrectly Formattted GetServiceByGuids with service ID: %s, ticks %s, rtt %s" 
//                                                                        guidID ticks rtt ))
//                VHubBlob( Data = null, TypeInfo = null )

    /// Obtain the VHubBlob in asynchronous fashion. 
    [<OperationContract>]
    [<WebGet(UriTemplate = "/VHub/GetBlobByIDAsync/{guidID}/{ticks}/{rtt}",
        RequestFormat = WebMessageFormat.Json,
        ResponseFormat = WebMessageFormat.Json,
        BodyStyle = WebMessageBodyStyle.Bare)>]
    member x.GetBlobByIDAsync( guidID: string, ticks: string, rtt: string) = 
        if Utils.IsNull x.VHub then 
            new Task<_>( fun _ -> VHubBlob( Data = null, TypeInfo = null ) )
        else
            VHubWebHelper.RegisterClientActivity( x.VHub, rtt, "GetBlobByID" )
            let bSuccess, blobID = Guid.TryParse( guidID )
            if bSuccess then 
                x.VHub.GetBlobByIDAsync( blobID ) 
            else
                Logger.LogF( LogLevel.MildVerbose, ( fun _ -> sprintf "Incorrectly Formattted GetServiceByGuids with service ID: %s, ticks %s, rtt %s" 
                                                                       guidID ticks rtt ))
                new Task<_>( fun _ -> VHubBlob( Data = null, TypeInfo = null ) )



    member x.ProcessVHubReply taskSource (perfQ, result) =
        match result with 
        | :? RecogReply as reply -> 
            reply.PerfInformation <- perfQ.FrontEndInfo()
            taskSource.SetResult( reply )
        | _ -> 
            taskSource.SetResult( null )

    /// The following is set of Web Interface usable for human. They are not visible to the user. 
    [<OperationContract>]
    [<WebGet(UriTemplate = "/SyncLog.html" )>]
    member x.GetSyncLog( ) =
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "SyncLog.html" )
        let logFile = Logger.GetLogFile()
        let log = 
            if Utils.IsNotNull logFile then 
                try 
                    use logF = new FileStream( logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite )
                    use readStream = new StreamReader( logF ) 
                    let recentReadStr = readStream.ReadToEnd()
                    readStream.Close()
                    logF.Close()
                    recentReadStr
                with 
                | e -> 
                    let msg = sprintf "Error in reading log file %s: %A" logFile e
                    msg
            else
                "No Input Filename"
        HttpTools.PlainTextToHttpStream( log )
    [<OperationContract>]
    [<WebGet(UriTemplate = "/Log.html")>]
    member x.GetLogAsync(  ) : Task<Stream> =
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "Log.html" )
        let logFile = Logger.GetLogFile()
        HttpTools.PlainTextToWebPage ( WebOperationContext.Current, WebCache.TaskReadFile( logFile ) )

    [<OperationContract>]
    [<WebGet(UriTemplate = "/Log{dstr}.html")>]
    member x.GetLogAsyncWithNumber( dstr: string ) : Task<Stream> =
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "Log"+dstr+".html" )
        let dref = ref 0
        Int32.TryParse( dstr, dref ) |> ignore
        let d = !dref
        let logTask = 
            let logFile = Logger.GetLogFile()
            if d <= 0 then 
                WebCache.TaskReadFile( logFile )
            else
                let logDir = Path.GetDirectoryName( logFile )
                let allLogs = Directory.GetFiles( logDir ) |> Array.rev
                if d >= allLogs.Length then 
                    WebCache.TaskReadFile( logFile )
                else
                    WebCache.TaskReadFile( allLogs.[d] )
        HttpTools.PlainTextToWebPage( WebOperationContext.Current, logTask )
    [<OperationContract>]
    [<WebGet(UriTemplate="/Test/{s}/{t}")>]
    member x.Test(s:string, t:string) : Stream =
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "Test" )
        let html = sprintf @"<!DOCTYPE html>\n\
            <html><body>Called with '%s' and '%s'</body></html>" s t
        upcast new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html))
    /// Infrequently used, thus synchronous mode
    [<OperationContract>]
    [<WebGet(UriTemplate = "/WebAsync/{fname}")>]
    member x.AsyncServeWebFile( fname: string ) = 
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "WebAsync/"+fname )
        let webRoot = VHubSetting.WebRoot
        let fileName = Path.Combine( webRoot, fname.Replace('\\', '/' ) )
        if File.Exists fileName then 
            HttpTools.FileToWebStreamAsync ( WebOperationContext.Current, fileName )
        else
            HttpTools.FileToWebStreamAsync ( WebOperationContext.Current, (Path.Combine( webRoot, VHubSetting.FileNotExist )) )
    [<OperationContract>]
    [<WebGet(UriTemplate = "/Web/{fname}")>]
    member x.ServeWebFile( fname: string ) = 
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "Web/"+fname )
        let content, meta = WebCache.retrieve( fname )
        if Utils.IsNull content then 
            let content, meta = WebCache.retrieve( VHubSetting.FileNotExist )
            HttpTools.ContentToHttpStreamWithMetadata ( WebOperationContext.Current, content, meta )
        else
            HttpTools.ContentToHttpStreamWithMetadata ( WebOperationContext.Current, content, meta )
    [<OperationContract>]
    [<WebGet(UriTemplate = "/GatewayPerformance.html")>]
    member x.GatewayPerformance( ) = 
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "GatewayPerformance.html" )
        let content, meta = WebCache.retrieve( VHubSetting.TemplateForGatewayPerformance )
        match contractGetGatewayPerformance with 
        | None -> 
            contractGetGatewayPerformance <- Some( ContractStore.ImportSeqFunction<_>( null, MonitorWebServiceParam.GetServicePerformanceContractName ) )
        | Some _ -> 
            ()
        match contractGetGatewayPerformance with
        | None -> 
            HttpTemplateBuilder.ServeTable( WebOperationContext.Current, content, 
                                              VHubSetting.ContentSlot0,
                                              WrapText (sprintf "Monitor Gateway Performance Contract %s is not found. " MonitorWebServiceParam.GetServicePerformanceContractName ), 
                                              VHubSetting.ContentSlot1,
                                              Seq.empty )
        | Some func -> 
            let gatewayInfo = func()
            let displayInfo = 
                gatewayInfo
                |> Seq.map( fun status -> [| WrapText status.HostName; 
                                             WrapText (System.Net.IPAddress( status.Address ).ToString()); 
                                             WrapText (TimeSpan(0, 0, int status.PeriodMonitored).ToString());
                                             WrapText (TimeSpan(0, 0, int status.PeriodAlive).ToString());
                                             WrapText (TimeSpan(0, 0, int status.GatewayAlive).ToString());
                                             WrapText (status.RttInMs.ToString());
                                             WrapText (status.PerfInMs.ToString());
                                             |] :> seq<_> )
            HttpTemplateBuilder.ServeTable( WebOperationContext.Current, content, 
                                              VHubSetting.ContentSlot0,
                                              WrapText ("Monitor Gateway Performance"  ), 
                                              VHubSetting.ContentSlot1,
                                              displayInfo )
    [<OperationContract>]
    [<WebGet(UriTemplate = "/RegisteredContent.html")>]
    member x.ServeRegisteredContent( ) = 
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "RegisteredContent.html" )
        let info = WebCache.snapShot()
        let content, meta = WebCache.retrieve( VHubSetting.TemplateForRegisteredContent )
        let serveInfo = info |> Seq.map( fun pair -> let _, meta = pair.Value
                                                     [| FormLink (WrapRaw( @"Web/"+pair.Key)) (WrapText pair.Key ); WrapText meta |] :> seq<_> )
        HttpTemplateBuilder.ServeTable( WebOperationContext.Current, content, 
                                          VHubSetting.ContentSlot0,
                                          WrapText (sprintf "Number of registered content: %d" info.Length), 
                                          VHubSetting.ContentSlot1,
                                          serveInfo )
    [<OperationContract>]
    [<WebGet(UriTemplate = "/RegisteredProviders.html")>]
    member x.ServeRegisteredProvider( ) = 
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "RegisteredProviders.html" )
        let info = VHubProviderList.toSeq()
        let content, meta = WebCache.retrieve( VHubSetting.TemplateForProviderRoster )
        let serveInfo = info |> Seq.map( fun entry -> VHubWebHelper.ToHtml entry )
        HttpTemplateBuilder.ServeTable( WebOperationContext.Current, content, 
                                          VHubSetting.ContentSlot0,
                                          WrapText (sprintf "Registered Providers: %d" (Seq.length info) ), 
                                          VHubSetting.ContentSlot1,
                                          serveInfo )
    [<OperationContract>]
    [<WebGet(UriTemplate = "/HostStatus.html")>]
    member x.ServeHostStatus( ) = 
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "HostStatus.html" )
        let serveFunc (sb) = 
            let currentProcess = System.Diagnostics.Process.GetCurrentProcess()
            let memInMB = currentProcess.WorkingSet64>>>20
            Logger.Log( LogLevel.ExtremeVerbose, ("Obtained Memory Information."))
            WrapParagraph ( WrapText (sprintf "Memory Usage:%d (MB)" memInMB ) ) sb |> ignore
            sb
        let content, meta = WebCache.retrieve( VHubSetting.TemplateForHostStatus )
        HttpTemplateBuilder.ServeContent( WebOperationContext.Current, content, 
                                            VHubSetting.ContentSlot0,
                                            serveFunc )
    [<OperationContract>]
    [<WebGet(UriTemplate = "/RegisteredInstances.html")>]
    member x.ShowRegisteredClassifers( ) = 
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "RegisteredInstances.html" )
        let content, meta = WebCache.retrieve( "RegisteredInstances.html" )
        HttpTemplateBuilder.ServeTable( WebOperationContext.Current, content, 
                                            VHubSetting.ContentSlot0,
                                            WrapRaw (sprintf "<b> Registered Backendss :%d </b>" ( x.VHub.BackEndHealth.Count ) ), 
                                            VHubSetting.ContentSlot1, 
                                            VHubWebHelper.HtmlRegisteredBackends( x.VHub ) )
    [<OperationContract>]
    [<WebGet(UriTemplate = "/ActiveClients.html")>]
    member x.ShowActiveClients( ) = 
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "ActiveClients.html" )
        let content, meta = WebCache.retrieve( "ActiveClients.html" )
        HttpTemplateBuilder.ServeTable( WebOperationContext.Current, content, 
                                            VHubSetting.ContentSlot0,
                                            WrapRaw (sprintf "<b> Active Clients :%d </b>" ( x.VHub.ClientTracking.Count ) ), 
                                            VHubSetting.ContentSlot1, 
                                            VHubWebHelper.ActiveClients( x.VHub ) )    
    [<OperationContract>]
    [<WebGet(UriTemplate = "/ExposedClassifiers.html")>]
    member x.ShowExposedClassifers( ) = 
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "ExposedClassifiers.html" )
        let content, meta = WebCache.retrieve( "ExposedClassifiers.html" )
        HttpTemplateBuilder.ServeTable( WebOperationContext.Current, content, 
                                            VHubSetting.ContentSlot0,
                                            WrapRaw (sprintf "<b> Exposed Classifiers to Apps: %d (Unique mega classifier is not shown)</b> " x.VHub.ServiceCollectionByID.Count ), 
                                            VHubSetting.ContentSlot1, 
                                            VHubWebHelper.HtmlExposedRegisteredInstances ( x.VHub ) )
    [<OperationContract>]
    [<WebGet(UriTemplate = "/ActiveClassifiers.html")>]
    member x.ShowActiveClassifers( ) = 
        VHubWebHelper.RegisterClientActivity( x.VHub, null, "ActiveClassifiers.html" )
        let content, meta = WebCache.retrieve( "ActiveClassifiers.html" )
        HttpTemplateBuilder.ServeTable( WebOperationContext.Current, content, 
                                            VHubSetting.ContentSlot0,
                                            WrapRaw (sprintf "<b> Active Classifiers</b> " ), 
                                            VHubSetting.ContentSlot1, 
                                            VHubWebHelper.HtmlActiveRegisteredInstances ( x.VHub ) )





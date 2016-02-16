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
open VMHub.ServiceEndpoint
open VMHub.Gateway
open Prajna.Tools.HttpBuilderHelper
open Prajna.WCFTools.SafeHttpBuilderHelper

open Prajna.Service.FSharp

type VHubWebHelper() =
    static member val Current = null with get, set
    static member val SvcHost : WebServiceHost = null with get, set
    static member val SvcEndPoint : System.ServiceModel.Description.ServiceEndpoint = null with get, set
    static member val SvcBehavior : System.ServiceModel.Description.ServiceMetadataBehavior = null with get, set
    static member val RecogObjectContractName = "RecogObject" with get
    static member val ListClientContractName = "ListClients" with get
    static member val ListVHubServices = "ListVHubServices" with get
    static member val GetBackendPerformanceContractName = "GetBackendPerformance" with get

    static member Init() = 
        let inst = VHubFrontEndInstance<VHubFrontendStartParam>()
        VHubWebHelper.Current <- inst
        // Exported current backend server list and their performance information 
        ContractStore.ExportSeqFunction( VHubWebHelper.GetBackendPerformanceContractName, inst.GetBackendPerformance, -1, true )
        // Export current client lists (request to front end ) and what the client are doing
        ContractStore.ExportSeqFunction( VHubWebHelper.ListClientContractName, (fun _ -> inst.ListClientsByIP(-1)), -1, true )
        // Export current service collections
        ContractStore.ExportSeqFunction( VHubWebHelper.ListVHubServices, inst.ListVHubServices, -1, true )
        // Export a recognition interface
        ContractStore.ExportFunctionTaskWithParam( VHubWebHelper.RecogObjectContractName, inst.RecogObject, true ) 

        inst
    static member ReleaseAll() = 
        VHubWebHelper.Current <- null 
        VHubWebHelper.SvcHost <- null 
        VHubWebHelper.SvcEndPoint <- null 
        VHubWebHelper.SvcBehavior <- null 
    static member getProviderNameWithLink( connectionID ) (sb:System.Text.StringBuilder) = 
        let provider = VHubProviderList.getProvider( connectionID ) 
        if Utils.IsNull provider then 
            sb
        else
            FormLink ( WrapRaw provider.InstituteURL ) (WrapText provider.RecogEngineName) sb
    static member ToHtml( x: VHubProviderEntry ) =
        [| WrapText (x.RecogEngineID.ToString()) ; 
           WrapText (x.RecogEngineName);
           FormLink( WrapRaw x.InstituteURL) (WrapText x.InstituteName ); 
           WrapText x.ContactEmail |] :> seq<_>
// No need to register & unregister image. 
//    member x.RegisterImage() = 
//        let hashcode, url = WebCacheRegisterable.registerItem( null, x.SampleImage, x.ImageContentType ) 
//        x.HashCode <- hashcode
//        x.URL <- url
//    member x.UnregisterImage() = 
//        WebCacheRegisterable.unregisterItem( x.HashCode, x.URL )
    static member ImageURLByGuid (id:Guid) = 
        let buf = OwnershipTracking.Current.GetCacheableBuffer( id ) 
        if Utils.IsNull buf.TypeInfo then 
            if Utils.IsNull buf.Buffer then 
                "null"
            else
                let name = id.ToString()
                WebCache.addItem( name, buf.Buffer )
                name
        else
            let typ = System.Text.Encoding.UTF8.GetString(buf.TypeInfo)
            let name = id.ToString() + "." + typ
            WebCache.addItem( name, buf.Buffer )
            name
    static member ImageURL (x:VHubServiceInstance) = 
        VHubWebHelper.ImageURLByGuid x.SampleImageGuid
    static member RelativeURLBuilder (x:VHubServiceInstance) (sb:StringBuilder ) = 
        sb.Append("Web/").Append( VHubWebHelper.ImageURL x )
    static member AbsoluteURLBuilder (x:VHubServiceInstance) (sb:StringBuilder ) =
        sb.Append( VHubSetting.GlobalVisibleMonitorServiceUrl ).Append("Web/").Append( VHubWebHelper.ImageURL x )
    static member AbsoluteImageURL (x:VHubServiceInstance) = 
        VHubSetting.GlobalVisibleMonitorServiceUrl + "Web/" + ( VHubWebHelper.ImageURL x )
    static member AbsoluteImageURLByGuid (id:Guid ) = 
        VHubSetting.GlobalVisibleMonitorServiceUrl + "Web/" + ( VHubWebHelper.ImageURLByGuid id )
    static member BuildImageItemRelative (x:VHubServiceInstance) (width:int) (height:int) (sb:StringBuilder) = 
        let sb1 = sb.Append( "<img src=\"" ) |> VHubWebHelper.RelativeURLBuilder x
        let sb2 = sb1.Append( "\" alt=\"").Append( x.RecogDomain ).Append("\" " )
        let sb3 = if width > 0 then sb2.Append( "width=" ).Append( width.ToString() ) else sb2
        let sb4 = if height > 0 then sb3.Append( "height=" ).Append( height.ToString() ) else sb3
        sb4.Append( ">" )
    static member GetRemoteEndpoint() = 
        try
            let prop = OperationContext.Current.IncomingMessageProperties
            let remoteEndPoint = prop.Item( System.ServiceModel.Channels.RemoteEndpointMessageProperty.Name) 
            let addressString = ref "Unknown"
            let port = ref 0
            if not ( Utils.IsNull remoteEndPoint ) then 
                match remoteEndPoint with 
                | :? System.ServiceModel.Channels.RemoteEndpointMessageProperty as rp ->
                    addressString := rp.Address
                    port := rp.Port
                | _ -> 
                    ()
            !addressString, (!port)
        with 
        | e -> 
            let msg = sprintf "Exception %A" e
            Logger.LogF( LogLevel.Info, ( fun _ -> sprintf "!!! Exception !!! At GetRemoteEndpoint, %s" msg ))
            msg, 0
    static member RegisterClientActivity( x: VHubFrontEndInstance<_>, rttString, info: string ) = 
        if not(Utils.IsNull x) then 
            let addressByClient, portByClient = VHubWebHelper.GetRemoteEndpoint()
            let bParse, rttSentByClient = Int32.TryParse( rttString )
            x.RegisterClient( addressByClient, portByClient, rttSentByClient, info )
    static member HtmlRegisteredBackends (x: VHubFrontEndInstance<_>) = 
        if Utils.IsNull x then 
            Seq.empty
        else
            let allBackEnds = x.GetActiveBackends()
            let showProviderInfo (tuple:int64*VHubBackEndPerformance) = 
                let remoteSignature, perf = tuple 
                let providerInstance = perf.AppInfo
            
                [| (VHubWebHelper.getProviderNameWithLink( providerInstance.ProviderID ));
                   (WrapText ( providerInstance.HostName )); 
                   (WrapRaw ( providerInstance.VersionString )); 
                   (WrapRaw ( LocalDNS.GetShowInfo( LocalDNS.Int64ToIPEndPoint( remoteSignature ) )) );
                   (if Utils.IsNull perf then Operators.id else WrapRaw ( sprintf "%A" ((DateTime.UtcNow).Subtract( perf.StartTime) )) );
                   (if Utils.IsNull perf then Operators.id else WrapRaw ( sprintf "%A" ((DateTime.UtcNow).Subtract( perf.LastSendTicks) )) );
                   (if Utils.IsNull perf then Operators.id else WrapRaw ( sprintf "%A" (perf.NumCompletedQuery )) );
                   (if Utils.IsNull perf then Operators.id else WrapRaw ( sprintf "%d(%d)" (0) (0) ))
                |] :> seq<_>            
    //        let showClassiferInfo ( cl:IMClassifierInfoBriefServer ) = 
    //            seq { 
    //                yield Operators.id
    //                // yield WrapRaw ( sprintf "Image(%dB)" cl.SampleImage.Length )
    //                yield (cl.BuildImageItemRelative 100 0)
    //                yield (fun sb -> sb.Append(">=").Append( cl.LeastSmallestDimension.ToString()).Append("*").Append(cl.LeastSmallestDimension.ToString()) )
    //                for domain in cl.Domains do
    //                    yield WrapText domain
    //            }
            seq {
                for tuple in allBackEnds do
                    let remoteSignature, perf = tuple 
                    if not (Utils.IsNull perf.AppInfo ) then 
                        yield (showProviderInfo tuple)
    //                for cl in pair.Value.Classifiers do
    //                    yield ( showClassiferInfo cl )   
           }

    /// <summary>
    /// Show information of the clients that are visiting the frontend.
    /// </summary>
    static member ActiveClients (x: VHubFrontEndInstance<_>) = 
        if Utils.IsNull x then 
            Seq.empty
        else
            let allActiveClients = x.ListClients( -1 )
            seq {
                for tuple in allActiveClients do
                    yield (VHubWebHelper.OneClientInfo tuple)
           }
    static member OneClientInfo tuple = 
        let tCur = (DateTime.UtcNow)
        let addrString, rtt, ticks, info = tuple
        let elapse = tCur.Subtract(DateTime(ticks)).TotalSeconds
        [|  (WrapText ( addrString )); 
            (WrapRaw ( rtt.ToString() )); 
            (WrapRaw ( elapse.ToString("F")) );
            (WrapText ( info ))
        |] :> seq<_>            

    /// <summary> 
    /// Show HTML of registered classifers
    /// </summary> 
    static member HtmlExposedRegisteredInstances (x: VHubFrontEndInstance<_>) = 
        if Utils.IsNull x then 
            Seq.empty
        else
            let allInstances = x.GetActiveInstances()
            seq {
                for pair in allInstances do
                    yield (VHubWebHelper.ShowWorkingClassifier pair)
            }


    static member ShowWorkingClassifier tuple = 
        let megaID, vHubInstance, remoteSignatures = tuple
        let dim = vHubInstance.LeastSmallestDimension
        [| (WrapRaw (megaID.ToString()));
            (WrapText ( if StringTools.IsNullOrEmpty( vHubInstance.EngineName) then vHubInstance.RecogDomain else vHubInstance.RecogDomain + "@" + vHubInstance.EngineName ));
            (WrapText ( vHubInstance.RecogDomain )); 
            (WrapText ( vHubInstance.EngineName ));
            (WrapText ( (Seq.length remoteSignatures).ToString() ));
            (VHubWebHelper.BuildImageItemRelative vHubInstance 100 0);
            (fun sb -> sb.Append(">=").Append( dim.ToString()).Append("*").Append(dim.ToString()))
        |] :> seq<_>            
    static member HtmlActiveRegisteredInstances (x: VHubFrontEndInstance<_>) = 
        if Utils.IsNull x then 
            Seq.empty
        else
            let allInstances = x.GetActiveInstances() |> Seq.filter ( fun tuple -> let megaID, vHubInstance, remoteSignatures = tuple
                                                                                   not (StringTools.IsNullOrEmpty( vHubInstance.EngineName) ) )
            seq {
                for pair in allInstances do
                    yield (VHubWebHelper.ShowWorkingClassifier pair)
            }

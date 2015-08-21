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
		policy.fs
  
	Description: 
		VHub FilteringPolicy, ProcessingPolicy, QueryPolicy

	Author:																	
 		Jin Li, Principal Researcher
 		Microsoft Research, One Microsoft Way
 		Email: jinl at microsoft dot com
    Date:
        Nov. 2014
	
 ---------------------------------------------------------------------------*)
namespace VMHub.Gateway

open System
open System.IO
open System.Collections.Concurrent
open Prajna.Tools
open Prajna.Tools.FSharp

open VMHub.Data
  
/// <summary> 
/// A Provider Registration enty is information that all instances of the applications from that particular provider will share. 
/// The information include:
/// ConnectionGuid for spam deterrence. 
/// Locale that is used by System.Globalization.CultureInfo()
/// RecogEngineName (e.g., Prajna) that identifer the image recognition provider 
/// InstituteName (e.g., MSR CCS) that identifer the institution that provides the iamge recognizer
/// InstituteURL that provides more information of the institution that provides the iamge recognizer service. 
/// Contact email
/// </summary>
[<AllowNullLiteral>]
type VHubProviderEntry() = 
    inherit RecogEngine()
    member val ConnectionID = Guid.Empty with get, set
    member x.TryParse( line: string ) = 
        let tabs = line.Split("\t".ToCharArray(), StringSplitOptions.RemoveEmptyEntries ) |> Array.map ( fun st -> st.Trim() )
        if tabs.Length > 5 then 
            let id = ref Guid.Empty
            if Guid.TryParse( tabs.[0], id ) then 
                x.ConnectionID <- !id
            if Guid.TryParse( tabs.[1], id ) then 
                x.RecogEngineID <- (!id)
            x.RecogEngineName <- tabs.[2]
            x.InstituteName <- tabs.[3]
            x.InstituteURL <- tabs.[4]
            x.ContactEmail <- tabs.[5]
            true
        else
            false
    static member Parse( info: string ) = 
        let entry = VHubProviderEntry()
        if entry.TryParse( info ) then 
            entry
        else
            null

/// <summary>
/// Hold a list of Image Recognition Provider
/// </summary>
type VHubProviderList() = 
    static member val Current = new VHubProviderList() with get
    static member init(rootDir: string, providerFileName: string) =
        let fname = Path.Combine( rootDir, providerFileName )
        VHubProviderList.Current.ParseFile( fname )
    member val Members = ConcurrentDictionary<_,_>() with get, set
    /// <summary>
    /// Parsing a file to get image recognition providers. 
    /// </summary>
    member x.ParseFile( fname: string ) = 
        try
            use readFile = new FileStream( fname, FileMode.Open, FileAccess.Read, FileShare.ReadWrite )
            use readStream = new StreamReader( readFile ) 
            try
                while not (readStream.EndOfStream) do 
                    let line = readStream.ReadLine()
                    let entry =  VHubProviderEntry.Parse( line ) 
                    if not (Utils.IsNull entry) then 
                        x.Members.Item( entry.ConnectionID ) <- entry
            finally 
                readStream.Close()
                readFile.Close()
        with 
        | e -> 
            Logger.Log( LogLevel.Error, ( sprintf "Fail to parse the provider roster file %s" fname ))
    /// Retrieve current provider list
    member x.ToSeq() = 
        x.Members.Values |> seq<_>    
    /// Retrieve current provider list
    static member toSeq() = 
        VHubProviderList.Current.ToSeq()  
    /// Get Provider
    member x.GetProvider( connectionID ) =
        let refProvider = ref Unchecked.defaultof<_>
        if x.Members.TryGetValue( connectionID, refProvider ) then 
            (!refProvider)
        else
            null
    static member getProvider( connectionID ) = 
        VHubProviderList.Current.GetProvider( connectionID )   
    static member getProviderName( connectionID ) = 
        let provider = VHubProviderList.getProvider( connectionID ) 
        if Utils.IsNull provider then 
            null
        else
            provider.RecogEngineName






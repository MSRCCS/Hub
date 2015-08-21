﻿(*---------------------------------------------------------------------------
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
		WebCache.fs
  
	Description: 
		Precache web files for in memory serving. 

	Author:																	
 		Jin Li, Principal Researcher
 		Microsoft Research, One Microsoft Way
 		Email: jinl at microsoft dot com
    Date:
        Nov. 2014
	
 ---------------------------------------------------------------------------*)
namespace VMHub.Gateway

open System
open System.Diagnostics

type ProcessorPercentage() = 
    /// <remarks>
    /// This is a very heavy object and will take around 0.5 sec to initialize, so we share the initialization here. 
    /// </remarks>
    static member val Processor = 
            let processor = new Management.ManagementObject("Win32_PerfFormattedData_PerfOS_Processor.Name='_Total'")
            processor with get
    static member GetPercentProcessorTime() =
            let processor = ProcessorPercentage.Processor
            lock ( processor ) ( fun _ -> 
                processor.Get()
                let wmi = processor.GetPropertyValue( "PercentProcessorTime" ) :?> UInt64 
                wmi
                )
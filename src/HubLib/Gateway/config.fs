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
		config.fs
  
	Description: 
		Gateway configuration parameter. 

	Author:																	
 		Jin Li, Partner Research Manager
 		Microsoft Research, One Microsoft Way
 		Email: jinl at microsoft dot com
    Date:
        Feb. 2016
	
 ---------------------------------------------------------------------------*)
namespace Prajna.Service.Hub.Visual.Configuration

type HubSetting() = 
    static member val FileNotExist = "filenotfound.html" with get, set
    static member val Default = "default.html" with get, set
    static member val DefaultUser = "defaultuser.html" with get, set
                        


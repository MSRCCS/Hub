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
		IMhub
  
	Description: 
		Usage Guide: the application is intended to run in an virtual machine (e.g., an Azure virtual machine behind a round robin load balancing point). 

	Author:																	
 		Jin Li, Principal Researcher
 		Microsoft Research, One Microsoft Way
 		Email: jinl at microsoft dot com
    Date:
        Nov. 2014
	
 ---------------------------------------------------------------------------*)
 Server 2012 R2, need to use powershell command:
 
 
  IMHub uses the following port:

 web port, for Hub information, e.g., 

 http://servername.cloudapp.net/ImGatewayService/Log.html	: Return current log of the current gateway
 http://servername.cloudapp.net/ImGatewayService/Log##.html	: Return log of past gateway execution instance

 Creating imhub-uswest
 username: localhost\imhub
 password: Imhub#1118
 cloudservice: imhub-uswest.cloudapp.net 
 port 80: ImGateway
 port 81: ImHubReg

The following additional command needs to be executed to setup the webserver and service. 

1. "New-NetFirewallRule -display "port 80" -Direction Inbound -LocalPort 80-81 -Protocol TCP -Action Allow"

 to enable firewall. Setting firewall manually in Windows Firewall with advanced security doesn't seem to work. The application just don't get the packet. 

 2. "netsh http add urlacl url=http://+:80/ user=DOMAIN\user"
 
	to Enable URLACL

	
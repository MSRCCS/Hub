/*---------------------------------------------------------------------------
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
        EvaluationServer.fs

    Description: 
        An instance of Prajna cloud service for performing evaluation for (but not limited to) visual recognition.

    Author:																	
        Lei Zhang, Senior Researcher
        Microsoft Research, One Microsoft Way
        Email: leizhang at microsoft dot com
    Date:
        February, 2016
    
 ---------------------------------------------------------------------------*/
using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using VMHub.ServiceEndpoint;
using VMHub.Data;
using Prajna.Tools;
using Prajna.Service.CSharp;
using VMHubClientLibrary;
using System.Threading.Tasks;

namespace EvaluationServer
{
    public class EvaluationServerInstance : VHubBackEndInstance<VHubBackendStartParam>
    {
        static string gateWay;
        static EvaluationSetting evalSetting;
        
        // Please set appropriate values for the following variables for a new Recognition Server.
        static string providerName = "Sample-CSharp";
        static string providerGuidStr = "843EF294-C635-42DA-9AD8-E79E82F9A357";
        static string domainName = "";

        static ConcurrentDictionary<string, Evaluator> dictEvaluator = new ConcurrentDictionary<string, Evaluator>();        

        public static EvaluationServerInstance Current { get; set; }
        public EvaluationServerInstance(string gateWay, string configFile, EvaluationSetting evalSetting) :
            /// Fill in your recognizing engine name here
            base(providerName)
        {
            /// CSharp does support closure, so we have to pass the recognizing instance somewhere, 
            /// We use a static variable for this example. But be aware that this method of variable passing will hold a reference to SampleRecogInstanceCSharp

            EvaluationServerInstance.Current = this;
            Func<VHubBackendStartParam, bool> del = InitializeRecognizer;
            this.OnStartBackEnd.Add(del);

            EvaluationServerInstance.gateWay = gateWay;
            EvaluationServerInstance.evalSetting = evalSetting;

            EvaluationServerInstance.domainName = evalSetting.config["service_name"];
        }

        public static bool InitializeRecognizer(VHubBackendStartParam pa)
        {
            var bInitialized = true;
            var x = EvaluationServerInstance.Current;
            if (!Object.ReferenceEquals(x, null))
            {
                /// <remarks>
                /// To implement your own image recognizer, please obtain a connection Guid by contacting jinl@microsoft.com
                /// </remarks>
                Guid providerGUID = new Guid(providerGuidStr);
                x.RegisterAppInfo( providerGUID , "0.0.0.1");
                Trace.WriteLine("****************** Register TeamName: " + providerName + " provider GUID: " + providerGUID.ToString() + " domainName: " + domainName);

                Func<Guid, int, RecogRequest, RecogReply> del = PredictionFunc;
                /// <remarks>
                /// Register your prediction function here. 
                /// </remarks> 

                string logoFile = Path.Combine(evalSetting.rootDir, "logo.jpg");
                if (!File.Exists(logoFile))
                {
                    string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                    logoFile = Path.Combine(exeDir, "logo.jpg");
                }
                x.RegisterClassifierCS(domainName, logoFile, 100, del);

                // initialization here
            }
            else
            {
                bInitialized = false;
            }
            return bInitialized;
        }

        public static RecogReply PredictionFunc(Guid id, int timeBudgetInMS, RecogRequest req)
        {
            Dictionary<string, string> cmdDict;
            try
            {
                string request = Encoding.Default.GetString(req.Data);
                cmdDict = request.Split(';')
                    .Select(x => x.Split(':'))
                    .ToDictionary(f => f[0].Trim(), f => f[1].Trim(), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                string msg = "Invalid request.";
                return VHubRecogResultHelper.FixedClassificationResult(msg, msg);
            }

            string cmd = cmdDict["cmd"];
            string serviceGuid = cmdDict["serviceGuid"];

            string result = string.Empty;

            if (string.Compare(cmd, "Start", true) == 0 || string.Compare(cmd, "Resume", true) == 0)
            {
                int instanceNum = Convert.ToInt32(cmdDict["instanceNum"]);
                Evaluator evaluator = dictEvaluator.GetOrAdd(serviceGuid, guid =>
                {
                    var eval = new Evaluator(gateWay, guid, evalSetting);
                    Task.Run(async () =>
                    {
                        bool isResume = string.Compare(cmd, "Resume", true) == 0;
                        await eval.Eval(instanceNum, eval.cancellationTokenSource.Token, isResume);
                    })
                    .ContinueWith(task =>
                    {
                        Evaluator value;
                        dictEvaluator.TryRemove(guid, out value);
                        // dispose cancellationTokenSource
                        value.cancellationTokenSource.Dispose();
                    });
                    return eval;
                });

                result = string.Format("Evaluation started for service: {0}.", serviceGuid);
            }
            else if (string.Compare(cmd, "Cancel", true) == 0)
            {
                Evaluator evaluator;
                if (dictEvaluator.TryGetValue(serviceGuid, out evaluator))
                {
                    evaluator.cancellationTokenSource.Cancel();
                    result = string.Format("Evaluation is being cancelled.");
                }
                else
                {
                    result = string.Format("Evaluation is not running.");
                }

            }
            else if (string.Compare(cmd, "Check", true) == 0)
            {
                Evaluator evaluator;
                if (dictEvaluator.TryGetValue(serviceGuid, out evaluator))
                {
                    TimeSpan span = DateTime.UtcNow - evaluator.timeStart;
                    result = string.Format("{0}: processed: {1}, succeeded: {2} / {3}, throughput: {4:F2} images/sec", serviceGuid, evaluator.TriedNumOfImages, evaluator.SucceededNumOfImages, evaluator.TotalNumOfImages,
                        (float)evaluator.TriedNumOfImages / span.TotalSeconds);
                }
                else
                {
                    result = string.Format("Evaluation for service {0} is not found", serviceGuid);
                }

            }
            else if (string.Compare(cmd, "List", true) == 0)
            {
                var lines = dictEvaluator.Select(kv =>
                {
                    TimeSpan span = DateTime.UtcNow - kv.Value.timeStart;

                    var evaluator = kv.Value;
                    return string.Format("{0}: processed: {1}, succeeded: {2} / {3}, throughput: {4:F2} images/sec", kv.Key, evaluator.TriedNumOfImages, evaluator.SucceededNumOfImages, evaluator.TotalNumOfImages,
                        (float)kv.Value.TriedNumOfImages / span.TotalSeconds);
                });
                result = string.Join("\n", lines);                    
            }
            else
            {
                result = string.Format("Request is not supported: {0}", cmd);
            }

            Console.WriteLine(result);
            return VHubRecogResultHelper.FixedClassificationResult(result, result);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var usage = @"
    Usage: Launch a local instance of Caffe Host service.\n\
    Command line arguments: \n\
        -gateway     SERVERURI       ServerUri\n\
        -evalconfig  ConfigFile      path to config file specifying measurment data and settings\n\
        -log         LogFile         Path to log file\n\
";
            List<string> args_list = args.ToList();
            args_list.Add("-con");
            args = args_list.ToArray();
            var parse = new ArgumentParser(args);
            var usePort = VHubSetting.RegisterServicePort;
            var gatewayServers = parse.ParseStrings("-gateway", new string[] {"vm-hub.trafficmanager.net"});
            var serviceName = "EvaluationServerService";

            var configFile = parse.ParseString("-evalconfig", "");
            var evalSetting = new EvaluationSetting(configFile);

            var bAllParsed = parse.AllParsed(usage);

            // prepare parameters for registering this recognition instance to vHub gateway
            var startParam = new VHubBackendStartParam();
            /// Add traffic manager gateway, see http://azure.microsoft.com/en-us/services/traffic-manager/, 
            /// Gateway that is added as traffic manager will be repeatedly resovled via DNS resolve
            foreach (var gatewayServer in gatewayServers)
            {
                if (!(StringTools.IsNullOrEmpty(gatewayServer)))
                    startParam.AddOneTrafficManager(gatewayServer, usePort);
            };

            // start a local instance. 
            Console.WriteLine("Local instance started and registered to {0}", gatewayServers[0]);
            Console.WriteLine("Current working directory: {0}", Directory.GetCurrentDirectory());
            Console.WriteLine("Press ENTER to exit");
            RemoteInstance.StartLocal(serviceName, startParam, 
                            () => new EvaluationServerInstance(gatewayServers[0], configFile, evalSetting));
            while (RemoteInstance.IsRunningLocal(serviceName))
            {
                if (Console.KeyAvailable)
                {
                    var cki = Console.ReadKey(true);
                    if (cki.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine("ENTER pressed, exiting...");
                        RemoteInstance.StopLocal(serviceName);
                    }
                    else
                        System.Threading.Thread.Sleep(10);
                }
            }
        }
    }
}

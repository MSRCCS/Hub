using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using VMHub.ServiceEndpoint;
using VMHub.Data;
using Prajna.Tools;
using Prajna.Service.CSharp;
using Prajna.Vision.Caffe;

namespace CaffeHost
{
    public class CmdArgs
    {
        public string recoginzerName;
        public string protoFile;
        public string modelFile;
        public string labelMapFile;
        public int topK;
        public float confThreshold;
    }

    public class CaffeHostInstance : VHubBackEndInstance<VHubBackendStartParam>
    {
        static CmdArgs cmd = new CmdArgs();
        static string rootDir;
        static int numDataProcessed = 0;
        
        // Please set appropriate values for the following variables for a new Recognition Server.
        static string providerName = "Sample-CSharp";
        static string providerGuidStr = "843EF294-C635-42DA-9AD8-E79E82F9A357";
        //static string domainName = "CaffeHost";

        static CaffePredictor caffePredictor = new CaffePredictor();

        public static CaffeHostInstance Current { get; set; }
        public CaffeHostInstance(string rootDir, CmdArgs cmdArgs) :
            /// Fill in your recognizing engine name here
            base(providerName)
        {
            /// CSharp does support closure, so we have to pass the recognizing instance somewhere, 
            /// We use a static variable for this example. But be aware that this method of variable passing will hold a reference to SampleRecogInstanceCSharp

            CaffeHostInstance.Current = this;
            Func<VHubBackendStartParam, bool> del = InitializeRecognizer;
            this.OnStartBackEnd.Add(del);

            CaffeHostInstance.rootDir = rootDir;
            cmd = cmdArgs;
        }

        public static bool InitializeRecognizer(VHubBackendStartParam pa)
        {
            var bInitialized = true;
            var x = CaffeHostInstance.Current;
            if (!Object.ReferenceEquals(x, null))
            {
                /// <remarks>
                /// To implement your own image recognizer, please obtain a connection Guid by contacting jinl@microsoft.com
                /// </remarks>
                //x.RegisterAppInfo(new Guid("B1380F80-DD03-420C-9D0E-2CAA04B6E24D"), "0.0.0.1");
                Guid providerGUID = new Guid(providerGuidStr);
                //Guid providerGUID = new Guid("843EF294-C635-42DA-9AD8-E79E82F9A357");
                x.RegisterAppInfo( providerGUID , "0.0.0.1");
                Trace.WriteLine("****************** Register TeamName: " + providerName + " provider GUID: " + providerGUID.ToString() + " domainName: " + cmd.recoginzerName);

                Func<Guid, int, RecogRequest, RecogReply> del = PredictionFunc;
                /// <remarks>
                /// Register your prediction function here. 
                /// </remarks> 

                string logoFile = Path.Combine(rootDir, "logo.jpg");
                if (!File.Exists(logoFile))
                {
                    string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                    logoFile = Path.Combine(exeDir, "logo.jpg");
                }
                x.RegisterClassifierCS(cmd.recoginzerName, logoFile, 100, del);

                caffePredictor.Init(Path.Combine(rootDir, cmd.protoFile), Path.Combine(rootDir, cmd.modelFile), Path.Combine(rootDir, cmd.labelMapFile));
            }
            else
            {
                bInitialized = false;
            }
            return bInitialized;
        }

        public static RecogReply PredictionFunc(Guid id, int timeBudgetInMS, RecogRequest req)
        {
            byte[] imgBuf = req.Data;

            using (var ms = new MemoryStream(imgBuf))
            using (var img = (Bitmap)Bitmap.FromStream(ms))
            {
                string result = caffePredictor.Predict(img, cmd.topK, cmd.confThreshold);

                numDataProcessed++;
                Console.WriteLine("Image {0}: {1}", numDataProcessed, result);
                return VHubRecogResultHelper.FixedClassificationResult(result, result);
            }
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
        -recogname   RecogName       Recognizer Name\n\
        -rootdir     Root_Directory  this directory holds logo image and model files\n\
        -proto       ProtoFile       Path to proto file\n\
        -model       ModelFile       Path to model file\n\
        -labelmap    LabelMapFile    Path to label map file\n\
        -topk        TopK            Top K result to return (default: 5)\n\
        -thresh      Confidence      Confidence threshold (default: 0.9)
        -log         LogFile         Path to log file\n\
";
            List<string> args_list = args.ToList();
            args_list.Add("-con");
            args = args_list.ToArray();
            var parse = new ArgumentParser(args);
            var usePort = VHubSetting.RegisterServicePort;
            var gatewayServers = parse.ParseStrings("-gateway", new string[] {"vm-hubr.trafficmanager.net"});
            var serviceName = "CaffeHostService";

            var rootdir = parse.ParseString("-rootdir", Directory.GetCurrentDirectory());
            var cmd = new CmdArgs();
            cmd.recoginzerName = parse.ParseString("-recogName", "CaffeHost");
            cmd.protoFile = parse.ParseString("-proto", "");
            cmd.modelFile = parse.ParseString("-model", "");
            cmd.labelMapFile = parse.ParseString("-labelmap", "");
            cmd.topK = parse.ParseInt("-topk", 5);
            cmd.confThreshold = (float)parse.ParseFloat("-thresh", 0.9);

            var bAllParsed = parse.AllParsed(usage);

            CaffePredictor predictor = new CaffePredictor();

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
                            () => new CaffeHostInstance(rootdir, cmd));
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

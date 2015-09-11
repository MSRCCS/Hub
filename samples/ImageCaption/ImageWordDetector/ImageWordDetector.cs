using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using Prajna.Tools;
using Prajna.Service.ServiceEndpoint;

using Prajna.Service.CSharp;

using WordDetector;
using VMHub.ServiceEndpoint;
using VMHub.Data;

namespace ImageWordDetectorServer
{

    public class ImageWordDetectorInstance: VHubBackEndInstance<VHubBackendStartParam>
    {
        static string saveImageDir;
        static string modelDir;
        static int numImageRecognized = 0;

        static ImageWordDetector wordDetector = new ImageWordDetector();

        public static ImageWordDetectorInstance Current { get; set; }
        public ImageWordDetectorInstance(string saveImageDir, string modelDir) :
            /// Fill in your recognizing engine name here
            base("Sample-CSharp")
        {
            /// CSharp does support closure, so we have to pass the recognizing instance somewhere, 
            /// We use a static variable for this example. But be aware that this method of variable passing will hold a reference to SampleRecogInstanceCSharp

            ImageWordDetectorInstance.Current = this;
            Func<VHubBackendStartParam, bool> del = InitializeRecognizer;
            this.OnStartBackEnd.Add(del);

            ImageWordDetectorInstance.saveImageDir = saveImageDir;
            ImageWordDetectorInstance.modelDir = modelDir;
        }

        public static bool InitializeRecognizer(VHubBackendStartParam pa)
        {
            var bInitialized = true;
            var x = ImageWordDetectorInstance.Current;
            if (!Object.ReferenceEquals(x, null))
            {
                /// <remarks>
                /// To implement your own image recognizer, please obtain a connection Guid by contacting jinl@microsoft.com
                /// </remarks> 
                x.RegisterAppInfo(new Guid("843EF294-C635-42DA-9AD8-E79E82F9A357"), "0.0.0.1");
                Func<Guid, int, RecogRequest, RecogReply> del = PredictionFunc;
                /// <remarks>
                /// Register your prediction function here. 
                /// </remarks> 
                x.RegisterClassifierCS("#WordDetector", Path.Combine(modelDir, "logo.jpg"), 100, del);

                string v1Proto = Path.Combine(modelDir, @"v1\mil_finetune.prototxt.deploy");
                string v1Model = Path.Combine(modelDir, @"v1\snapshot_iter_240000.caffemodel");
                string v1LabelMap = Path.Combine(modelDir, @"v1\labelmap.txt");
                string v1Threshold = Path.Combine(modelDir, @"v1\threshold.txt");
                string v2Proto = Path.Combine(modelDir, @"v2\mil_finetune.prototxt.deploy_feat");
                string v2Model = Path.Combine(modelDir, @"v2\snapshot_iter_480000.caffemodel");
                string v2LabelMap = Path.Combine(modelDir, @"v2\labelmap.txt");

                wordDetector.Init(v1Proto, v1Model, v1LabelMap, v1Threshold, v2Proto, v2Model, v2LabelMap, true);
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
            byte[] imgType = System.Text.Encoding.UTF8.GetBytes( "jpg" );
            Guid imgID = BufferCache.HashBufferAndType( imgBuf, imgType );
            string imgFileName = imgID.ToString() + ".jpg";

            string filename = Path.Combine( saveImageDir, imgFileName );
            if (!File.Exists(filename))
                FileTools.WriteBytesToFileConcurrent(filename, imgBuf);

            string result_words = wordDetector.DetectWords(filename);
            string result_feature = wordDetector.ExtractFeature(filename);

            string resultString = result_words+ "\n" + result_feature;

            File.Delete(filename);
            numImageRecognized++;
            Console.WriteLine("Image {0}: {1}", numImageRecognized, resultString);
            return VHubRecogResultHelper.FixedClassificationResult(resultString, resultString);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var usage = @"
    Usage: Launch a local instance of IRC.SampleRecogServerCSharp.\n\
    Command line arguments: \n\
        -gateway     SERVERURI       ServerUri\n\
        -rootdir     Root_Directory  this directory holds logo image and data model files\n\
        -saveimage   DIRECTORY       Directory where recognized image is saved. Default: current dir \n\
        -log         LogFile         Path to log file \n\
    ";
            List<string> args_list = args.ToList();
            args_list.Add("-con");
            args = args_list.ToArray();
            var parse = new ArgumentParser(args);
            var usePort = VHubSetting.RegisterServicePort;
            var saveimagedir = parse.ParseString("-saveimage", Directory.GetCurrentDirectory());
            var gatewayServers = parse.ParseStrings("-gateway", new string[] { "vhub.trafficmanager.net" });
            var rootdir = parse.ParseString("-rootdir", Directory.GetCurrentDirectory());
            var serviceName = "ImageWordDetector";

            var logoImageDir = rootdir;

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
                            () => new ImageWordDetectorInstance(saveimagedir, logoImageDir));
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

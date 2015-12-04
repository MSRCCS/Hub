using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

using Prajna.Tools;
using Prajna.Service.ServiceEndpoint;

using Prajna.Service.CSharp;

using VMHub.ServiceEndpoint;
using VMHub.Data;

using ImgCap;
using CaffeLibMC;

namespace ImageCaptionServer
{

    public class ImageCaptionInstance: VHubBackEndInstance<VHubBackendStartParam>
    {
        static string saveImageDir;
        static string modelDir;
        static int numImageRecognized = 0;

        static Predictor predictor;

        public static ImageCaptionInstance Current { get; set; }
        public ImageCaptionInstance(string saveImageDir, string modelDir) :
            /// Fill in your recognizing engine name here
            base("Sample-CSharp")
        {
            /// CSharp does support closure, so we have to pass the recognizing instance somewhere, 
            /// We use a static variable for this example. But be aware that this method of variable passing will hold a reference to SampleRecogInstanceCSharp

            ImageCaptionInstance.Current = this;
            Func<VHubBackendStartParam, bool> del = InitializeRecognizer;
            this.OnStartBackEnd.Add(del);

            ImageCaptionInstance.saveImageDir = saveImageDir;
            ImageCaptionInstance.modelDir = modelDir;
        }

        public static bool InitializeRecognizer(VHubBackendStartParam pa)
        {
            var bInitialized = true;
            var x = ImageCaptionInstance.Current;
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
                string exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                x.RegisterClassifierCS("#ImageCaption", Path.Combine(exeDir, "logo.jpg"), 100, del);

                string wordDetectorProto = Path.Combine(modelDir, @"Model\wordDetector.proto");
                string wordDetectorModel = Path.Combine(modelDir, @"Model\wordDetector.caffemodel");
                string featureDetectorProto = Path.Combine(modelDir, @"Model\FeaturesDetector.proto");
                string featureDetectorModel = Path.Combine(modelDir, @"Model\FeaturesDetector.caffemodel");
                string thresholdFile = Path.Combine(modelDir, @"Model\thresholds.txt");
                string wordLabelMapFile = Path.Combine(modelDir, @"Model\labelmap.txt");
                string languageModel = Path.Combine(modelDir, @"Model\sentencesGeneration.lblmmodel");
                string referenceDssmModelFile = Path.Combine(modelDir, @"Model\reference.dssmmodel");
                string candidateDssmModelFile = Path.Combine(modelDir, @"Model\candidate.dssmmodel");
                string trigramFile = Path.Combine(modelDir, @"Model\coco.cap.l3g");
                CaffeModel.SetDevice(0); //This needs to be set before instantiating the net
                var lmTestArguments = new LangModel.Arguments
                {
                    NumWorkers = 8,
                    MaxSentenceLength = 19,
                    NumSentences = 500,
                    BeamWidth = 200,
                    AttributesCoverageBar = 5,
                    KTopWords = 100
                };

                predictor = new Predictor(wordDetectorProto, wordDetectorModel, featureDetectorProto, featureDetectorModel,
                                    thresholdFile, wordLabelMapFile,
                                    languageModel, lmTestArguments, referenceDssmModelFile, candidateDssmModelFile, trigramFile);
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

            Stopwatch timer = Stopwatch.StartNew();
            CaffeModel.SetDevice(0);
            string resultString = predictor.Predict(filename);
            timer.Stop();

            //File.Delete(filename);
            numImageRecognized++;
            Console.WriteLine("Image {0}:{1}:{2}: {3}", numImageRecognized, imgFileName, timer.Elapsed, resultString);
            return VHubRecogResultHelper.FixedClassificationResult(resultString, resultString);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var usage = @"
    Usage: Launch a local instance of Image Caption Service.\n\
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
            var gatewayServers = parse.ParseStrings("-gateway", new string[] { "vm-hubr.trafficmanager.net" });
            var rootdir = parse.ParseString("-rootdir", Directory.GetCurrentDirectory());
            var serviceName = "ImageCaption";

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
                            () => new ImageCaptionInstance(saveimagedir, rootdir));
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

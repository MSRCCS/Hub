using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Prajna.Service.ServiceEndpoint;
using VMHub.ServiceEndpoint;
using VMHub.Data;
using Prajna.Tools;
using Prajna.Service.CSharp;

namespace CaptionGenerationServer
{

    public class CaptionGenerationInstance : VHubBackEndInstance<VHubBackendStartParam>
    {
        static string saveImageDir;
        static string rootDir;
        static int numImageRecognized = 0;
        
        // Please set appropriate values for the following variables for a new Recognition Server.
        static string providerName = "Sample-CSharp";
        static string providerGuidStr = "843EF294-C635-42DA-9AD8-E79E82F9A357";
        static string domainName = "#Caption";

        public static CaptionGenerationInstance Current { get; set; }
        public CaptionGenerationInstance(string saveImageDir, string rootDir) :
            /// Fill in your recognizing engine name here
            base(providerName)
        {
            /// CSharp does support closure, so we have to pass the recognizing instance somewhere, 
            /// We use a static variable for this example. But be aware that this method of variable passing will hold a reference to SampleRecogInstanceCSharp

            CaptionGenerationInstance.Current = this;
            Func<VHubBackendStartParam, bool> del = InitializeRecognizer;
            this.OnStartBackEnd.Add(del);

            CaptionGenerationInstance.saveImageDir = saveImageDir;
            CaptionGenerationInstance.rootDir = Path.GetFullPath(rootDir);
        }

        public static bool InitializeRecognizer(VHubBackendStartParam pa)
        {
            var bInitialized = true;
            var x = CaptionGenerationInstance.Current;
            if (!Object.ReferenceEquals(x, null))
            {
                /// <remarks>
                /// To implement your own image recognizer, please obtain a connection Guid by contacting jinl@microsoft.com
                /// </remarks>
                //x.RegisterAppInfo(new Guid("B1380F80-DD03-420C-9D0E-2CAA04B6E24D"), "0.0.0.1");
                Guid providerGUID = new Guid(providerGuidStr);
                //Guid providerGUID = new Guid("843EF294-C635-42DA-9AD8-E79E82F9A357");
                x.RegisterAppInfo( providerGUID , "0.0.0.1");
                Trace.WriteLine("****************** Register TeamName: " + providerName + " provider GUID: " + providerGUID.ToString() + " domainName: " + domainName);

                
                Func<Guid, int, RecogRequest, RecogReply> del = PredictionFunc;
                /// <remarks>
                /// Register your prediction function here. 
                /// </remarks> 
                x.RegisterClassifierCS(domainName, Path.Combine(rootDir, "logo.jpg"), 100, del);
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

            string resultString = "";
            try
            {
                string filename = Path.Combine(saveImageDir, imgFileName);
                if (!File.Exists(filename))
                    FileTools.WriteBytesToFileConcurrent(filename, imgBuf);

                Directory.SetCurrentDirectory(rootDir);
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = @"main.bat",
                    Arguments = filename,
                    //RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                var process = Process.Start(processStartInfo);
                //var resultString = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (File.Exists(filename + ".caption"))
                    resultString = File.ReadAllLines(filename + ".caption")[0];

                File.Delete(filename);
                File.Delete(filename + "_1.txt");
                File.Delete(filename + "_1.txt.detections.prec.txt");
                File.Delete(filename + "_1.txt.detections.sc.txt");
                File.Delete(filename + "_1.txt.img.dmsm.fea");
                File.Delete(filename + ".caption");
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.Error, e.Message);
                resultString = "Service exception. Please try again.";
            }
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
        -rootdir     Root_Directory  this directory holds logo image and caption script files\n\
        -saveimage   DIRECTORY       Directory where recognized image is saved. Default: current dir \n\
        -log         LogFile         Path to log file \n\
";
            List<string> args_list = args.ToList();
            args_list.Add("-con");
            args = args_list.ToArray();
            var parse = new ArgumentParser(args);
            var usePort = VHubSetting.RegisterServicePort;
            var saveimagedir = parse.ParseString("-saveimage", Directory.GetCurrentDirectory());
            var gatewayServers = parse.ParseStrings("-gateway", new string[] {"vm-hubr.trafficmanager.net"});
            var rootdir = parse.ParseString("-rootdir", Directory.GetCurrentDirectory());
            var serviceName = "ImageCaptionGenerator";

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
                            () => new CaptionGenerationInstance(saveimagedir, rootdir));
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

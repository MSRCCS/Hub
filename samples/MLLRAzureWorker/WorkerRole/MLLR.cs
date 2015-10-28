namespace MLLR
{
    using System;
    using System.IO;
    using System.Diagnostics;
    using System.Reflection;

    using Prajna.Service.CSharp;
    using Prajna.Service.ServiceEndpoint;
    using Prajna.Tools;
    
    using VMHub.ServiceEndpoint;
    using VMHub.Data;

    using Logger = Prajna.Tools.CSharp.Logger;

    public class MLLRInstance : VHubBackEndInstance<VHubBackendStartParam>
    {
        static string saveDataDir;
        static string rootDir;
        static int numDataProcessed = 0;

        // Please set appropriate values for the following variables for a new Recognition Server.
        static string providerName = "Sample-CSharp";
        static string providerGuidStr = "843EF294-C635-42DA-9AD8-E79E82F9A357";
        static string domainName = "#MLLR-Test";

        public static MLLRInstance Current { get; set; }
        public MLLRInstance(string saveDataDir, string rootDir) :
            /// Fill in your recognizing engine name here
            base(providerName)
        {
            /// CSharp does support closure, so we have to pass the recognizing instance somewhere, 
            /// We use a static variable for this example. But be aware that this method of variable passing will hold a reference to SampleRecogInstanceCSharp

            MLLRInstance.Current = this;
            Func<VHubBackendStartParam, bool> del = InitializeRecognizer;
            this.OnStartBackEnd.Add(del);

            MLLRInstance.saveDataDir = saveDataDir;
            MLLRInstance.rootDir = rootDir;
        }

        public static bool InitializeRecognizer(VHubBackendStartParam pa)
        {
            var bInitialized = true;
            var x = MLLRInstance.Current;
            if (!Object.ReferenceEquals(x, null))
            {
                /// <remarks>
                /// To implement your own image recognizer, please obtain a connection Guid by contacting jinl@microsoft.com
                /// </remarks>
                //x.RegisterAppInfo(new Guid("B1380F80-DD03-420C-9D0E-2CAA04B6E24D"), "0.0.0.1");
                Guid providerGUID = new Guid(providerGuidStr);
                //Guid providerGUID = new Guid("843EF294-C635-42DA-9AD8-E79E82F9A357");
                x.RegisterAppInfo(providerGUID, "0.0.0.1");

                Logger.Log(LogLevel.Info, "****************** Register TeamName: " + providerName + " provider GUID: " + providerGUID.ToString() + " domainName: " + domainName);

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
            byte[] data = req.Data;
            byte[] dataType = System.Text.Encoding.UTF8.GetBytes("mllr");
            Guid dataID = BufferCache.HashBufferAndType(data, dataType);
            string dataFileName = dataID.ToString() + ".mllr";

            string filename = Path.Combine(saveDataDir, dataFileName);
            if (!File.Exists(filename))
                FileTools.WriteBytesToFileConcurrent(filename, data);

            Directory.SetCurrentDirectory(rootDir);
            var processStartInfo = new ProcessStartInfo
            {
                FileName = @"main.bat",
                Arguments = filename,
                UseShellExecute = false
            };
            var process = Process.Start(processStartInfo);
            
            process.WaitForExit();
            string resultString = "";
            if (File.Exists(filename + ".mllr"))
                resultString = File.ReadAllText(filename + ".mllr");

            File.Delete(filename);
            File.Delete(filename + ".mllr");

            numDataProcessed++;
            Logger.Log(LogLevel.Info, "Data " + numDataProcessed + ": " + resultString);
            return VHubRecogResultHelper.FixedClassificationResult(resultString, resultString);
        }
    }

    public class MLLRServer
    {
        private readonly string serviceName = "MLLRService";

        public void Start()
        {
            var curDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var usePort = VHubSetting.RegisterServicePort;
            var savedatadir = Path.Combine(curDir, "savedata");
            if (!Directory.Exists(savedatadir))
            {
                Directory.CreateDirectory(savedatadir);
            }
            var gatewayServers = new string[] { "vm-hubr.trafficmanager.net" };
            var rootdir = Path.Combine(curDir, "Data");

            // prepare parameters for registering this recognition instance to vHub gateway
            var startParam = new VHubBackendStartParam();
            /// Add traffic manager gateway, see http://azure.microsoft.com/en-us/services/traffic-manager/, 
            /// Gateway that is added as traffic manager will be repeatedly resovled via DNS resolve
            foreach (var gatewayServer in gatewayServers)
            {
                if (!(StringTools.IsNullOrEmpty(gatewayServer)))
                    startParam.AddOneTrafficManager(gatewayServer, usePort);
            };

            Logger.Log(LogLevel.Info, "Local instance started and registered to " + gatewayServers[0]);
            Logger.Log(LogLevel.Info, "Current working directory:  " + Directory.GetCurrentDirectory());

            RemoteInstance.StartLocal(serviceName, startParam,
                            () => new MLLRInstance(savedatadir, rootdir));
        }

        public void Stop()
        {
            RemoteInstance.StopLocal(serviceName);
        }
    }
}

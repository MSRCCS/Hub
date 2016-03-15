using System;
using System.Text;
using System.Diagnostics;

using CmdParser;
using VMHubClientLibrary;

namespace CommandLineTool
{
    class Program
    {
        public class PrajnaHub
        {
            public GatewayHttpInterface Hub;
            public bool bInitialized;

            public PrajnaHub(string Gateway, Guid CustomerID, string CustomerKey)
            {
                Hub = new GatewayHttpInterface(Gateway, CustomerID, CustomerKey);
                bInitialized = true;
            }
        }

        class ArgsList
        {
            [Argument(ArgumentType.AtMostOnce, HelpText = "Prajna Hub Gateway URL, default: vm-hub.trafficmanager.net")]
            public string pHub = "vm-hub.trafficmanager.net";
            [Argument(ArgumentType.AtMostOnce, HelpText = "the GUID to specify the evaluation service")]
            public string evalServiceGuid = "22e6b28e-2e41-5d74-5a13-e1a5b9195648";  // for "msr-irc@icme2016"
        }
        class ArgsEval: ArgsList
        {
            //[Argument(ArgumentType.AtMostOnce, HelpText = "the GUID to specify the evaluation provider (optional now)")]
            //public string evalProviderGuid = Guid.Empty.ToString();
            [Argument(ArgumentType.Required, HelpText = "the GUID to specify the recognizer service for evaluation (your recognizer)")]
            public string recogServiceGuid = null;
        }
        class ArgsStart: ArgsEval
        {
            [Argument(ArgumentType.AtMostOnce, HelpText = "the number of instances running for this recognizer")]
            public int numInstances = 1;
        }

        const string _cmdFormat = "cmd:{0};serviceGuid:{1};instanceNum:{2}";
        static void EvalCommand(string pHub, string evalServiceGuid, string cmdString)
        {
            Guid schemaID, distID, aggrID, providerID, serviceID;
            schemaID = distID = aggrID = providerID = Guid.Empty;
            if (!Guid.TryParse(evalServiceGuid, out serviceID))
            {
                Console.WriteLine("Invalid evaluation service GUID, please double check.");
                return;
            }

            var gateway = new PrajnaHub(pHub, Guid.Empty, "SecretKeyShouldbeLongerThan10");
            string res = gateway.Hub.ProcessAsyncString(providerID, schemaID, serviceID, distID, aggrID, Encoding.ASCII.GetBytes(cmdString)).Result;

            Console.WriteLine("Response from the evaluation service:");
            Console.WriteLine(res);
        }

        static bool CheckRecogServiceGuid(string guidStr)
        {
            Guid guid;
            if (!Guid.TryParse(guidStr, out guid))
            {
                Console.WriteLine("Invalid recognizer service GUID, please double check.");
                return false;
            }
            return true;
        }

        static void Start(ArgsStart cmd)
        {
            if (cmd.numInstances > 20)
            {
                Console.WriteLine("For the evaluation purpose, we just use up to 20 instances.");
                cmd.numInstances = 20;
            }
            if (!CheckRecogServiceGuid(cmd.recogServiceGuid))
                return;
            
            string hubCmdString = string.Format(_cmdFormat, "Start", cmd.recogServiceGuid, cmd.numInstances);
            EvalCommand(cmd.pHub, cmd.evalServiceGuid, hubCmdString);
        }

        static void Cancel(ArgsEval cmd)
        {
            if (!CheckRecogServiceGuid(cmd.recogServiceGuid))
                return;
            
            string hubCmdString = string.Format(_cmdFormat, "Cancel", cmd.recogServiceGuid, 0);
            EvalCommand(cmd.pHub, cmd.evalServiceGuid, hubCmdString);
        }

        static void Check(ArgsEval cmd)
        {
            if (!CheckRecogServiceGuid(cmd.recogServiceGuid))
                return;

            string hubCmdString = string.Format(_cmdFormat, "Check", cmd.recogServiceGuid, 0);
            EvalCommand(cmd.pHub, cmd.evalServiceGuid, hubCmdString);
        }

        static void List(ArgsList cmd)
        {
            string hubCmdString = string.Format(_cmdFormat, "List", Guid.Empty, 0);
            EvalCommand(cmd.pHub, cmd.evalServiceGuid, hubCmdString);
        }

        static void Main(string[] args)
        {
            ParserX.AddTask<ArgsStart>(Start, "Start a new evaluation");
            ParserX.AddTask<ArgsEval>(Cancel, "Cancel the current evaluation");
            ParserX.AddTask<ArgsEval>(Check, "Check the progress of the current evaluation");
            ParserX.AddTask<ArgsList>(List, "List the progress of all the current evaluations");
            
            if (ParserX.ParseArgumentsWithUsage(args))
            {
                Stopwatch timer = Stopwatch.StartNew();
                ParserX.RunTask();
                timer.Stop();
                Console.WriteLine("Time used: {0}", timer.Elapsed);
            }
        }
    }
}

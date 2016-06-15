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
        Evaluation.fs

    Description: 
        Implementation of core functions in evaluation with throttling control.

    Author:																	
        Lei Zhang, Senior Researcher
        Microsoft Research, One Microsoft Way
        Email: leizhang at microsoft dot com
    Date:
        February, 2016
    
 ---------------------------------------------------------------------------*/
using System;
using System.IO;
using System.Linq;
using VMHubClientLibrary;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EvaluationServer
{
    public class EvaluationSetting
    {
        public Dictionary<string, string> config;
        // This is the ground truth file used for evaluation.
        // Each line corresponding one image, with TAB-separated columns defined as:
        public string measurementSetFile;
        public string measurementSetCountFile;        // just store the total number of images for progress report

        // Columns in each line are defined by "columns" in the config file. 
        // For example: "imagekey,label,flag,,,,,imagedata"
        // We expect "imagekey" and "imagedata" for sending traffic
        // "label" and "flag" for metric calculation
        public Dictionary<string, int> columns;

        // This file saves the evaluation result for each run.
        // Each line for one evaluation run, logged after the whole evaluation process is completed
        // The TAB-separated columns are defined as:
        // log_id, provider_name, eval_start_time, eval_end_time, count_for_accuracy_computation, succeeded_total_images, top1_accuracy, top5_accuracy
        public string measurementResultFile;

        public string rootDir;

        // This is the sub folder under rootDir, used for storing detailed evaluation results for 
        // every evaluation request, one file for one request.
        public string evaluationLogDir;

        public int maxGlobalRetry;
        public int maxWaitMins;

        public EvaluationSetting(string configFile)
        {
            rootDir = Path.GetDirectoryName(Path.GetFullPath(configFile));

            config = File.ReadLines(configFile)
                .Where(line => line.Trim().StartsWith("#") == false)
                .Select(line => line.Split(':'))
                .ToDictionary(cols => cols[0].Trim(), cols => cols[1].Trim(), StringComparer.OrdinalIgnoreCase);

            var getPath = new Func<string, string>(file => Path.Combine(rootDir, file));

            measurementSetFile = getPath(config["testfile"]);
            measurementSetCountFile = getPath(Path.ChangeExtension(measurementSetFile, ".count.tsv"));
            if (config.ContainsKey("eval_log_dir"))
                evaluationLogDir = getPath(config["eval_log_dir"]);
            else
                evaluationLogDir = getPath("eval_log");
            measurementResultFile = Path.Combine(evaluationLogDir, "_" + Path.ChangeExtension(config["testfile"], ".eval_run.tsv"));

            columns = config["columns"]
                .Split(new char[] { ' ', ';', ',' })
                .Select((col, idx) => Tuple.Create(col.Trim(), idx))
                .Where(tp => !string.IsNullOrEmpty(tp.Item1))
                .ToDictionary(tp => tp.Item1, tp => tp.Item2);

            if (config.ContainsKey("max_retry"))
                maxGlobalRetry = Convert.ToInt32(config["max_retry"]);
            else
                maxGlobalRetry = 10;

            if (config.ContainsKey("max_wait_mins"))
                maxWaitMins = Convert.ToInt32(config["max_wait_mins"]);
            else
                maxWaitMins = 60;

            Console.WriteLine("Test file: {0}", measurementSetFile);
            Console.WriteLine("Evaluation log dir: {0}", evaluationLogDir);
            Console.WriteLine("Max retry: {0}, Max wait mins: {1}", maxGlobalRetry, maxWaitMins);
        }
    }

    public class Evaluator
    {
        const string SystemError = "$SystemError$";
        public static ReaderWriterLock logWriteLock = new ReaderWriterLock();
        public CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        string gateWay;
        string serviceGuid;
        EvaluationSetting evalSetting;

        public int TotalNumOfImages = 0;
        public int TriedNumOfImages = 0;    // globally retried images will be counted multiple times here
        public int SucceededNumOfImages = 0;

        public DateTime timeStart;

        public Evaluator(string gateWay, string serviceGuid, EvaluationSetting evalSetting)
        {
            this.gateWay = gateWay;
            this.serviceGuid = serviceGuid;
            this.evalSetting = evalSetting;
        }

        string GetLastUnfinishedLog(string serviceGuid)
        {
            var files = Directory.GetFiles(evalSetting.evaluationLogDir, serviceGuid + "*.log")
                .Select(f => new { file = f, dateTime = File.GetLastWriteTime(f) })
                .OrderByDescending(f => f.dateTime)
                .ToArray();
            if (files.Count() > 0)
                return files.First().file;
            else
                return null;
        }

        public async Task Eval(int concurrency, CancellationToken cancelToken, bool isResume)
        {
            if (!Directory.Exists(evalSetting.evaluationLogDir))
                Directory.CreateDirectory(evalSetting.evaluationLogDir);

            timeStart = DateTime.UtcNow;

            string logDetail = null;
            if (isResume)
                logDetail = GetLastUnfinishedLog(serviceGuid);

            if (string.IsNullOrEmpty(logDetail))
            {
                logDetail = Path.Combine(evalSetting.evaluationLogDir,
                        string.Format("{0}_{1}.log", serviceGuid.ToString(), timeStart.ToString("yyyyMMdd_HHmmss")));
            }
            else
            {
                Console.WriteLine("{0}: resume from {1}", serviceGuid, logDetail);
            }

            if (!File.Exists(evalSetting.measurementSetCountFile))
            {
                Console.WriteLine("Count total number of images in the measurement set");
                int count = File.ReadLines(evalSetting.measurementSetFile).Count();
                File.WriteAllText(evalSetting.measurementSetCountFile, count.ToString());
            }
            TotalNumOfImages = Convert.ToInt32(File.ReadLines(evalSetting.measurementSetCountFile).First());
            Console.WriteLine("{0}: total images to evaluate: {1}", serviceGuid, TotalNumOfImages);

            int colImageKey = evalSetting.columns["imagekey"];
            int colImageData = evalSetting.columns["imagedata"];
            int colRecogResult = colImageData;

            TriedNumOfImages = 0;
            double minutes_to_wait = 1;
            int global_retry = 0;
            for (global_retry = 0; global_retry < evalSetting.maxGlobalRetry; global_retry++)
            {
                HashSet<string> succeededImageKeys;
                if (File.Exists(logDetail))
                {
                    var cols_expected = File.ReadLines(evalSetting.measurementSetFile)
                        .Select(line => line.Split('\t').Length)
                        .First() + 1;       // +1 for time stamp in log file

                    succeededImageKeys = new HashSet<string>(File.ReadLines(logDetail)
                        .Select(line => line.Split('\t'))
                        .Where(cols => cols.Length == cols_expected)  // filter corrupted lines due to break and resume
                        .Where(cols => !cols[colRecogResult].StartsWith(SystemError))
                        .Select(cols => cols[colImageKey])
                        .Distinct(),
                        StringComparer.Ordinal);
                    SucceededNumOfImages = succeededImageKeys.Count();
                    Console.WriteLine("{0}: succeeded images: {1}", serviceGuid, SucceededNumOfImages);
                }
                else
                    succeededImageKeys = new HashSet<string>();

                if (succeededImageKeys.Count() >= TotalNumOfImages)
                    break;

                if (global_retry > 0)
                {
                    //double minutes_to_wait = Math.Min(evalSetting.maxWaitMins, 1.0 * Math.Pow(2, global_retry - 1));
                    double minutes = Math.Min(evalSetting.maxWaitMins, minutes_to_wait);
                    Console.WriteLine("{0}: retry {1}, wait for {2:F2} minutes...", serviceGuid, global_retry, minutes);
                    await Task.Delay(TimeSpan.FromMinutes(minutes), cancelToken);
                    minutes_to_wait *= 2;
                }

                GatewayHttpInterface vmHub = new GatewayHttpInterface(gateWay, Guid.Empty, "SecretKeyShouldbeLongerThan10");

                // run evaluation in parallel
                var lines = File.ReadLines(evalSetting.measurementSetFile)
                    .AsParallel().AsOrdered().WithDegreeOfParallelism(concurrency)
                    .Select(line => line.Split('\t'))
                    .Where(cols => !succeededImageKeys.Contains(cols[colImageKey]))
                    //.Where(cols => !string.IsNullOrEmpty(cols[1]))
                    .Select(async cols => 
                    {
                        if (cancelToken.IsCancellationRequested)
                        {
                            cols[colRecogResult] = string.Empty;
                            return cols;
                        }

                        byte[] imageData = Convert.FromBase64String(cols[colImageData]);

                        string result = string.Empty;
                        for (int local_retry = 0; local_retry < 3; local_retry++)
                        {
                            try
                            {
                                result = await vmHub.ProcessAsyncString(Guid.Empty, Guid.Empty, Guid.Parse(serviceGuid), Guid.Empty, Guid.Empty, imageData);
                                result = result.Trim();
                                if (result.IndexOf("return 0B.") >= 0)
                                    result = SystemError + ": " + result;
                            }
                            catch (Exception)
                            {
                                // timeout or other system error
                                Console.WriteLine("\n{0}: Fails to get a reply: {1}", SystemError, serviceGuid);
                                result = SystemError + ": Fails to get a reply"; // SystemError + e.Message;
                            }
                            if (!result.StartsWith(SystemError))
                                break;
                        }
                        if (!result.StartsWith(SystemError))
                        {
                            Interlocked.Increment(ref SucceededNumOfImages);
                            minutes_to_wait = 1;
                        }

                        Interlocked.Increment(ref TriedNumOfImages);
                        Console.Write("Lines processed: {0}\r", TriedNumOfImages);
                        //Console.WriteLine("{0}: {1}", ProcessedNumOfImages, result);
                        cols[colRecogResult] = result;
                        return cols;
                    })
                    .Select(cols => string.Join("\t", cols.Result) + "\t" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss.fff"));

                using (var sw = File.AppendText(logDetail))
                {
                    int count = 0;
                    foreach (var line in lines)
                    {
                        if (cancelToken.IsCancellationRequested)
                        {
                            Console.WriteLine("Cancel requested. Lines logged: {0}", count);
                            sw.Flush();
                            break;
                        }
                        sw.WriteLine(line);
                        if (++count % 100 == 0)
                            sw.Flush();
                    }
                }
            }
            DateTime timeEnd = DateTime.UtcNow;

            //// Move to offline evaluation
            //// calculate accuracy
            //var accuracies = File.ReadLines(logDetail)
            //    .Select(line => line.Split('\t'))
            //    .Where(cols => !string.IsNullOrEmpty(cols[1]))
            //    .Select(cols =>
            //    {
            //        string label = cols[1].ToLower();
            //        var result = cols[2].Split(';')
            //            .Select(r => r.Split(':')[0].Trim().ToLower())
            //            .Take(5)
            //            .ToArray();

            //        if (result.Length == 0)
            //            return Tuple.Create(false, false);
            //        else
            //            return Tuple.Create(string.Compare(label, result[0]) == 0, Array.IndexOf(result, label) >= 0);
            //    })
            //    .ToArray();
            //var top1_acc = (float)accuracies.Sum(tp => tp.Item1 ? 1 : 0) / accuracies.Count();
            //var top5_acc = (float)accuracies.Sum(tp => tp.Item2 ? 1 : 0) / accuracies.Count();

            // write to log
            try
            {
                logWriteLock.AcquireWriterLock(1000 * 600); // wait up to 10 minutes
                using (var sw = File.AppendText(evalSetting.measurementResultFile))
                {
                    //sw.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}", serviceGuid, timeStart, timeEnd, accuracies.Count(), SucceededNumOfImages, top1_acc, top5_acc);
                    TimeSpan span = timeEnd - timeStart;
                    string log_msg = string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}", 
                        serviceGuid, Path.GetFileName(logDetail), timeStart, timeEnd, 
                        global_retry, TriedNumOfImages, SucceededNumOfImages, 
                        TriedNumOfImages / span.TotalSeconds);
                    sw.WriteLine(log_msg);
                    Console.WriteLine();
                    Console.WriteLine(log_msg);
                }
                logWriteLock.ReleaseLock();
            }
            catch (ApplicationException)
            {
                Console.WriteLine("Time out in getting write permission for eval result file: {0}", evalSetting.measurementResultFile);
            }
        }
    }
}
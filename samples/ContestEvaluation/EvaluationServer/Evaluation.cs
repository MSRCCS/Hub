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

namespace EvaluationServer
{
    public class EvaluationSetting
    {
        // This is the ground truth file used for evaluation.
        // Each line corresponding one image, with TAB-separated columns defined as:
        //      image_id, ground_truth_label, based64_encoded_image_stream
        public string measurementSetFile = "measurement_set.tsv";
        public string measurementSetCountFile = "measurement_set.count.tsv";        // just store the total number of images for progress report
        
        // This file saves the evaluation result for each run.
        // Each line for one evaluation run, logged after the whole evaluation process is completed
        // The TAB-separated columns are defined as:
        // log_id, provider_name, eval_start_time, eval_end_time, count_for_accuracy_computation, succeeded_total_images, top1_accuracy, top5_accuracy
        public string measurementResultFile = "measurement_result.tsv";

        // This is the sub folder under rootDir, used for storing detailed evaluation results for 
        // every evaluation request, one file for one request.
        public string evaluationLogDir = "eval_log";
    }

    public class Evaluator
    {
        const string SystemError = "$SystemError$";
        public static ReaderWriterLock logWriteLock = new ReaderWriterLock();
        public CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        GatewayHttpInterface vmHub;
        string serviceGuid;
        string rootDir;
        EvaluationSetting evalSetting;

        public int TotalNumOfImages = 0;
        public int ProcessedNumOfImages = 0;
        public int SucceededNumOfImages = 0;

        public DateTime timeStart;

        public Evaluator(GatewayHttpInterface vmHub, string serviceGuid, string rootDir, EvaluationSetting evalSetting)
        {
            this.vmHub = vmHub;
            this.serviceGuid = serviceGuid;
            this.rootDir = rootDir;
            this.evalSetting = evalSetting;
        }

        public void Eval(int concurrency, CancellationToken cancelToken)
        {
            var getPath = new Func<string, string>(x => Path.Combine(rootDir, x));

            if (!Directory.Exists(getPath(evalSetting.evaluationLogDir)))
                Directory.CreateDirectory(getPath(evalSetting.evaluationLogDir));

            timeStart = DateTime.UtcNow;
            string logDetail = Path.Combine(rootDir, evalSetting.evaluationLogDir,
                    string.Format("{0}_{1}.log", serviceGuid.ToString(), timeStart.ToString("yyyyMMdd_HHmmss")));

            if (!File.Exists(getPath(evalSetting.measurementSetCountFile)))
            {
                Console.WriteLine("Count total number of images in the measurement set");
                int count = File.ReadLines(getPath(evalSetting.measurementSetFile)).Count();
                File.WriteAllText(getPath(evalSetting.measurementSetCountFile), count.ToString());
            }
            TotalNumOfImages = Convert.ToInt32(File.ReadAllLines(getPath(evalSetting.measurementSetCountFile))[0]);

            // run evaluation in parallel
            {
                ProcessedNumOfImages = 0;
                var lines = File.ReadLines(getPath(evalSetting.measurementSetFile))
                    .AsParallel().AsOrdered().WithDegreeOfParallelism(concurrency)
                    .Select(line => line.Split('\t'))
                    //.Where(cols => !string.IsNullOrEmpty(cols[1]))
                    .Select(async cols => 
                    {
                        if (cancelToken.IsCancellationRequested)
                        {
                            cols[2] = string.Empty;
                            return cols;
                        }

                        byte[] imageData = Convert.FromBase64String(cols[2]);

                        string result = string.Empty;
                        for (int retry = 0; retry < 10; retry++)
                        {
                            try
                            {
                                result = await vmHub.ProcessAsyncString(Guid.Empty, Guid.Empty, Guid.Parse(serviceGuid), Guid.Empty, Guid.Empty, imageData);
                                result = result.Trim();
                            }
                            catch (Exception)
                            {
                                // timeout or other system error
                                result = string.Empty; // SystemError + e.Message;
                            }
                            if (result.IndexOf("return 0B.") < 0)
                                break;
                        }
                        if (result.IndexOf("return 0B.") < 0)
                            Interlocked.Increment(ref SucceededNumOfImages);

                        Interlocked.Increment(ref ProcessedNumOfImages);
                        Console.Write("Lines processed: {0}\r", ProcessedNumOfImages);
                        //Console.WriteLine("{0}: {1}", ProcessedNumOfImages, result);
                        cols[2] = result;
                        return cols;
                    })
                    .Select(cols => string.Join("\t", cols.Result) + "\t" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss.fff"));

                using (var sw = new StreamWriter(logDetail))
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

            // calculate accuracy
            var accuracies = File.ReadLines(logDetail)
                .Select(line => line.Split('\t'))
                .Where(cols => !string.IsNullOrEmpty(cols[1]))
                .Select(cols =>
                {
                    string label = cols[1].ToLower();
                    var result = cols[2].Split(';')
                        .Select(r => r.Split(':')[0].Trim().ToLower())
                        .Take(5)
                        .ToArray();

                    if (result.Length == 0)
                        return Tuple.Create(false, false);
                    else
                        return Tuple.Create(string.Compare(label, result[0]) == 0, Array.IndexOf(result, label) >= 0);
                })
                .ToArray();
            var top1_acc = (float)accuracies.Sum(tp => tp.Item1 ? 1 : 0) / accuracies.Count();
            var top5_acc = (float)accuracies.Sum(tp => tp.Item2 ? 1 : 0) / accuracies.Count();

            // write to log
            try
            {
                logWriteLock.AcquireWriterLock(1000 * 600); // wait up to 10 minutes
                using (var sw = File.AppendText(getPath(evalSetting.measurementResultFile)))
                {
                    sw.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}", serviceGuid, timeStart, timeEnd, accuracies.Count(), SucceededNumOfImages, top1_acc, top5_acc);
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
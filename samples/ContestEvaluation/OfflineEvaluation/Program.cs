﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using CmdParser;

namespace OfflineEvaluation
{
    class Program
    {
        const string SystemError = "$SystemError$";

        class ArgsDogICME16
        {
            #pragma warning disable 0649    // suppress the warning for "Field ... is never assigned to"
            [Argument(ArgumentType.MultipleUnique, HelpText = "Input log file generated by EvaluationServer.")]
            public string[] log;
            #pragma warning restore 0649
        }

        static void DogICME16(ArgsDogICME16 cmd)
        {
            int valid_num = 0;
            int processed_num = 0;
            var accuracies = cmd.log.SelectMany(logfile => File.ReadLines(logfile))
                .Where(line => !string.IsNullOrEmpty(line))
                .Select(line => line.Split('\t'))
                .Where(cols => cols.Count() >= 3)
                .GroupBy(cols => cols[0])
                .Select(g =>
                {
                    var valid_lines = g.AsEnumerable()
                        .Where(cols => !cols[2].StartsWith(SystemError));
                    if (valid_lines.Count() > 0)
                        return valid_lines.First();

                    return g.AsEnumerable().First();
                })
                .Where(cols =>
                {
                    processed_num++;
                    return !string.IsNullOrEmpty(cols[1]);
                })
                .Select(cols =>
                {
                    string label = cols[1].ToLower();
                    var result = cols[2].Split(';')
                        .Select(r => r.Split(':')[0].Trim().ToLower())
                        .Take(5)
                        .ToArray();

                    if (!cols[2].StartsWith(SystemError))
                        valid_num++;

                    if (result.Length == 0)
                        return Tuple.Create(false, false);
                    else
                        return Tuple.Create(string.Compare(label, result[0]) == 0, Array.IndexOf(result, label) >= 0);
                })
                .ToArray();
            var top1_acc = (float)accuracies.Sum(tp => tp.Item1 ? 1 : 0) / accuracies.Count();
            var top5_acc = (float)accuracies.Sum(tp => tp.Item2 ? 1 : 0) / accuracies.Count();

            Console.WriteLine("Processed: {0}, evaluated: {1}, valid: {2}, top1_acc: {3}, top5_acc: {4}", processed_num, accuracies.Count(), valid_num, top1_acc, top5_acc);
        }

        class ArgsCelebMM16
        {
            [Argument(ArgumentType.Required, HelpText = "Config file for the evaluation task.")]
            public string config = null;
            [Argument(ArgumentType.Required, HelpText = "Input log file generated by EvaluationServer.")]
            public string log = null;
            [Argument(ArgumentType.AtMostOnce, HelpText = "Dump perf list file (default: false)")]
            public bool dump = false;
        }

        static dynamic[] CalcPrecisionCoverage(IEnumerable<dynamic> source, int total)
        {
            var src = source.ToArray();
            int correct_count = 0;
            var perf_list = src.Select((x, idx) =>
            {
                if (x.check)
                    correct_count++;
                float coverage = (float)(idx + 1) / total;
                float precision = (float)correct_count / (idx + 1);
                return new { x.label, x.result, x.check, x.conf, correct_count, precision, coverage };
            });

            return perf_list.ToArray();
        }

        static void OutputPerf(dynamic[] perf_list, float prec)
        {
            var t = perf_list.Where(x => x.precision >= prec).ToArray();
            if (t.Length > 0)
                Console.WriteLine("Prec = {0}, Coverage = {1}, Conf = {2}", prec, t.Last().coverage, t.Last().conf);
            else
                Console.WriteLine("Prec = {0}, Coverage = {1}, Conf = ###", prec, 0);
        }

        static void CelebMM16(ArgsCelebMM16 cmd)
        {
            var config = File.ReadLines(cmd.config)
                .Where(line => line.Trim().StartsWith("#") == false)
                .Select(line => line.Split(':'))
                .ToDictionary(cols => cols[0].Trim(), cols => cols[1].Trim(), StringComparer.OrdinalIgnoreCase);

            var columns = config["columns"]
                .Split(new char[] { ' ', ';', ',' })
                .Select((col, idx) => Tuple.Create(col.Trim(), idx))
                .Where(tp => !string.IsNullOrEmpty(tp.Item1))
                .ToDictionary(tp => tp.Item1, tp => tp.Item2);

            var label_flags = File.ReadLines(Path.Combine(Path.GetDirectoryName(cmd.config), config["testfile"]))
                .Select(line => line.Split('\t'))
                .Select(cols => Tuple.Create(cols[columns["label"]], Convert.ToInt32(cols[columns["flag"]])))
                .ToArray();

            int set1_total = label_flags.Where(x => (x.Item2 & 0x1) > 0).Count();
            int set2_total = label_flags.Where(x => (x.Item2 & 0x2) > 0).Count();

            Console.WriteLine("Evaluation task: {0}", config["service_name"]);
            Console.WriteLine("In ground truth: Set1 (hard) images: {0}, Set2 (random) images: {1}", set1_total, set2_total);

            var cols_expected = File.ReadLines(cmd.log)
                .Select(line => line.Split('\t').Length)
                .GroupBy(x => x)
                .Select(g => Tuple.Create(g.Key, g.Count()))
                .OrderByDescending(x => x.Item2)
                .First()
                .Item1;

            var valid_results = File.ReadLines(cmd.log)
                .Select(line => line.Split('\t'))
                .Where(cols => cols.Length == cols_expected)  // filter corrupted lines due to break and resume
                .Where(cols => !cols[columns["imagedata"]].StartsWith(SystemError) 
                                    && cols[columns["imagedata"]].IndexOf("return 0B.") < 0)    // for backward compatibility
                .Select(cols =>
                {
                    string label = cols[columns["label"]].ToLower();
                    var recog = cols[columns["imagedata"]].Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => r.Trim().Split(':'))
                        .Where(rs => rs.Length >= 2)
                        .Select(rs =>
                        {
                            float confidence;
                            try
                            {
                                confidence = Convert.ToSingle(rs[1].Trim());
                            }
                            catch (Exception)
                            {
                                confidence = float.MinValue;
                            }
                            return Tuple.Create(rs[0].Trim().ToLower(), confidence);
                        })
                        .Take(5)
                        .ToArray();
                    var result = recog.Select(x => x.Item1).ToArray();
                    var conf = recog.Select(x => x.Item2).ToArray();

                    var check = (result.Length == 0) 
                        ? Tuple.Create(false, false)
                        : Tuple.Create(string.Compare(label, result[0]) == 0, Array.IndexOf(result, label) >= 0);

                    return new { label, flag = Convert.ToInt32(cols[columns["flag"]]), result = cols[columns["imagedata"]], check, conf};
                })
                .ToArray();

            var set1 = valid_results.Where(x => (x.flag & 0x1) > 0).Select(x => new {x.label, x.result, x.check, x.conf }).ToArray();
            var set2 = valid_results.Where(x => (x.flag & 0x2) > 0).Select(x => new {x.label, x.result, x.check, x.conf }).ToArray();
            Console.WriteLine("Tested: Set1 images: {0}, Set2 images: {1}", set1.Count(), set2.Count());
            if (set1.Count() == 0 && set2.Count() == 0)
                return;

            var set1_rank_top1 = set1.Select(x => new { x.label, x.result, check = x.check.Item1, conf = x.conf.Length == 0 ? float.MinValue : x.conf.First() })
                                    .OrderByDescending(x => x.conf);
            //var set1_rank_top5 = set1.Select(x => new { x.label, x.result, check = x.check.Item2, conf = x.conf.Length == 0 ? float.MinValue : x.conf.Last() })
            //                        .OrderByDescending(x => x.Item2);
            var set2_rank_top1 = set2.Select(x => new { x.label, x.result, check = x.check.Item1, conf = x.conf.Length == 0 ? float.MinValue : x.conf.First() })
                                    .OrderByDescending(x => x.conf);
            //var set2_rank_top5 = set2.Select(x => new { x.label, x.result, check = x.check.Item2, conf = x.conf.Length == 0 ? float.MinValue : x.conf.Last() })
            //                        .OrderByDescending(x => x.Item2);

            Console.WriteLine("DataSet1 (Difficult)");
            var set1_top1_list = CalcPrecisionCoverage(set1_rank_top1, set1_total);
            OutputPerf(set1_top1_list, 0.95f);
            OutputPerf(set1_top1_list, 0.99f);

            Console.WriteLine("DataSet2 (Random)");
            var set2_top1_list = CalcPrecisionCoverage(set2_rank_top1, set2_total);
            OutputPerf(set2_top1_list, 0.95f);
            OutputPerf(set2_top1_list, 0.99f);

            if (cmd.dump)
            {
                var lines1 = set1_top1_list.Select(x => ((string)x.label + "\t" + (string)x.result + "\t" + (bool)x.check + "\t" + (float)x.conf + "\t" + (int)x.correct_count + "\t" + (float)x.precision + "\t" + (float)x.coverage));
                File.WriteAllLines(Path.ChangeExtension(cmd.log, ".set1.tsv"), lines1);

                var lines2 = set2_top1_list.Select(x => ((string)x.label + "\t" + (string)x.result + "\t" + (bool)x.check + "\t" + (float)x.conf + "\t" + (int)x.correct_count + "\t" + (float)x.precision + "\t" + (float)x.coverage));
                File.WriteAllLines(Path.ChangeExtension(cmd.log, ".set2.tsv"), lines2);
            }
        }

        class ArgsCheckLog
        {
            [Argument(ArgumentType.Required, HelpText = "Input log file generated by EvaluationServer.")]
            public string log = null;
            [Argument(ArgumentType.Required, HelpText = "Result column index")]
            public int result = -1;
            [Argument(ArgumentType.AtMostOnce, HelpText = "Label column index (default: 0)")]
            public int label = 0;
            [Argument(ArgumentType.AtMostOnce, HelpText = "Include distractor result (default: false)")]
            public bool distractor = false;
            [Argument(ArgumentType.AtMostOnce, HelpText = "Print out all valid lines (default: false)")]
            public bool print = false;
        }

        static void CheckLog(ArgsCheckLog cmd)
        {
            var cols_expected = File.ReadLines(cmd.log)
                .Select(line => line.Split('\t').Length)
                .GroupBy(x => x)
                .Select(g => Tuple.Create(g.Key, g.Count()))
                .OrderByDescending(x => x.Item2)
                .First()
                .Item1;

            int total_raw_lines = 0;
            var lines = File.ReadLines(cmd.log)
                .Select(line =>
                {
                    total_raw_lines++;
                    return line.Split('\t');
                })
                .Where(cols => cols.Length == cols_expected)  // filter corrupted lines due to break and resume
                .Where(cols => !cols[cmd.result].StartsWith(SystemError)
                                 && cols[cmd.result].IndexOf("return 0B.") < 0)   // not a system error
                .Where(cols => cmd.distractor || !string.IsNullOrEmpty(cols[cmd.label]))
                .Select(cols =>
                {
                    // try parse result format
                    if (cols[cmd.result].Split(';').Length > 0)
                    {
                        try
                        {
                            cols[cmd.result].Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(r => r.Trim().Split(':'))
                                .Select(rs => Tuple.Create(rs[0].Trim().ToLower(), Convert.ToSingle(rs[1].Trim())))
                                .ToArray();
                        }
                        catch
                        {
                            Console.WriteLine("Error line: {0}", string.Join("\t", cols));
                        }
                    }

                    return cols;
                })
                .Select(cols => cols[cmd.label] + "\t" + cols[cmd.result])
                .ToArray();

            if (cmd.print)
                foreach (var line in lines)
                    Console.WriteLine(line);

            Console.WriteLine("Total raw lines: {0}", total_raw_lines);
            Console.WriteLine("Total valid lines: {0}", lines.Length);
            Console.WriteLine("Valid cols: {0}", cols_expected);
        }

        static void Main(string[] args)
        {
            ParserX.AddTask<ArgsDogICME16>(DogICME16, "Dog@ICME16 evaluation");
            ParserX.AddTask<ArgsCelebMM16>(CelebMM16, "Celeb@MM16 evaluation");
            ParserX.AddTask<ArgsCheckLog>(CheckLog, "Check log file");

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Drawing;
using CaffeLibMC;

namespace Prajna.Vision.Caffe
{
    public class CaffePredictor
    {
        protected CaffeModel _caffeModel = new CaffeModel();
        protected string[] _labelMap = null;
        protected Dictionary<string, string> _entityInfo = null;

        public void Init(string recogProtoFile, string recogModelFile, string recogLabelMapFile, string entityInfoFile = null)
        {
            // Init face recognition
            string protoFile = Path.GetFullPath(recogProtoFile);
            string modelFile = Path.GetFullPath(recogModelFile);
            string labelMapFile = Path.GetFullPath(recogLabelMapFile);
            string curDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Path.GetDirectoryName(recogProtoFile));
            _caffeModel.Init(protoFile, modelFile, labelMapFile, true);
            Directory.SetCurrentDirectory(curDir);
            Console.WriteLine("Succeed: Load Model File!\n");

            _labelMap = File.ReadAllLines(recogLabelMapFile)
                .Select(line => line.Split('\t')[0])
                .ToArray();

            if (!string.IsNullOrEmpty(entityInfoFile))
            {
                _entityInfo = File.ReadLines(entityInfoFile)
                    .Select(line => line.Split('\t'))
                    .ToDictionary(cols => cols[0], cols => cols[1]);
            }
        }

        public string Predict(Bitmap bmp, int topK, float minConfidence)
        {
            // predict
            float[] probs = _caffeModel.ExtractOutputs(bmp, "prob");

            // get top K
            var topKResult = probs.Select((score, k) => new KeyValuePair<int, float>(k, score))
                                .Where(kv => kv.Value > minConfidence)
                                .OrderByDescending(kv => kv.Value)
                                .Take(topK)
                                .Select(kv =>
                                {
                                    var label = _labelMap[kv.Key];
                                    if (_entityInfo != null && _entityInfo.ContainsKey(label))
                                        label = _entityInfo[label];
                                    return new KeyValuePair<string, float>(label, kv.Value);
                                });

            // output
            string result = string.Join("; ", topKResult.Select(kv => string.Format("{0}:{1}", kv.Key, kv.Value)));

            return result;
        }

    }

}

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
        protected CaffeModel _caffeModel;
        protected string[] _labelMap = null;
        protected Dictionary<string, string> _entityInfo = null;

        public void Init(string protoFile, string modelFile, string meanFile, string labelMapFile, string entityInfoFile, int gpu)
        {
            // Init caffe model
            CaffeModel.SetDevice(gpu);
            _caffeModel = new CaffeModel(protoFile, modelFile);
            _caffeModel.SetMeanFile(meanFile);
            Console.WriteLine("Succeed: Load Model File!\n");

            _labelMap = File.ReadAllLines(labelMapFile)
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
            float[] probs = _caffeModel.ExtractOutputs(new Bitmap[] { bmp }, "prob");

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

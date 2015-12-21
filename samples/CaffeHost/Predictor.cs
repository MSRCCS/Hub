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

        public void Init(string recogProtoFile, string recogModelFile, string recogLabelMapFile)
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
        }

        public string Predict(Bitmap bmp, int topK, float minConfidence)
        {
            // predict
            float[] probs = _caffeModel.ExtractOutputs(bmp, "prob");

            // get top K
            var topKResult = probs.Select((score, k) => new KeyValuePair<int, float>(k, score))
                                .OrderByDescending(kv => kv.Value)
                                .Take(topK).Where(kv => kv.Value > minConfidence);

            // output
            string result = string.Join("; ", topKResult.Select(kv => string.Format("{0}:{1}", _labelMap[kv.Key], kv.Value)));

            return result;
        }

    }

}

﻿#nowarn "40" // recursive object initialization 

#r "System.Windows.Forms"

#r @"..\..\packages\EmguCV_CUDA_MSRCCS_Private\lib\net45\Emgu.CV.World.dll"
#r @"..\..\packages\EmguCV_CUDA_MSRCCS_Private\lib\net45\Emgu.CV.UI.dll"

#r @"..\..\paket-files\onenet11\FaceRecognition\faceSDKv2\FaceSdkManagedWrapper.dll"

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Threading
open System.Drawing
open System.Windows.Forms

open Emgu.CV
open Emgu.CV.Cuda
open Emgu.CV.UI
open Emgu.CV.Structure
open Emgu.CV.Features2D

#load @"Base.fsx" 
#load @"SimpleTracker.fsx" 
#load @"OpticalFlowTracker.fsx" 
open Base
open SimpleTracker
open OpticalFlowTracker

let faceSDKFaceDetector() : Mat -> Rectangle[] =
    let detector = 
        let modelFile = __SOURCE_DIRECTORY__ + @"\..\..\paket-files\onenet11\FaceRecognition\faceSDKv2\ModelFile\ProductCascadeJDA27ptsWithLbf.mdl"
        let model = new FaceSdk.Model()
        model.Load modelFile
        new FaceSdk.FaceDetectionJDA(model, 6)
    fun frame ->
        let rects = detector.Detect(FaceSdk.ImageUtility.LoadImageFromBitmapAsGray(frame.Bitmap))
        rects |> Array.map (fun rect -> Rectangle(rect.Left, rect.Top, rect.Width, rect.Height))

let openCVFaceDetector() : Mat -> Rectangle[] =
    let file = @"\\onenet11\PrajnaHubDependencies\Top 10 Ensemble Comedy Movie Casts.mp4"
    let classifierFiles = 
        let baseFolder = __SOURCE_DIRECTORY__ + @"\..\..\packages\EmguCV_CUDA_MSRCCS_Private\data\haarcascades" 
        [| "haarcascade_frontalface_default.xml" |] 
        |> Array.map (fun file -> Path.Combine(baseFolder, file))
    let detectors = 
        classifierFiles
        |> Array.map (fun file -> new CascadeClassifier(file) (* new CascadeClassifier(file, ScaleFactor = 1.2, MinNeighbors = 7, MinObjectSize = Size(20,20)) *))
    fun frame -> 
        use grayFrame = new UMat()
        CvInvoke.CvtColor(frame, grayFrame, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray)
        CvInvoke.EqualizeHist(grayFrame, grayFrame)
        detectors 
        |> Array.map (fun detector -> detector.DetectMultiScale(grayFrame, scaleFactor=1.2, minNeighbors=7, minSize=Size(20,20)))
        |> Array.concat

let openCVGpuFaceDetector() : Mat -> Rectangle[] =
    let file = @"\\onenet11\PrajnaHubDependencies\Top 10 Ensemble Comedy Movie Casts.mp4"
    let classifierFiles = 
        let baseFolder = __SOURCE_DIRECTORY__ + @"\..\..\packages\EmguCV_CUDA_MSRCCS_Private\data\haarcascades\cuda" 
        [| "haarcascade_frontalface_default.xml" (*; "haarcascade_profileface.xml"*) |] 
        |> Array.map (fun file -> Path.Combine(baseFolder, file))
    let detectors = 
        classifierFiles
        |> Array.map (fun file -> new CudaCascadeClassifier(file, ScaleFactor = 1.2, MinNeighbors = 7, MinObjectSize = Size(20,20)))
    fun frame -> 
        use gpuGrayFrame = new GpuMat()
        use grayFrame = new UMat()
        use output = new GpuMat()
        CvInvoke.CvtColor(frame, grayFrame, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray)
        CvInvoke.EqualizeHist(grayFrame, grayFrame)
        gpuGrayFrame.Upload(grayFrame)
        detectors 
        |> Array.map (fun detector ->
            detector.DetectMultiScale(gpuGrayFrame, output)
            detector.Convert(output))
        |> Array.concat

let readFrames (file:string option) : int * int * Mat seq =
    let videoCapture = match file with | Some(filename) -> new Capture(filename) | _ -> new Capture()
    let numFrames = videoCapture.GetCaptureProperty(CvEnum.CapProp.FrameCount) |> int 
    let width = videoCapture.GetCaptureProperty(CvEnum.CapProp.FrameWidth) |> int
    let height = videoCapture.GetCaptureProperty(CvEnum.CapProp.FrameHeight) |> int
    let frames = 
        let max = if numFrames > 0 then int64 numFrames else Int64.MaxValue
        seq { 
            for _ in 1L..max do 
                yield videoCapture.QueryFrame() 
        }
    width, height, frames

let updateViewer (curFrame: int) (viewer: ImageViewer) (sw: Stopwatch) (displayFrame: Mat) (disposeOld: bool) =
    let oldFrame = viewer.ImageBox.BackgroundImage
    viewer.Invoke(Action(fun _ -> 
        viewer.Text <- sprintf "%d, %.2f fps" curFrame (1000.0 / float sw.ElapsedMilliseconds)
        sw.Restart()
        viewer.ImageBox.Image <- displayFrame
        if oldFrame <> null && disposeOld then
            oldFrame.Dispose())) |> ignore

let createStandardViewer() = 
    let viewer = new ImageViewer(Width=1024, Height=768)
    viewer.Shown.Add(fun _ -> 
        viewer.ImageBox.BackgroundImageLayout <- ImageLayout.None
        viewer.BringToFront())
    let thread = new Thread(ThreadStart(fun _ -> Application.Run(viewer) |> ignore))
    thread.Start()
    viewer

// Starts the Face Detection on the given file, if Some file is passed
// or using the camera, if None is passed
let startFaceDetection (tracker: FrameProcessor<_,_>) (file: string option) (options: Options) = 
    let mutable stillGoing = true
    let viewer = createStandardViewer()
    let w,h,frames = readFrames file
    viewer.Width <- w
    viewer.Height <- h
    let sw = Stopwatch.StartNew()
    let lowLatencyFrame = if options.LowLatencyMode then new Mat() else null
    let go() = 
        frames 
        |> Seq.mapi(fun i frame -> (i,frame))
        |> Seq.tryFind(fun (i,frame) ->
            match tracker.PushFrame frame with
            | Some displayFrame when stillGoing ->
                if options.LowLatencyMode then
                    let last = options.HistorySize - 1
                    tracker.FrameHistory.[last].CopyTo(lowLatencyFrame)
                    tracker.DrawObjects(tracker.ObjectHistory.[last], lowLatencyFrame)
                    updateViewer i viewer sw lowLatencyFrame false
                    displayFrame.Dispose()
                else
                    updateViewer i viewer sw displayFrame true
            | _ -> ()
            not stillGoing)
        |> ignore
    (new Thread(new ThreadStart(go))).Start()

//// The following code can be used for debugging and experimentation, 
//// stopping at a specific frame, and stepping through frames one by one.
//// These lines should be run without the "ThreadStart" line above.
//    let frameStepping() =
//        let frameEnum = frames.GetEnumerator()
//        let step i = 
//            let ret = frameEnum.MoveNext()
//            if ret then
//                match frameProc.PushFrame(frameEnum.Current) with
//                | Some displayFrame ->
//                    let last = options.HistorySize - 1
//                    let lastFrame = frameProc.FrameHistory.[options.HistorySize-1]
//                    frameProc.DrawObjects(frameProc.ObjectHistory.[last], lastFrame)
//                    updateViewer i viewer sw lastFrame true
//    //                updateViewer i viewer sw displayFrame true
//                | None -> ()
//            ret
//        let mutable cur = 1
//        for _ in 1..100 do //options.HistorySize do
//            step cur |> ignore
//            cur <- cur + 1

// This is the entry point of the script, which can be altered manually
// to experiment with different settings. After a change, just send the 
// function defition and "do" call to FSI to see the effects.
let testFaceDetection() =
    let options = {
        DisplayBackground = true
        DisplayOriginalRects = true
        UseInterpolatedRects = true 
        DisplayInterpolatedRects = true 
        DisplayPoints = false
        MinFramesToStay = 10
        HistorySize = 15
        MaxOpticalFlowError = 45.f
        DisplayIds = true
        FaceDetector = openCVGpuFaceDetector()
        LowLatencyMode = true
        }
    let frameProc = OpticalFlowTracker(options)
    let file = 
            None
            //Some @"\\onenet11\PrajnaHubDependencies\MoveSummery.flv"
            //Some @"\\onenet11\PrajnaHubDependencies\Tokyo - Walking around Shibuya.mp4"
            //Some @"\\onenet11\PrajnaHubDependencies\Tokyo - Walking around Shibuya 1440p.mp4"
            //Some @"\\onenet11\PrajnaHubDependencies\Crossing the street in front of Shibuya railway station, Tokyo.mp4"
            //Some @"\\onenet11\PrajnaHubDependencies\Top 10 Ensemble Comedy Movie Casts.mp4"
    startFaceDetection frameProc file options

do testFaceDetection()
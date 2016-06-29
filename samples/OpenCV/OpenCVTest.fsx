#r "System.Windows.Forms"

#r @"..\..\packages\EmguCV_CUDA_MSRCCS_Private\lib\net45\Emgu.CV.World.dll"
#r @"..\..\packages\EmguCV_CUDA_MSRCCS_Private\lib\net45\Emgu.CV.UI.dll"

#r @"..\..\paket-files\onenet11\FaceRecognition\faceSDKv2\FaceSdkManagedWrapper.dll"

//#r @"..\..\..\EntityRecognitionBinary\Lib\CelebRecognitionLib.dll"
//open CelebRecognition

open System
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

let createStandardViewer() = 
    let viewer = new ImageViewer(Width=1024, Height=768)
    viewer.Shown.Add(fun _ -> 
        viewer.Left <- 3000
        viewer.ImageBox.BackgroundImageLayout <- ImageLayout.None)
    let thread = new Thread(ThreadStart(fun _ -> Application.Run(viewer) |> ignore))
    thread.Start()
    viewer

let readFrames (file:string) : int * int * Mat seq =
    let videoCapture = new Capture(file)
    let numFrames = videoCapture.GetCaptureProperty(CvEnum.CapProp.FrameCount) |> int 
    let width = videoCapture.GetCaptureProperty(CvEnum.CapProp.FrameWidth) |> int
    let height = videoCapture.GetCaptureProperty(CvEnum.CapProp.FrameHeight) |> int
    let frames = 
        seq { 
            for _ in 1..numFrames do 
                yield videoCapture.QueryFrame() 
        }
    width, height, frames

let testFaceDetection() =

    let detectFaces : Mat -> Rectangle[] =
        let detector = 
            let modelFile = __SOURCE_DIRECTORY__ + @"..\..\..\paket-files\onenet11\FaceRecognition\faceSDKv2\ModelFile\ProductCascadeJDA27ptsWithLbf.mdl"
            let model = new FaceSdk.Model()
            model.Load modelFile
            new FaceSdk.FaceDetectionJDA(model)
        fun frame ->
            let rects = detector.Detect(FaceSdk.ImageUtility.LoadImageFromBitmapAsGray(frame.Bitmap))
            rects |> Array.map (fun rect -> Rectangle(rect.Left, rect.Top, rect.Width, rect.Height))

    let viewer = createStandardViewer()
    let w,h,frames = readFrames @"\\onenet11\PrajnaHubDependencies\Crossing the street in front of Shibuya railway station, Tokyo.mp4"
    viewer.Width <- w
    viewer.Height <- h
    let sw = Stopwatch.StartNew()
    frames |> Seq.skip 300 |> Seq.iteri (fun i frame ->
        for rect in detectFaces frame do 
            CvInvoke.Rectangle(frame, rect, Bgr(Color.Blue).MCvScalar, 2)
        use oldFrame = viewer.ImageBox.BackgroundImage
        viewer.Invoke(Action(fun _ -> 
            viewer.Text <- sprintf "%.0f fps" (1000.0 / float sw.ElapsedMilliseconds)
            sw.Restart()
            viewer.ImageBox.BackgroundImage <- frame.Bitmap)) |> ignore)

//let testFaceRecognition() =
//
//    let recognizeFaces : Mat -> CelebrityRecognitionResult[] =
//        let modelDir = @"C:\Users\brunosb\OneDrive\GitHub\EntityRecognitionBinary\env\model\celeb"
//        let predictor = new CelebrityPredictor(modelDir, 1)
//        fun img -> predictor.Predict(img.Bitmap, 0.5f)
//
//    let viewer = createStandardViewer()
//    let w,h,frames = readFrames @"\\onenet11\PrajnaHubDependencies\Top 10 Ensemble Comedy Movie Casts.mp4"
//    viewer.Width <- w
//    viewer.Height <- h
//    let sw = Stopwatch.StartNew()
//    frames |> Seq.skip 200 |> Seq.iteri (fun i frame ->
//        for result in recognizeFaces frame do 
//            CvInvoke.Rectangle(frame, Rectangle(result.Rect.X, result.Rect.Y, result.Rect.Width, result.Rect.Height), Bgr(Color.Blue).MCvScalar, 2)
//            let r = result.Rect
//            if result.RecognizedAs.Length > 0 then
//                let celeb = result.RecognizedAs.[0]
//                CvInvoke.PutText(
//                    frame, 
//                    sprintf "%s, %.3f" celeb.EntityName celeb.Confidence, 
//                    Point(r.X, r.Y + r.Height + 25), CvEnum.FontFace.HersheyTriplex, 1.0, Bgr(Color.Blue).MCvScalar)
//        use oldFrame = viewer.ImageBox.BackgroundImage
//        viewer.Invoke(Action(fun _ -> 
//            viewer.Text <- sprintf "%.0f fps" (1000.0 / float sw.ElapsedMilliseconds)
//            sw.Restart()
//            viewer.ImageBox.BackgroundImage <- frame.Bitmap)) |> ignore)

let testOpticalFlow() =
    let file = @"\\onenet11\PrajnaHubDependencies\Driving Brooklyn - Free Stock Footage - CC Attribution.mp4"
    let viewer = createStandardViewer()
    let frames = 
        seq {
            let capture = new Capture(file)
            let numFrames = capture.GetCaptureProperty(CvEnum.CapProp.FrameCount) |> int
            yield! 
                (seq {while true do yield capture.QueryFrame()}
                |> Seq.take (numFrames - 1))
        }
        |> Seq.pairwise
    let tracker = new GFTTDetector(400, 0.02, 15., 6)
    frames |> Seq.iteri (fun i (frame1, frame2) ->
        let points = tracker.Detect(frame1, null) |> Array.map (fun pt -> pt.Point)
        let mutable outPoints = Unchecked.defaultof<PointF[]>
        let mutable status = Unchecked.defaultof<byte[]>
        let mutable trackErrors = Unchecked.defaultof<float32[]>
        CvInvoke.CalcOpticalFlowPyrLK(frame1, frame2, points, Size(20, 20), 3, MCvTermCriteria(20), &outPoints, &status, &trackErrors)
        let vectors = Array.zip (Array.zip3 points outPoints trackErrors) status
        for (pt1, pt2, err),status in vectors do
            if status = 1uy && err < 8.0f then
                let mag = 5.0f
                let direction = PointF((pt2.X - pt1.X) * mag, (pt2.Y - pt1.Y) * mag)
                CvInvoke.Rectangle(frame2, Rectangle(int pt1.X, int pt1.Y, 2, 2) , Bgr(Color.Red).MCvScalar, 1)
                CvInvoke.Line(frame2, Point(int pt1.X, int pt1.Y), Point(int (pt1.X + direction.X), int (pt2.Y + direction.Y)),
                                 Bgr(Color.Red).MCvScalar, 1) 
        viewer.Text <- i.ToString()
        viewer.Image <- frame2
        viewer.Refresh()
        frame1.Dispose())

let testOpticalFlowGpu() =
    let file = @"\\onenet11\PrajnaHubDependencies\Driving Brooklyn - Free Stock Footage - CC Attribution.mp4"
    let viewer = createStandardViewer()
    let getFrames() = 
        let capture = new Capture(file)
        let numFrames = capture.GetCaptureProperty(CvEnum.CapProp.FrameCount) |> int
        let dt = capture.GetCaptureProperty(CvEnum.CapProp.FourCC)
        let frames =
            seq {
                yield! 
                    (seq {while true do yield capture.QuerySmallFrame()}
                    |> Seq.take (numFrames - 1))
            }
            |> Seq.pairwise
        numFrames, dt, frames
//    let tracker = new GFTTDetector(400, 0.02, 15., 6)
    let numFrames, dt, frames = getFrames()
    let detector = new GFTTDetector(400, 0.02, 15., 6)
    //let tracker = new Cuda.CudaFastFeatureDetector(10, false, FastDetector.DetectorType.Type9_16, 100)

    frames |> Seq.iteri (fun i (frame1, frame2) ->
        let frame1, frame2 = frames |> Seq.item 0

//        let grayFrame1 = new UMat()
//        CvInvoke.CvtColor(frame1, grayFrame1, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray)

        let corners = detector.Detect(frame1, null)

        let outPoints = new Util.VectorOfPointF()// Unchecked.defaultof<PointF[]>
        let status = new Util.VectorOfByte() //Unchecked.defaultof<byte[]>
        let trackErrors = new Util.VectorOfFloat() // Unchecked.defaultof<float32[]>
        //CvInvoke.CalcOpticalFlowPyrLK(frame1, frame2, corners, Size(20, 20), 3, MCvTermCriteria(20), &outPoints, &status, &trackErrors)

        let gpuFrame1 = new Cuda.GpuMat(frame1)
        let gpuFrame2 = new Cuda.GpuMat(frame2)
        let points = new Cuda.GpuMat()

        using (new Util.VectorOfPointF()) (fun prevPoints ->
            let prevPoints = new Util.VectorOfPointF()
            prevPoints.Push(corners |> Array.map(fun c -> c.Point))
            let optFlow = new Cuda.CudaSparsePyrLKOpticalFlow(Size(20,20), 3)
            optFlow.Calc(gpuFrame1, gpuFrame2, prevPoints, outPoints, status, trackErrors)
            )


        //detector.Detect(gpuFrame1, null)
        //detector.Detect(gpuFrame1, points, null, null)

        let mutable outPoints = Unchecked.defaultof<PointF[]>
        let mutable status = Unchecked.defaultof<byte[]>
        let mutable trackErrors = Unchecked.defaultof<float32[]>

//        for (pt1, pt2, err),status in vectors do
//                CvInvoke.Rectangle(frame2, Rectangle(int pt1.X, int pt1.Y, 2, 2) , Bgr(Color.Red).MCvScalar, 1)

//        CvInvoke.CalcOpticalFlowPyrLK(frame1, frame2, points, Size(20, 20), 3, MCvTermCriteria(20), &outPoints, &status, &trackErrors)
//        let vectors = Array.zip (Array.zip3 points outPoints trackErrors) status
//        for (pt1, pt2, err),status in vectors do
//            if status = 1uy && err < 8.0f then
//                let mag = 5.0f
//                let direction = PointF((pt2.X - pt1.X) * mag, (pt2.Y - pt1.Y) * mag)
//                CvInvoke.Rectangle(frame2, Rectangle(int pt1.X, int pt1.Y, 2, 2) , Bgr(Color.Red).MCvScalar, 1)
//                CvInvoke.Line(frame2, Point(int pt1.X, int pt1.Y), Point(int (pt1.X + direction.X), int (pt2.Y + direction.Y)),
//                                 Bgr(Color.Red).MCvScalar, 1) 
        viewer.Text <- i.ToString()
//        viewer.Image <- frame2
//        viewer.Refresh()
        frame1.Dispose()
        )
    
// TODO: This can be sped up to faster-than-real-time by doing the detection in multiple threads
let testFaces() = 
    let file = @"\\onenet11\PrajnaHubDependencies\Crossing the street in front of Shibuya railway station, Tokyo.mp4"
    let cascadeClassifierFile = __SOURCE_DIRECTORY__ + @"\..\..\packages\EmguCV_CUDA_MSRCCS_Private\data\haarcascades\haarcascade_frontalface_default.xml" 
    let viewer = createStandardViewer()
    let detector = new CascadeClassifier(cascadeClassifierFile)
    let capture = new Capture(file)
    let numFrames = capture.GetCaptureProperty(CvEnum.CapProp.FrameCount) |> int 
    viewer.Width <- capture.GetCaptureProperty(CvEnum.CapProp.FrameWidth) |> int
    viewer.Height <- capture.GetCaptureProperty(CvEnum.CapProp.FrameHeight) |> int
    let mutable count = 0
    while count < numFrames do
        use frame = capture.QueryFrame()
        use grayFrame = new UMat()
        CvInvoke.CvtColor(frame, grayFrame, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray)
        CvInvoke.EqualizeHist(grayFrame, grayFrame)
        viewer.Text <- Convert.ToString count
        let faces = detector.DetectMultiScale(frame, 1.3, 5, Size(20, 20))
        for face in faces do
            CvInvoke.Rectangle(frame, face, Bgr(Color.Blue).MCvScalar, 2)
//            CvInvoke.PutText(frame, sprintf "%d x %d" face.Height face.Width, Point(face.Right, face.Top - 10), CvEnum.FontFace.HersheyPlain, 1.0, Bgr(Color.Blue).MCvScalar)
        count <- count + 1
        viewer.Image <- frame
        viewer.Refresh()

let testFacesGpu() = 
    let file = @"\\onenet11\PrajnaHubDependencies\Top 10 Ensemble Comedy Movie Casts.mp4"
    let classifierFiles = 
        let basePath = __SOURCE_DIRECTORY__ + @"\..\..\packages\EmguCV_CUDA_MSRCCS_Private\data\haarcascades\cuda" 
        ["haarcascade_frontalface_default.xml" (*; "haarcascade_profileface.xml"*)] |> List.map (fun file -> Path.Combine(basePath, file))
    let viewer = createStandardViewer()
    let detectors = 
        classifierFiles
        |> List.map (fun file -> new CudaCascadeClassifier(file, ScaleFactor = 1.2, MinNeighbors = 5, MinObjectSize = Size(20,20)))
    let capture = new Capture(file)
    let frames = 
        let numFrames = capture.GetCaptureProperty(CvEnum.CapProp.FrameCount) |> int 
        seq { for _ in 1..numFrames do yield capture.QueryFrame() }
    viewer.Width <- capture.GetCaptureProperty(CvEnum.CapProp.FrameWidth) |> int
    viewer.Height <- capture.GetCaptureProperty(CvEnum.CapProp.FrameHeight) |> int
    let gpuGrayFrame = new GpuMat()
    let grayFrame = new UMat()
    let output = new GpuMat()
    let sw = Stopwatch.StartNew()
    frames |> Seq.iteri (fun i frame ->
        CvInvoke.CvtColor(frame, grayFrame, Emgu.CV.CvEnum.ColorConversion.Bgr2Gray)
        CvInvoke.EqualizeHist(grayFrame, grayFrame)
        gpuGrayFrame.Upload(grayFrame)
        let faceRects : Rectangle list = 
            [for detector in detectors do
                detector.DetectMultiScale(gpuGrayFrame, output)
                yield! detector.Convert(output)]
        for face in faceRects (* |> nonInstersecting *) do                    
            CvInvoke.Rectangle(frame, face, Bgr(Color.Blue).MCvScalar, 2)
//            CvInvoke.PutText(frame, sprintf "%d x %d" face.Height face.Width, Point(face.Right, face.Top - 10), CvEnum.FontFace.HersheyPlain, 1.0, Bgr(Color.Blue).MCvScalar)
        let oldImage = viewer.Image
        viewer.Text <- sprintf "%.0f fps" (1000.0 / sw.Elapsed.TotalMilliseconds)
        sw.Restart()
        viewer.Image <- frame
        if oldImage <> null then
            oldImage.Dispose()
    )

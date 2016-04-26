#r "System.Windows.Forms"

#r @"..\..\packages\EmguCV_MSRCCS_Private\lib\net45\Emgu.CV.World.dll"
#r @"..\..\packages\EmguCV_MSRCCS_Private\lib\net45\Emgu.CV.UI.dll"

open System
open System.Threading
open System.Drawing
open System.Windows.Forms

open Emgu.CV
open Emgu.CV.UI
open Emgu.CV.Structure
open Emgu.CV.Features2D

let createStandardViewer() = 
    let viewer = new ImageViewer(Width=1024, Height=768)
    viewer.Shown.Add(fun _ -> viewer.Left <- 3000)
    let thread = new Thread(ThreadStart(fun _ -> Application.Run(viewer) |> ignore))
    thread.Start()
    viewer

let testOpticalFlow() =
    let file = @"\\onenet11\PrajnaHubDependencies\Driving Brooklyn - Free Stock Footage - CC Attribution.mp4"
    let viewer = createStandardViewer()
    let frames = 
        seq {
            let capture = new Capture(file)
            let numFrames = capture.GetCaptureProperty(CvEnum.CapProp.FrameCount) |> int
            yield! 
                (seq {while true do yield capture.QuerySmallFrame()}
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
    

// TODO: This can be sped up to faster-than-real-time by doing the detection in multiple threads
let testFaces() = 
    let file = @"\\onenet11\PrajnaHubDependencies\Crossing the street in front of Shibuya railway station, Tokyo.mp4"
    let cascadeClassifierFile = __SOURCE_DIRECTORY__ + @"..\..\..\packages\EmguCV_MSRCCS_Private\data\haarcascades\haarcascade_frontalface_default.xml" 
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


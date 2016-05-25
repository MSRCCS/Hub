#r @"..\..\packages\EmguCV_CUDA_MSRCCS_Private\lib\net45\Emgu.CV.World.dll"
#r @"..\..\packages\EmguCV_CUDA_MSRCCS_Private\lib\net45\Emgu.CV.UI.dll"

open Emgu.CV
open System.Drawing

type Options = {
    mutable DisplayBackground: bool
    mutable DisplayOriginalRects: bool
    mutable DisplayInterpolatedRects: bool
    mutable UseInterpolatedRects: bool
    mutable DisplayPoints: bool
    mutable MinFramesToStay: int
    HistorySize: int
    mutable MaxOpticalFlowError: float32
    mutable DisplayIds: bool
    mutable FaceDetector: Mat -> Rectangle[]
    mutable LowLatencyMode: bool
    }

let interpolate (r1: Rectangle) (r2:Rectangle) (curPoint: int) (numPoints: int) = 
    let pointPos = (1.0 / float (numPoints + 1)) * float (curPoint + 1)
    let inline inter a b = a + int (float(b - a) * pointPos)
    Rectangle(inter r1.X r2.X, inter r1.Y r2.Y, inter r1.Width r2.Width, inter r1.Height r2.Height)

type CircularBuffer<'T>(maxLen: int) =

    let mutable start = -1
    let mutable next = 0
    let mutable count = 0
    let arr = Array.zeroCreate<'T> maxLen

    member this.Add(x: 'T) =
        arr.[next] <- x
        next <- (next + 1) % maxLen
        count <- min (count + 1) maxLen
        if count = maxLen then
            start <- (start + 1) % maxLen

    member this.Item 
        with get(i: int) : 'T = arr.[if start = -1 then i else (start + i) % maxLen]
        and set (i: int) (value: 'T) = arr.[if start = -1 then i else (start + i) % maxLen] <- value

    member this.Count = count

    member this.ToSeq() = 
        seq { for i in 0 .. count - 1 -> this.[i] } 

type ITracker<'FrameInfoType, 'DetectionType> =
    abstract DetectObjects : Mat -> 'DetectionType
    abstract InsertMissingObjects : history: CircularBuffer<'FrameInfoType> * frame: Mat * curFrameRects: 'DetectionType -> 'FrameInfoType
    abstract DrawObjects : t: 'FrameInfoType * displayFrame: Mat -> unit


[<AbstractClass>]
type FrameProcessor<'FrameInfoType, 'DetectionType>(options: Options) = 

    let objectHistory = CircularBuffer<'FrameInfoType>(options.HistorySize)
    let frameHistory = CircularBuffer<Mat>(options.HistorySize)

    let mutable curFrame = 0

    abstract DetectObjects : Mat -> 'DetectionType

    abstract InsertMissingObjects : history: CircularBuffer<'FrameInfoType> * frame: Mat * curFrameRects: 'DetectionType -> 'FrameInfoType

    abstract DrawObjects : t: 'FrameInfoType * displayFrame: Mat -> unit

    interface ITracker<'FrameInfoType, 'DetectionType> with
        member this.DetectObjects frame = this.DetectObjects frame
        member this.InsertMissingObjects(buffer, frame, detects) = this.InsertMissingObjects(buffer, frame, detects)
        member this.DrawObjects(frameInfo, frame) = this.DrawObjects(frameInfo, frame)

    member this.FrameHistory = frameHistory
    member this.ObjectHistory = objectHistory

    member this.PushFrame(frame: Mat) : Mat option =
        let objects = this.DetectObjects frame 
        let frame = 
            if options.DisplayBackground then
                frame
            else
                new Mat(frame.Size, frame.Depth, frame.NumberOfChannels)
        let displayFrame = frameHistory.[0]
        let displayRects = objectHistory.[0]
        let newRectList = this.InsertMissingObjects(objectHistory, frame, objects)
        objectHistory.Add newRectList
        frameHistory.Add frame
        curFrame <- curFrame + 1
        if curFrame > options.HistorySize then
            this.DrawObjects(displayRects, displayFrame)
            Some displayFrame
        else
            None

    member this.Finish() : Mat seq = 
        seq {
            for objects,frame in Seq.zip (objectHistory.ToSeq()) (frameHistory.ToSeq()) do 
                this.DrawObjects(objects, frame) 
                yield frame
        }


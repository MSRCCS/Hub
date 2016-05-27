#r @"..\..\packages\EmguCV_CUDA_MSRCCS_Private\lib\net45\Emgu.CV.World.dll"
#r @"..\..\packages\EmguCV_CUDA_MSRCCS_Private\lib\net45\Emgu.CV.UI.dll"

#load "Base.fsx" 

open System.Collections.Generic
open System.Drawing
open Emgu.CV
open Emgu.CV.Structure
open Base

type ColoredRect = {Rectangle: Rectangle; Color: Color; Successor: FrameRect option}

and FrameRect =
    | Native of ColoredRect
    | Inserted of ColoredRect * MatchScore: float

let getColoredRect = function
    | Native cr -> cr
    | Inserted(cr,_) -> cr

let getRect (fr: FrameRect) = (getColoredRect fr).Rectangle
let setRect (fr: FrameRect) (value: Rectangle) = 
    match fr with
    | Native cr -> Native {cr with Rectangle = value}
    | Inserted(cr,s) -> Inserted({cr with Rectangle = value}, s)

let getColor (fr: FrameRect) = (getColoredRect fr).Color
let setColor (fr: FrameRect) (value: Color) = 
    match fr with
    | Native cr -> Native {cr with Color = value}
    | Inserted(cr,s) -> Inserted({cr with Color = value}, s)

let haveSuccessor (fr: FrameRect) = (getColoredRect fr).Successor
let setSuccessor (fr: FrameRect) (value: FrameRect option) = 
    match fr with
    | Native cr -> Native {cr with Successor = value}
    | Inserted(cr,s) -> Inserted({cr with Successor = value}, s)

let area (r: Rectangle) = r.Width * r.Height

let intersect (r1: Rectangle) (r2: Rectangle) = 
    let tmp = r1
    tmp.Intersect(r2) // tsss... Intersect modifies Rectangle, which is a struct
    tmp

let matchScore (r1: Rectangle) (r2: Rectangle)  =
    let intersection = area (intersect r1 r2)
    let union = area r1 + area r2 - intersection
    float intersection / float union

let newColor =
    let rec pallete = seq { yield! [Color.Blue; Color.Magenta; Color.DarkGreen; Color.Yellow; Color.Red; Color.Cyan; Color.Violet
                                    Color.Aquamarine; Color.Azure; Color.Brown; Color.Chartreuse; Color.Crimson; Color.Khaki; Color.LawnGreen ]; yield! pallete}
    let e = pallete.GetEnumerator()
    fun () ->
        e.MoveNext() |> ignore
        e.Current

// SimpleTracker just compares the bounding boxes of detections in different frames, without any content comparison,
// and makes the connection if the overlap is greater than a fixed threshold.
// The main thing is to demonstrate the API, and how to go "deep" into the the frame stream with a trivial comparer.
type SimpleTracker(options: Options) =
    inherit FrameProcessor<List<FrameRect>, List<FrameRect>>(options)

    override __.DetectObjects (mat: Mat) = 
        let faceRects = options.FaceDetector mat |> Array.map (fun r -> Native {Rectangle=r; Color=Color.Black; Successor=None})
        List<_>(faceRects)

    override __.DrawObjects (rects: List<FrameRect>, displayFrame: Mat) =
        rects |> Seq.iter (function 
            | Native {Rectangle=rect; Color=color} -> 
                if options.DisplayOriginalRects then
                    CvInvoke.Rectangle(displayFrame, rect, Bgr(color).MCvScalar, 2)
            | Inserted({Rectangle=rect; Color=color}, score) -> 
                if options.DisplayInterpolatedRects then
                    CvInvoke.Rectangle(displayFrame, rect, Bgr(color).MCvScalar, 6))

    override __.InsertMissingObjects (history: CircularBuffer<List<FrameRect>>, _, curFrameRects: List<FrameRect>) : List<FrameRect> =
        let matchThreshold = 1.0 / 4.0
        let newRectList = new List<FrameRect>()
        for newFrameRect in curFrameRects do
            let firstMatch = 
                history.ToSeq()
                |> Seq.mapi (fun i frame -> i,frame)
                |> Seq.rev
                |> (fun fs -> 
                    if options.UseInterpolatedRects then fs 
                    else (if Seq.isEmpty fs then fs else fs |> Seq.take 1))
                |> Seq.tryPick(fun (i,frameRects) ->
                    frameRects 
                    |> Seq.fold (fun (curMax: (FrameRect * float) option) (cur: FrameRect) ->
                        if (getColoredRect cur).Successor.IsSome then
                            curMax
                        else
                            let matchVal = matchScore (getRect newFrameRect) (getRect cur)
                            if matchVal > matchThreshold then
                                match curMax with
                                | None -> Some(cur, matchVal)
                                | Some(_,prevVal) when prevVal < matchVal -> Some(cur, matchVal) 
                                | _ -> curMax
                            else
                                curMax) None
                    |> Option.map (fun (r,score) -> i,r,score))
            match firstMatch with
            | None -> 
                newRectList.Add <| Native({getColoredRect newFrameRect with Color=newColor(); Successor=None})
            | Some(frame, frameRect, score) -> 
                history.[frame].Remove(frameRect) |> ignore
                history.[frame].Add(
                    match frameRect with 
                    | Native(cr) -> Native({cr with Successor = Some newFrameRect})
                    | Inserted(cr,s) -> Inserted({cr with Successor = Some newFrameRect}, s))
                // e.g.: frame=28, historyCount=30 implies numPointsToInterpolate = 1
                let numPointsToInterpolate = history.Count - frame - 1 
                for i = frame + 1 to history.Count - 1 do
                    // interpolate from 0 to numPointsToInterpolate - 1
                    let curPointToInterpolate = i - (frame + 1) 
                    let interpolatedRect = interpolate (getRect frameRect) (getRect newFrameRect) curPointToInterpolate numPointsToInterpolate 
                    history.[i].Add(Inserted({Rectangle=interpolatedRect; Color=getColor frameRect; Successor=Some newFrameRect}, score))
                newRectList.Add <| Native({getColoredRect newFrameRect with Color=getColor frameRect})
        newRectList


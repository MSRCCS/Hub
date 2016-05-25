#r @"..\..\packages\EmguCV_CUDA_MSRCCS_Private\lib\net45\Emgu.CV.World.dll"
#r @"..\..\packages\EmguCV_CUDA_MSRCCS_Private\lib\net45\Emgu.CV.UI.dll"

#load "Base.fsx" 

open System.Collections.Generic
open System.Drawing
open Emgu.CV
open Emgu.CV.Structure
open Emgu.CV.Features2D
open Base

type RectAndPoints = {Rectangle: Rectangle; Points: Point[]; IsNative: bool}

type Entity() = 
    
    static let nextId =
        let mutable cur = 1
        fun () -> cur <- cur + 1; cur - 1

    let id = nextId()
    
    let elements = List<FrameObject>()

    member __.Id = id

    member __.Elements = elements

    member this.Add(rectAndPoints: RectAndPoints, frameNumber: int) = 
        let newFRP = new FrameObject(this, elements.Count, rectAndPoints, frameNumber)
        elements.Add(newFRP)
        newFRP

    member this.Count = elements.Count

and FrameObject internal (entity: Entity, posInEntity: int, rectAndPoints: RectAndPoints, frameNumber: int) =
    member __.Entity = entity
    member __.PosInEntity = posInEntity
    member __.RectAndPoints = rectAndPoints
    member __.FrameNumber = frameNumber
    
and FrameInfo = {Frame: Mat; FrameNumber: int; Objects : FrameObject list}

let rotateAndScale (rotation: float32) (scale: float32) (r: Rectangle) = 
    RotatedRect(PointF((r.Left + r.Right) / 2 |> float32, (r.Top + r.Bottom) / 2 |> float32), SizeF(float32 r.Width * scale, float32 r.Height * scale), rotation)

type OpticalFlowTracker(options: Options) =
    inherit FrameProcessor<FrameInfo, RectAndPoints list>(options)

    let tracker = new GFTTDetector(1000, 0.005, 2., 4, false)
    let mutable curFrame = 0

    let matchPoints (fromFrame: Mat) (toFrame: Mat) (fromFramePoints: Point[] seq) : Point[] list =
        let allFromFramePoints = 
            fromFramePoints 
            |> Seq.concat
            |> Seq.map (fun p -> PointF(float32 p.X, float32 p.Y))
            |> Seq.toArray
        let opticalFlowResult : (PointF * byte * float32) list = // matchedPoint, matchStatus, matchQuality 
            if allFromFramePoints.Length > 0 then
                let mutable outPoints = Unchecked.defaultof<PointF[]>
                let mutable status = Unchecked.defaultof<byte[]>
                let mutable trackErrors = Unchecked.defaultof<float32[]>
                CvInvoke.CalcOpticalFlowPyrLK(fromFrame, toFrame, allFromFramePoints, Size(40, 40), 3, MCvTermCriteria(20), &outPoints, &status, &trackErrors)
                Array.zip3 outPoints status trackErrors
                |> Array.toList
            else
                List.empty
        let matchedRectPoints : Point[] list = 
            fromFramePoints
            |> Seq.map (fun pts -> pts.Length)
            |> Seq.fold (fun (building,rest) numPoints ->
                let matchedPoints = 
                    rest 
                    |> Seq.take numPoints
                    |> Seq.choose (fun (p: PointF,status,err) -> 
                        if status = 1uy && err < (options.MaxOpticalFlowError) then Some(Point(int p.X, int p.Y)) else None)
                    |> Seq.toArray
                matchedPoints::building, List.skip numPoints rest) ([],opticalFlowResult)
            |> fst
            |> List.rev
        matchedRectPoints

    let bestMatchRect (frameRects: FrameObject seq) (refPoints: Point[]) : (FrameObject * int) option = 
        frameRects 
        |> Seq.fold (fun (curMax: (FrameObject * int) option) curRP ->
            let curRect = curRP.RectAndPoints.Rectangle
            let numPointsFound = refPoints |> Seq.where (fun pt -> curRect.Contains pt) |> Seq.length
            match curMax with
            | None when numPointsFound > 0 -> Some(curRP, numPointsFound)
            | Some(_,prevPointsFound) when prevPointsFound < numPointsFound -> Some(curRP, numPointsFound)
            | _ -> curMax) None

    let center (points: Point[]) = 
        let count = float32 points.Length
        let sumX, sumY = points |> Array.fold (fun (sumX,sumY) p -> sumX + p.X, sumY + p.Y) (0,0)
        PointF(float32 sumX / count, float32 sumY / count)

    let centerRect (r: Rectangle) = PointF(float32(r.Left + r.Right) / 2.0f, float32(r.Top + r.Bottom) / 2.0f)

    let distanceSquared (p1: PointF) (p2: PointF) = 
        let dx = p2.X - p1.X
        let dy = p2.Y - p1.Y
        dx * dx + dy * dy

    let matchRects (oldFrame: FrameInfo) (newFrame: Mat) (newFrameRects: RectAndPoints list) (alreadyMatched: HashSet<int>) =
        let rectsMatchedToPointsInPreviousFrame : (RectAndPoints * Point[]) list = 
            let newFramePointSeq : Point[] list = newFrameRects |> List.map (fun rp -> rp.Points)
            let oldFrameMatchedPoints : Point[] list = matchPoints newFrame oldFrame.Frame newFramePointSeq
            Seq.zip newFrameRects oldFrameMatchedPoints
            |> Seq.sortBy (fun ({Rectangle=oldRect}, oldPoints) -> distanceSquared (centerRect oldRect) (center oldPoints))
            |> Seq.toList
        let oldFrameRectsRemaining : Dictionary<int, FrameObject> =
            oldFrame.Objects 
            |> Seq.choose (fun rp -> 
                if rp.PosInEntity = rp.Entity.Count-1 && not (alreadyMatched.Contains rp.Entity.Id)
                then Some (rp.Entity.Id, rp)
                else None) 
            |> (fun pairs -> 
                let ret = new Dictionary<int, FrameObject>()
                for i,rp in pairs do
                    if ret.ContainsKey(i) then
                        System.Diagnostics.Debugger.Break()
                    else
                        ret.Add(i,rp)
                ret)
        let matched,notMatched =
            rectsMatchedToPointsInPreviousFrame 
            |> List.fold (fun (matched,notMatched) (rect,matchedPoints) ->
                if oldFrameRectsRemaining.Count = 0 then
                    matched,(rect::notMatched)
                else
                    let bestOldRect = bestMatchRect oldFrameRectsRemaining.Values matchedPoints
                    // Should we use a proper bin matching algorithm for best assignment?
                    match bestOldRect with
                    | None -> matched,(rect::notMatched)
                    | Some((oldObj,numPointsMatched)) -> 
                        do oldFrameRectsRemaining.Remove oldObj.Entity.Id |> ignore
                        do alreadyMatched.Add oldObj.Entity.Id |> ignore
                        (oldObj,rect)::matched,notMatched) 
                (list<FrameObject * RectAndPoints>.Empty, list<RectAndPoints>.Empty)
            |> (fun (m,nm) -> List.rev m, List.rev nm)
        matched,notMatched

    let colors = [|Color.Blue; Color.Magenta; Color.DarkGreen; Color.Yellow; Color.Red; Color.Cyan; Color.Violet;
                   Color.Aquamarine; Color.Azure; Color.Brown; Color.Chartreuse; Color.Crimson; Color.Khaki; Color.LawnGreen |]

    override __.DetectObjects (mat: Mat) = 
        let faceRects = options.FaceDetector mat 
        use mask =
            let mask = new Mat(mat.Size, CvEnum.DepthType.Cv8U, 1) 
            mask.SetTo(MCvScalar(0.0))
            let dummyRect = ref Rectangle.Empty
            for faceRect in faceRects do
                CvInvoke.Ellipse(mask, faceRect |> rotateAndScale 0.0f 0.8f, Bgr(Color.White).MCvScalar, 2) |> ignore
                let center = Point((faceRect.Left + faceRect.Right) / 2, (faceRect.Top + faceRect.Bottom) / 2)
                CvInvoke.FloodFill(mask, null, center, Bgr(Color.White).MCvScalar, dummyRect, MCvScalar(0.0), MCvScalar(0.0)) |> ignore
            mask
        let points = tracker.Detect(mat, mask) |> Array.map (fun pt -> Point(int pt.Point.X, int pt.Point.Y))
        let rectsAndPoints = 
            faceRects 
            |> Seq.map(fun (r: Rectangle) -> 
                let facePoints = points |> Array.where(fun pt -> r.Contains pt)
                {Rectangle=r; Points=facePoints; IsNative=true})
            |> Seq.toList
        rectsAndPoints

    override __.InsertMissingObjects (history: CircularBuffer<FrameInfo>, frame: Mat, newDetections: RectAndPoints list) : FrameInfo =
        let newRectList = new List<FrameObject>()
        let historyFrames = 
            history.ToSeq()
            |> Seq.mapi (fun i frame -> i,frame)
            |> Seq.rev
            |> (fun fs -> 
                if options.UseInterpolatedRects then fs 
                else (if Seq.isEmpty fs then fs else fs |> Seq.take 1))
            |> Seq.toList

        // This ensures we only call optical flow computation as much as needed
        // remainingRects is a sequence of rects of decreasing size
        // We zip decreasingRects with historyFrames and match pairwise
        let matches,noMatches =
            let rec matchRectsDeep' (curHistory: (int * FrameInfo) list) (curMatches: (int * FrameObject * RectAndPoints) list) (remainingRects: RectAndPoints list) (alreadyMatched: HashSet<int>) 
                : (int * FrameObject * RectAndPoints) list * RectAndPoints list =
                match curHistory,remainingRects with
                | [],_ | _,[] -> curMatches, remainingRects
                | (frameNum, headFrame)::olderFrames, _ ->
                    let matchedThisFrame,notMatchedThisFrame = matchRects headFrame frame remainingRects alreadyMatched
                    let frameTaggedMatches = matchedThisFrame |> List.map (fun (old,newF) -> frameNum,old,newF)
                    // do matchedThisFrame |> List.iter (fun (oldFRP,_) -> alreadyMatched.Add oldFRP.Id |> ignore)
                    matchRectsDeep' olderFrames (List.append frameTaggedMatches curMatches) notMatchedThisFrame alreadyMatched
            let m,nm = matchRectsDeep' historyFrames [] (Seq.toList newDetections) (new HashSet<int>())
            List.toArray m, List.toArray nm

        for historyPos,oldFRP,newFRP in matches do
            // e.g.: frame=28, historyCount=30 implies numPointsToInterpolate = 1
            let numPointsToInterpolate = history.Count - historyPos - 1 
            let mutable nextFrame = oldFRP.FrameNumber + 1
            for i = historyPos + 1 to history.Count - 1 do
                // interpolate from 0 to numPointsToInterpolate - 1
                let curPointToInterpolate = i - (historyPos + 1) 
                let interpolatedRect = {Rectangle=interpolate oldFRP.RectAndPoints.Rectangle newFRP.Rectangle curPointToInterpolate numPointsToInterpolate; Points=Array.empty; IsNative=false}
                let interpolatedObject = //{FrameRect=Inserted({Rectangle=interpolatedRect;Color=getColor oldFRP.FrameRect; Successor=None}, 0.0); Frame=nextFrame; Points=Array.empty; List=oldFRP.List; Id=oldFRP.Id}
                    oldFRP.Entity.Add(interpolatedRect, nextFrame)
                history.[i] <- {history.[i] with Objects = interpolatedObject :: history.[i].Objects}
                nextFrame <- nextFrame + 1
            let newObject = oldFRP.Entity.Add(newFRP, curFrame)
            newRectList.Add newObject 

        for rectAndPoints in noMatches do
            let entity = new Entity()
            let firstObject = entity.Add(rectAndPoints, curFrame)
            newRectList.Add firstObject

        let newFrameInfo = {Frame=frame; FrameNumber=curFrame; Objects=Seq.toList newRectList}
        curFrame <- curFrame + 1
        newFrameInfo

    override __.DrawObjects (objects: FrameInfo, displayFrame: Mat) =

        objects.Objects
        |> Seq.iter (fun frameObj -> //({FrameRect=rect; Points=points; Entity=Some(list); Id=rectId}) ->
            if frameObj.Entity.Count >= options.MinFramesToStay then
                let r = frameObj.RectAndPoints.Rectangle
                let color = colors.[frameObj.Entity.Id % colors.Length]
                if frameObj.RectAndPoints.IsNative then
                    if options.DisplayOriginalRects then
                        CvInvoke.Rectangle(displayFrame, r, Bgr(color).MCvScalar, 2)
                else  
                    if options.DisplayInterpolatedRects then
                        CvInvoke.Rectangle(displayFrame, r, Bgr(color).MCvScalar, 5)
                if options.DisplayPoints then
                    for point in frameObj.RectAndPoints.Points do
                        CvInvoke.Rectangle(displayFrame, Rectangle(point, Size(2,2)), Bgr(Color.Red).MCvScalar, 1)
                if options.DisplayIds then
                    CvInvoke.PutText(displayFrame, frameObj.Entity.Id.ToString(), Point(r.Left, r.Bottom + 15), CvEnum.FontFace.HersheyPlain, 1.0, Bgr(color).MCvScalar)
        )


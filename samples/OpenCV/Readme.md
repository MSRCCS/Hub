# Open CV Sample

This project demonstrates how to use EmguCV, a .Net binding for OpenCV, from an F# script.

We use a private NuGet package of EmguCV 3.1, since the official package only supports 3.0. 
EmguCV 3.1 not only contains the latest OpenCV 3.1 packages, it is also easier to use because it 
bundles all OpenCV functionality in only 2 DLLs (vs. 8 or 10 in 3.0).

The .fsx file contains currently demonstrates two pieces of OpenCV functionality: face detection, and sparse optical flow.

To run the demos, simply send the entire file to F# Interactive and then call testOpticalFlow() or testFaces().

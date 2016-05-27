# Open CV Sample

This project demonstrates how to use EmguCV, a .Net binding for OpenCV, from an F# script.

We use a private NuGet package of EmguCV 3.1, since the official package only supports 3.0. 
EmguCV 3.1 not only contains the latest OpenCV 3.1 packages, it is also easier to use because it 
bundles all OpenCV functionality in only 2 DLLs (vs. 8 or 10 in 3.0). 

The OpenCVTest.fsx demonstrates two pieces of OpenCV functionality: face detection, and sparse optical flow. Simply send the entire file to F# Interactive and then call testOpticalFlow() or testFaces() to see the demos.

The remaining files implement a simple face tracking algorithm, using bounding in one case and sparse optical flow (Lukas-Kanade) in the other. To see that demo, just send the entire FaceTracking.fsx file to F# Interactive. Different options of that demo can be customized in the last few lines of FaceTracking.fsx.

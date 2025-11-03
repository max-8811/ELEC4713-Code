using Microsoft.Kinect;
using System;
using System.Globalization;

namespace WpfApplication1.Helpers
{
    public static class KinectHelpers
    {
        /// <summary>
        /// Gets the best estimate of a joint's position:
        /// - If Tracked: returns tracked position.
        /// - If Inferred: returns inferred position.
        /// - If NotTracked: estimates using depth/body index data if available.
        /// </summary>
        public static CameraSpacePoint TryGetEstimatedJointPosition(
        Joint joint,
        CoordinateMapper coordinateMapper,
        ushort[] depthData,
        byte[] bodyIndexData,
        int width, int height,
        int bodyIndex,
        Joint referenceJoint // Used to look around this area if occluded
        )
{
// 1. Use Tracked joint if available
if (joint.TrackingState == TrackingState.Tracked)
return joint.Position;


        // 2. Use Inferred joint if available
        if (joint.TrackingState == TrackingState.Inferred)
            return joint.Position;

        // 3. Estimate using depth/body index as fallback
        if (depthData == null || bodyIndexData == null)
            return joint.Position; // last resort, return whatever is there

        // Project the reference joint to depth space
        DepthSpacePoint referenceDepthPoint = coordinateMapper.MapCameraPointToDepthSpace(referenceJoint.Position);

        int refX = (int)Math.Round(referenceDepthPoint.X);
        int refY = (int)Math.Round(referenceDepthPoint.Y);

        int searchRadius = 10; // pixels; adjust as needed (was 15)

        // Search for a pixel with the matching body index near the reference
        for (int r = 1; r <= searchRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = refX + dx;
                    int y = refY + dy;
                    if (x < 0 || x >= width || y < 0 || y >= height)
                        continue;
                    int idx = y * width + x;
                    if (bodyIndexData[idx] == bodyIndex && depthData[idx] != 0)
                    {
                        // Found a valid pixel for this body
                        CameraSpacePoint p = coordinateMapper.MapDepthPointToCameraSpace(
                            new DepthSpacePoint { X = x, Y = y }, depthData[idx]);
                        return p;
                    }
                }
            }
        }

        // If all else fails, return the joint's current position
        return joint.Position;
    }

        /// <summary>
        /// Calculates the angle (in degrees) at jointB between (jointA-jointB-jointC).
        /// If a joint is not tracked, will attempt to estimate it using inferred state or depth/body index.
        /// </summary>
        public static double CalculateJointAngle(
            Joint jointA, Joint jointB, Joint jointC,
            CoordinateMapper coordinateMapper,
            ushort[] depthData, byte[] bodyIndexData,
            int width, int height, int bodyIndex)
        {
            // Try to get best-available positions for each joint
            CameraSpacePoint posA = TryGetEstimatedJointPosition(jointA, coordinateMapper, depthData, bodyIndexData, width, height, bodyIndex, jointB);
            CameraSpacePoint posB = TryGetEstimatedJointPosition(jointB, coordinateMapper, depthData, bodyIndexData, width, height, bodyIndex, jointB);
            CameraSpacePoint posC = TryGetEstimatedJointPosition(jointC, coordinateMapper, depthData, bodyIndexData, width, height, bodyIndex, jointB);

            return CalculateJointAngle(posA, posB, posC);
        }

        /// <summary>
        /// Calculates the angle (in degrees) at jointB between (jointA-jointB-jointC) from CameraSpacePoints.
        /// </summary>
        public static double CalculateJointAngle(CameraSpacePoint jointA, CameraSpacePoint jointB, CameraSpacePoint jointC)
        {
            // Vector from B to A
            double baX = jointA.X - jointB.X;
            double baY = jointA.Y - jointB.Y;
            double baZ = jointA.Z - jointB.Z;
            // Vector from B to C
            double bcX = jointC.X - jointB.X;
            double bcY = jointC.Y - jointB.Y;
            double bcZ = jointC.Z - jointB.Z;

            // Dot product and magnitudes
            double dot = baX * bcX + baY * bcY + baZ * bcZ;
            double magBA = Math.Sqrt(baX * baX + baY * baY + baZ * baZ);
            double magBC = Math.Sqrt(bcX * bcX + bcY * bcY + bcZ * bcZ);

            if (magBA == 0 || magBC == 0)
                return 0;

            double cosAngle = dot / (magBA * magBC);
            cosAngle = Math.Max(-1, Math.Min(1, cosAngle)); // Clamp for safety

            double angleRad = Math.Acos(cosAngle);
            return angleRad * (180.0 / Math.PI); // Convert to degrees
        }

        public static string ApiArrayFormatter(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                v = 0.0;

            return v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string ApiArrayFormatter(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
                v = 0f;

            return v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        //to add more...
    }
}
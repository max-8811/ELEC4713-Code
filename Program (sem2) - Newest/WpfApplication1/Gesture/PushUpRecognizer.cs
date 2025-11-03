using Microsoft.Kinect;
using WpfApplication1.Helpers;
using System;

namespace WpfApplication1.Gesture
{
    public class PushUpRecognizer : IGestureRecognizer
    {
        private PushUpPhase phase = PushUpPhase.Idle;
        private int pushUpCount = 0;
        private readonly float downThreshold = 0.04f; // Can be adjusted
        private readonly float upThreshold = 0.13f; // Can be adjusted

        private double leftElbowAngle = 0;
        private double rightElbowAngle = 0;
        private string feedback = "Start your push-ups!";

        public int PushUpCount { get { return pushUpCount; } }
        public double LeftElbowAngle { get { return leftElbowAngle; } }
        public double RightElbowAngle { get { return rightElbowAngle; } }
        public string Feedback { get { return feedback; } }

        public enum PushUpPhase
        {
            Idle,
            GoingDown,
            Bottom,
            GoingUp
        }

        /// <summary>
        /// Updated Update method: must be called with additional Kinect data.
        /// </summary>
        public void Update(
            Body body,
            CoordinateMapper coordinateMapper,
            ushort[] depthData,
            byte[] bodyIndexData,
            int depthWidth,
            int depthHeight,
            int bodyIndex
        )
        {
            if (body == null || !body.IsTracked)
                return;

            var joints = body.Joints;

            // Use joint objects instead of .Position
            Joint shoulderLeft = joints[JointType.ShoulderLeft];
            Joint elbowLeft = joints[JointType.ElbowLeft];
            Joint wristLeft = joints[JointType.WristLeft];
            Joint shoulderRight = joints[JointType.ShoulderRight];
            Joint elbowRight = joints[JointType.ElbowRight];
            Joint wristRight = joints[JointType.WristRight];

            Joint head = joints[JointType.Head];
            Joint spineBase = joints[JointType.SpineBase];
            Joint ankleLeft = joints[JointType.AnkleLeft];
            Joint ankleRight = joints[JointType.AnkleRight];
            Joint spineMid = joints[JointType.SpineMid];

            // Use the new helper to calculate angles, which will estimate occluded joints
            leftElbowAngle = KinectHelpers.CalculateJointAngle(
                shoulderLeft, elbowLeft, wristLeft,
                coordinateMapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex);

            rightElbowAngle = KinectHelpers.CalculateJointAngle(
                shoulderRight, elbowRight, wristRight,
                coordinateMapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex);

            double leftAlignmentAngle = KinectHelpers.CalculateJointAngle(
                head, spineBase, ankleLeft,
                coordinateMapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex);

            double rightAlignmentAngle = KinectHelpers.CalculateJointAngle(
                head, spineBase, ankleRight,
                coordinateMapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex);

            // Estimate spine mid position robustly
            CameraSpacePoint spine = KinectHelpers.TryGetEstimatedJointPosition(
                spineMid, coordinateMapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex, spineBase);

            bool isStraight = leftAlignmentAngle > 155 && rightAlignmentAngle > 155;

            // Arm checks
            bool armsBent = leftElbowAngle < 90 && rightElbowAngle < 90;
            bool armsExtended = leftElbowAngle > 155 && rightElbowAngle > 155;

            string phaseFeedback = "";

            // PHASED LOGIC
            switch (phase)
            {
                case PushUpPhase.Idle:
                    if (armsExtended && isStraight && spine.Y > upThreshold)
                    {
                        phase = PushUpPhase.GoingDown;
                        phaseFeedback = "Lower yourself down!";
                    }
                    break;

                case PushUpPhase.GoingDown:
                    if (!isStraight)
                    {
                        phaseFeedback = "Keep your body straight!";
                    }
                    else if (armsBent && spine.Y < downThreshold)
                    {
                        phase = PushUpPhase.Bottom;
                        phaseFeedback = "Now push up!";
                    }
                    else
                    {
                        phaseFeedback = "Lower yourself down!";
                    }
                    break;

                case PushUpPhase.Bottom:
                    if (!isStraight)
                    {
                        phaseFeedback = "Keep your body straight at the bottom!";
                    }
                    else if (armsBent && spine.Y >= downThreshold)
                    {
                        phase = PushUpPhase.GoingUp;
                        phaseFeedback = "Push up to the top!";
                    }
                    break;

                case PushUpPhase.GoingUp:
                    if (!isStraight)
                    {
                        phaseFeedback = "Keep your body straight!";
                    }
                    else if (armsExtended && spine.Y > upThreshold)
                    {
                        pushUpCount++;
                        phaseFeedback = string.Format("Great! Push-ups: {0}", pushUpCount);
                        phase = PushUpPhase.Idle; // Ready for next rep
                    }
                    else
                    {
                        phaseFeedback = "Push up to the top!";
                    }
                    break;
            }

            feedback = string.Format(
                "{0}\nLeftElbow: {1:F1}°, RightElbow: {2:F1}°, LeftAlign: {3:F1}°, RightAlign: {4:F1}°",
                phaseFeedback, leftElbowAngle, rightElbowAngle, leftAlignmentAngle, rightAlignmentAngle
            );
        }
    }
}
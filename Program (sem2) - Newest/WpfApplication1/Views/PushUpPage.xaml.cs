using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging; // Needed for WriteableBitmap
using System.Windows.Shapes;
using Microsoft.Kinect;
using WpfApplication1.Gesture;

namespace WpfApplication1.Views
{
    public partial class PushUpPage : Page
    {
        private PushUpRecognizer recognizer;
        private CoordinateMapper coordinateMapper; // Store for mapping
        private int colorWidth = 1920;   // Kinect v2 color frame width
        private int colorHeight = 1080;  // Kinect v2 color frame height

        public PushUpPage(Frame mainFrame)
        {
            InitializeComponent();
            recognizer = new PushUpRecognizer();
            FeedbackText.Text = recognizer.Feedback;
            PushUpCountText.Text = "Push-Ups: " + recognizer.PushUpCount;

            // Set canvas size to match color frame
            SkeletonCanvas.Width = colorWidth;
            SkeletonCanvas.Height = colorHeight;
        }

        // ***** ADD THIS METHOD *****
        public void SetCameraImage(WriteableBitmap bitmap)
        {
            CameraImage.Source = bitmap;
        }
        // ****************************

        // Accepts all relevant Kinect data, including coordinateMapper
        public void UpdateSkeletonAndFeedback(
            Body body,
            CoordinateMapper coordinateMapper,
            ushort[] depthData,
            byte[] bodyIndexData,
            int depthWidth,
            int depthHeight,
            int bodyIndex)
        {
            this.coordinateMapper = coordinateMapper; // Save for mapping

            recognizer.Update(
                body,
                coordinateMapper,
                depthData,
                bodyIndexData,
                depthWidth,
                depthHeight,
                bodyIndex
            );
            FeedbackText.Text = recognizer.Feedback;
            PushUpCountText.Text = "Push-Ups: " + recognizer.PushUpCount;

            if (body != null && body.IsTracked)
                DrawSkeleton(body);
            else
                SkeletonCanvas.Children.Clear();
        }

        private void DrawSkeleton(Body body)
        {
            SkeletonCanvas.Children.Clear();

            var joints = body.Joints;
            var bones = new[]
            {
                new[] { JointType.Head, JointType.Neck },
                new[] { JointType.Neck, JointType.SpineShoulder },
                new[] { JointType.SpineShoulder, JointType.SpineMid },
                new[] { JointType.SpineMid, JointType.SpineBase },
                new[] { JointType.ShoulderLeft, JointType.ElbowLeft },
                new[] { JointType.ElbowLeft, JointType.WristLeft },
                new[] { JointType.WristLeft, JointType.HandLeft },
                new[] { JointType.ShoulderRight, JointType.ElbowRight },
                new[] { JointType.ElbowRight, JointType.WristRight },
                new[] { JointType.WristRight, JointType.HandRight },
                new[] { JointType.HipLeft, JointType.KneeLeft },
                new[] { JointType.KneeLeft, JointType.AnkleLeft },
                new[] { JointType.AnkleLeft, JointType.FootLeft },
                new[] { JointType.HipRight, JointType.KneeRight },
                new[] { JointType.KneeRight, JointType.AnkleRight },
                new[] { JointType.AnkleRight, JointType.FootRight },
                new[] { JointType.ShoulderLeft, JointType.SpineShoulder },
                new[] { JointType.ShoulderRight, JointType.SpineShoulder },
                new[] { JointType.HipLeft, JointType.SpineBase },
                new[] { JointType.HipRight, JointType.SpineBase }
            };

            foreach (var bone in bones)
            {
                DrawBone(joints[bone[0]], joints[bone[1]]);
            }

            foreach (var joint in joints.Values)
            {
                DrawJoint(joint);
            }
        }

        // Maps the 3D CameraSpacePoint to color image 2D coordinates
        private Point SkeletonPointToColorSpace(CameraSpacePoint point)
        {
            if (coordinateMapper == null)
                return new Point(0, 0);

            ColorSpacePoint colorPoint = coordinateMapper.MapCameraPointToColorSpace(point);

            // Guard against invalid mapping (infinity/NaN)
            if (float.IsInfinity(colorPoint.X) || float.IsInfinity(colorPoint.Y) ||
                float.IsNaN(colorPoint.X) || float.IsNaN(colorPoint.Y))
            {
                return new Point(0, 0);
            }

            return new Point(colorPoint.X, colorPoint.Y);
        }

        private void DrawBone(Joint joint1, Joint joint2)
        {
            if (joint1.TrackingState == TrackingState.NotTracked || joint2.TrackingState == TrackingState.NotTracked)
                return;

            var p1 = SkeletonPointToColorSpace(joint1.Position);
            var p2 = SkeletonPointToColorSpace(joint2.Position);

            // Only draw if both points are within the image bounds
            if (p1.X < 0 || p1.X > colorWidth || p1.Y < 0 || p1.Y > colorHeight ||
                p2.X < 0 || p2.X > colorWidth || p2.Y < 0 || p2.Y > colorHeight)
                return;

            var line = new Line
            {
                X1 = p1.X,
                Y1 = p1.Y,
                X2 = p2.X,
                Y2 = p2.Y,
                Stroke = Brushes.Lime,
                StrokeThickness = 4
            };
            SkeletonCanvas.Children.Add(line);
        }

        private void DrawJoint(Joint joint)
        {
            if (joint.TrackingState == TrackingState.NotTracked)
                return;

            var pos = SkeletonPointToColorSpace(joint.Position);
            if (pos.X < 0 || pos.X > colorWidth || pos.Y < 0 || pos.Y > colorHeight)
                return;

            var ellipse = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = Brushes.Red
            };
            Canvas.SetLeft(ellipse, pos.X - 6);
            Canvas.SetTop(ellipse, pos.Y - 6);
            SkeletonCanvas.Children.Add(ellipse);
        }
    }
}
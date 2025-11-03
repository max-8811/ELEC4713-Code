using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Kinect;
using WpfApplication1.Helpers;

namespace WpfApplication1.Views
{
    public class ChatItem
    {
        private string _text;
        private string _timestamp;
        private Brush _bubbleColor;

        public ChatItem()
        {
            _text = string.Empty;
            _timestamp = string.Empty;
            _bubbleColor = new SolidColorBrush(Color.FromRgb(235, 248, 255));
        }

        public string Text { get { return _text; } set { _text = value; } }
        public string Timestamp { get { return _timestamp; } set { _timestamp = value; } }
        public Brush BubbleColor { get { return _bubbleColor; } set { _bubbleColor = value; } }
    }

    public partial class SquatPage : Page
    {
        private Gesture.SquatRecognizer recognizer;
        private CoordinateMapper coordinateMapper;

        private readonly int colorWidth = 1920;
        private readonly int colorHeight = 1080;

        private readonly ObservableCollection<ChatItem> _messages = new ObservableCollection<ChatItem>();

        public SquatPage(Frame mainFrame)
        {
            InitializeComponent();

            ChatItems.ItemsSource = _messages;

            ChatInput.Loaded += delegate { ApplyWatermark(); };
            ChatInput.GotFocus += delegate { ClearWatermark(); };
            ChatInput.LostFocus += delegate { ApplyWatermark(); };

            recognizer = new Gesture.SquatRecognizer(
                delegate(string headline)
                {
                    if (!Dispatcher.CheckAccess())
                        Dispatcher.Invoke(new Action(delegate { UpdateBanner(headline); }));
                    else
                        UpdateBanner(headline);
                },
                delegate(string block)
                {
                    if (!Dispatcher.CheckAccess())
                        Dispatcher.Invoke(new Action(delegate { AppendLlmBubble(block); }));
                    else
                        AppendLlmBubble(block);
                });

            UpdateBanner("Ready for squats!");
            SquatCountText.Text = recognizer.SquatCount.ToString();
            SetPauseUI(false, "");
        }

        private void ApplyWatermark()
        {
            if (string.IsNullOrEmpty(ChatInput.Text))
            {
                object tag = ChatInput.Tag;
                ChatInput.Text = tag == null ? string.Empty : tag.ToString();
                ChatInput.Foreground = Brushes.Gray;
            }
        }

        private void ClearWatermark()
        {
            if (ChatInput.Foreground == Brushes.Gray)
            {
                ChatInput.Text = string.Empty;
                ChatInput.Foreground = Brushes.Black;
            }
        }

        private void UpdateBanner(string headline)
        {
            headline = headline ?? string.Empty;

            bool paused = false;
            try
            {
                paused = (recognizer != null && (recognizer.IsPaused ||
                        headline.StartsWith("Paused:", StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                paused = headline.StartsWith("Paused:", StringComparison.OrdinalIgnoreCase);
            }

            SetPauseUI(paused, paused ? headline : string.Empty);

            if (CoachHeadline != null)
            {
                CoachHeadline.Text = headline;

                string lower = headline.ToLowerInvariant();
                if (lower.Contains("great") || lower.Contains("good") || lower.Contains("nice"))
                    CoachHeadline.Foreground = new SolidColorBrush(Color.FromRgb(24, 121, 78));
                else if (lower.Contains("too") || lower.Contains("avoid") || lower.Contains("fix") || lower.Contains("don"))
                    CoachHeadline.Foreground = new SolidColorBrush(Color.FromRgb(183, 71, 42));
                else
                    CoachHeadline.Foreground = Brushes.Black;
            }

            if (recognizer != null)
                SquatCountText.Text = recognizer.SquatCount.ToString();
        }

        private void AppendLlmBubble(string llmText)
        {
            if (string.IsNullOrWhiteSpace(llmText)) return;

            Brush bubble = new SolidColorBrush(Color.FromRgb(235, 248, 255));
            string l = llmText.ToLowerInvariant();
            if (l.Contains("great") || l.Contains("good") || l.Contains("nice"))
                bubble = new SolidColorBrush(Color.FromRgb(232, 247, 238));
            else if (l.Contains("too") || l.Contains("avoid") || l.Contains("fix") || l.Contains("don"))
                bubble = new SolidColorBrush(Color.FromRgb(255, 243, 227));

            AppendMessage(llmText, bubble);

            if (recognizer != null)
                SquatCountText.Text = recognizer.SquatCount.ToString();
        }

        private void SetPauseUI(bool show, string reasonLine)
        {
            if (PausePanel != null)
            {
                PausePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
            if (PauseText != null)
            {
                string text = !string.IsNullOrEmpty(reasonLine)
                    ? reasonLine
                    : "Paused due to tracking issues. Tap Continue to resume.";
                PauseText.Text = show ? text : string.Empty;
            }
        }

        private void AppendMessage(string text, Brush bubble)
        {
            bool isDuplicate = false;
            if (_messages.Count > 0)
                isDuplicate = _messages[_messages.Count - 1].Text == text;

            if (!isDuplicate)
            {
                ChatItem item = new ChatItem();
                item.Text = text ?? string.Empty;
                item.Timestamp = DateTime.Now.ToString("HH:mm:ss");
                if (bubble != null) item.BubbleColor = bubble;

                _messages.Add(item);
            }

            Dispatcher.BeginInvoke(new Action(delegate { ChatScroll.ScrollToEnd(); }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        public void SetCameraImage(WriteableBitmap bitmap)
        {
            CameraImage.Source = bitmap;
        }

        public void UpdateSkeletonAndFeedback(
            Body body,
            CoordinateMapper coordinateMapper,
            ushort[] depthData,
            byte[] bodyIndexData,
            int depthWidth,
            int depthHeight,
            int bodyIndex)
        {
            this.coordinateMapper = coordinateMapper;

            recognizer.Update(
                body,
                coordinateMapper,
                depthData,
                bodyIndexData,
                depthWidth,
                depthHeight,
                bodyIndex
            );

            if (PhaseText != null)
                PhaseText.Text = "Phase: " + (recognizer != null ? recognizer.PhaseName : "n/a");
            if (HipDropText != null)
                HipDropText.Text = "Hip drop: " + (recognizer != null ? Math.Round(recognizer.CurrentHipDropCm) : 0).ToString() + " cm";

            if (recognizer != null)
                SquatCountText.Text = recognizer.SquatCount.ToString();

            if (body != null && body.IsTracked && !recognizer.IsPaused)
                DrawSkeleton(body, coordinateMapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex);
            else
                SkeletonCanvas.Children.Clear();
        }

        private enum JointVisualSource { Tracked, Inferred, Estimated }

        private void DrawSkeleton(
            Body body,
            CoordinateMapper mapper,
            ushort[] depthData,
            byte[] bodyIndexData,
            int depthWidth,
            int depthHeight,
            int bodyIndex)
        {
            SkeletonCanvas.Children.Clear();

            var joints = body.Joints;

            JointType[][] bones = new JointType[][]
            {
                new JointType[] { JointType.Head, JointType.Neck },
                new JointType[] { JointType.Neck, JointType.SpineShoulder },
                new JointType[] { JointType.SpineShoulder, JointType.SpineMid },
                new JointType[] { JointType.SpineMid, JointType.SpineBase },
                new JointType[] { JointType.ShoulderLeft, JointType.ElbowLeft },
                new JointType[] { JointType.ElbowLeft, JointType.WristLeft },
                new JointType[] { JointType.WristLeft, JointType.HandLeft },
                new JointType[] { JointType.ShoulderRight, JointType.ElbowRight },
                new JointType[] { JointType.ElbowRight, JointType.WristRight },
                new JointType[] { JointType.WristRight, JointType.HandRight },
                new JointType[] { JointType.HipLeft, JointType.KneeLeft },
                new JointType[] { JointType.KneeLeft, JointType.AnkleLeft },
                new JointType[] { JointType.AnkleLeft, JointType.FootLeft },
                new JointType[] { JointType.HipRight, JointType.KneeRight },
                new JointType[] { JointType.KneeRight, JointType.AnkleRight },
                new JointType[] { JointType.AnkleRight, JointType.FootRight },
                new JointType[] { JointType.ShoulderLeft, JointType.SpineShoulder },
                new JointType[] { JointType.ShoulderRight, JointType.SpineShoulder },
                new JointType[] { JointType.HipLeft, JointType.SpineBase },
                new JointType[] { JointType.HipRight, JointType.SpineBase }
            };

            for (int i = 0; i < bones.Length; i++)
            {
                CameraSpacePoint aPos, bPos;
                Point aPoint, bPoint;
                JointVisualSource aSrc, bSrc;

                if (!TryGetJointDisplayInfo(body, bones[i][0], mapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex,
                                            out aPos, out aPoint, out aSrc))
                    continue;

                if (!TryGetJointDisplayInfo(body, bones[i][1], mapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex,
                                            out bPos, out bPoint, out bSrc))
                    continue;

                Line line = new Line();
                line.X1 = aPoint.X;
                line.Y1 = aPoint.Y;
                line.X2 = bPoint.X;
                line.Y2 = bPoint.Y;

                // If either endpoint is orange (Inferred or Estimated) -> bone orange, else lime
                if (aSrc == JointVisualSource.Tracked && bSrc == JointVisualSource.Tracked)
                    line.Stroke = Brushes.Lime;
                else
                    line.Stroke = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // orange

                line.StrokeThickness = 4;
                SkeletonCanvas.Children.Add(line);
            }

            // Joints
            foreach (Joint j in joints.Values)
            {
                CameraSpacePoint pos;
                Point p;
                JointVisualSource src;
                if (!TryGetJointDisplayInfo(body, j.JointType, mapper, depthData, bodyIndexData, depthWidth, depthHeight, bodyIndex,
                                            out pos, out p, out src))
                    continue;

                Brush fill = FillForSource(src);

                double dotSize = 18.0; // bigger dots
                Ellipse dot = new Ellipse();
                dot.Width = dotSize;
                dot.Height = dotSize;
                dot.Fill = fill;
                dot.Stroke = Brushes.Black;
                dot.StrokeThickness = 2;
                Canvas.SetLeft(dot, p.X - dotSize / 2.0);
                Canvas.SetTop(dot, p.Y - dotSize / 2.0);
                SkeletonCanvas.Children.Add(dot);
            }
        }

        // Joint colors:
        // - Tracked = Dark Green (#006400)
        // - Inferred = Orange
        // - Estimated = Orange
        private Brush FillForSource(JointVisualSource s)
        {
            if (s == JointVisualSource.Tracked) return new SolidColorBrush(Color.FromRgb(0x00, 0x64, 0x00)); // dark green
            return new SolidColorBrush(Color.FromRgb(255, 165, 0)); // orange for both inferred and estimated
        }

        private bool TryGetJointDisplayInfo(
            Body body,
            JointType jt,
            CoordinateMapper mapper,
            ushort[] depthData,
            byte[] bodyIndexData,
            int width, int height,
            int bodyIndex,
            out CameraSpacePoint cameraPos,
            out Point colorPos,
            out JointVisualSource visualSrc)
        {
            cameraPos = new CameraSpacePoint();
            colorPos = new Point(0, 0);
            visualSrc = JointVisualSource.Inferred;

            if (body == null) return false;

            Joint j = body.Joints[jt];

            if (j.TrackingState == TrackingState.Tracked)
            {
                visualSrc = JointVisualSource.Tracked;
                cameraPos = j.Position;
            }
            else if (j.TrackingState == TrackingState.Inferred)
            {
                visualSrc = JointVisualSource.Inferred; // orange
                cameraPos = j.Position;
            }
            else
            {
                Joint reference = PickReferenceJoint(body, jt);
                CameraSpacePoint estimated = KinectHelpers.TryGetEstimatedJointPosition(
                    j, mapper, depthData, bodyIndexData, width, height, bodyIndex, reference);

                visualSrc = JointVisualSource.Estimated; // also orange
                cameraPos = estimated;
            }

            colorPos = SkeletonPointToColorSpace(cameraPos);
            if (colorPos.X < 0 || colorPos.X > colorWidth || colorPos.Y < 0 || colorPos.Y > colorHeight)
                return false;

            return true;
        }

        private Joint PickReferenceJoint(Body body, JointType jt)
        {
            switch (jt)
            {
                case JointType.KneeLeft:
                case JointType.AnkleLeft:
                case JointType.FootLeft:
                    return body.Joints[JointType.HipLeft];

                case JointType.KneeRight:
                case JointType.AnkleRight:
                case JointType.FootRight:
                    return body.Joints[JointType.HipRight];

                case JointType.WristLeft:
                case JointType.HandLeft:
                    return body.Joints[JointType.ElbowLeft];

                case JointType.WristRight:
                case JointType.HandRight:
                    return body.Joints[JointType.ElbowRight];

                case JointType.ElbowLeft:
                    return body.Joints[JointType.ShoulderLeft];

                case JointType.ElbowRight:
                    return body.Joints[JointType.ShoulderRight];

                case JointType.Head:
                case JointType.Neck:
                case JointType.SpineShoulder:
                case JointType.SpineMid:
                    return body.Joints[JointType.SpineBase];

                default:
                    return body.Joints[JointType.SpineBase];
            }
        }

        private Point SkeletonPointToColorSpace(CameraSpacePoint point)
        {
            if (coordinateMapper == null)
                return new Point(0, 0);

            ColorSpacePoint colorPoint = coordinateMapper.MapCameraPointToColorSpace(point);

            if (float.IsInfinity(colorPoint.X) || float.IsInfinity(colorPoint.Y) ||
                float.IsNaN(colorPoint.X) || float.IsNaN(colorPoint.Y))
                return new Point(0, 0);

            return new Point(colorPoint.X, colorPoint.Y);
        }

        private void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ChatInput.Foreground == Brushes.Gray)
                return;

            string text = ChatInput.Text == null ? string.Empty : ChatInput.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            AppendMessage(text, null);
            ChatInput.Clear();
            ApplyWatermark();
        }

        private void ContinueBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                recognizer.ContinueAfterPause();
                SetPauseUI(false, "");
            }
            catch
            {
            }
        }
    }
}
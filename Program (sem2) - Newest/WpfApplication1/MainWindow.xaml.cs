using Microsoft.Kinect;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using WpfApplication1.Views;

namespace WpfApplication1
{
    public partial class MainWindow : Window
    {
        private KinectSensor kinectSensor;
        private BodyFrameReader bodyFrameReader;
        private DepthFrameReader depthFrameReader;
        private BodyIndexFrameReader bodyIndexFrameReader;
        private ColorFrameReader colorFrameReader;

        private Body[] bodies;
        private ushort[] depthData;
        private byte[] bodyIndexData;
        private int depthWidth;
        private int depthHeight;

        private WriteableBitmap colorBitmap;
        private FrameDescription colorFrameDescription;

        public MainWindow()
        {
            InitializeComponent();

            kinectSensor = KinectSensor.GetDefault();
            kinectSensor.Open();

            bodyFrameReader = kinectSensor.BodyFrameSource.OpenReader();
            bodyFrameReader.FrameArrived += BodyFrameReader_FrameArrived;

            depthFrameReader = kinectSensor.DepthFrameSource.OpenReader();
            bodyIndexFrameReader = kinectSensor.BodyIndexFrameSource.OpenReader();

            // --- Color frame setup ---
            colorFrameReader = kinectSensor.ColorFrameSource.OpenReader();
            colorFrameReader.FrameArrived += ColorFrameReader_FrameArrived;

            colorFrameDescription = kinectSensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Bgra);
            colorBitmap = new WriteableBitmap(
                colorFrameDescription.Width, colorFrameDescription.Height, 96.0, 96.0, System.Windows.Media.PixelFormats.Bgra32, null);

            // When you navigate to PushUpPage, keep a reference to it:
            //pushUpPage = new PushUpPage(mainFrame);
            //mainFrame.Navigate(pushUpPage);
            mainFrame.Navigate(new MenuPage(mainFrame));
        }

        private void ColorFrameReader_FrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null) return;

                using (KinectBuffer buffer = frame.LockRawImageBuffer())
                {
                    colorBitmap.Lock();
                    frame.CopyConvertedFrameDataToIntPtr(
                        colorBitmap.BackBuffer,
                        (uint)(colorFrameDescription.Width * colorFrameDescription.Height * 4),
                        ColorImageFormat.Bgra);

                    colorBitmap.AddDirtyRect(new Int32Rect(0, 0, colorBitmap.PixelWidth, colorBitmap.PixelHeight));
                    colorBitmap.Unlock();
                }

                // Set camera image for PushUpPage or SquatPage
                var pushUpPage = mainFrame.Content as PushUpPage;
                if (pushUpPage != null)
                {
                    pushUpPage.SetCameraImage(colorBitmap);
                }
                var squatPage = mainFrame.Content as SquatPage;
                if (squatPage != null)
                {
                    squatPage.SetCameraImage(colorBitmap);
                }
            }
        }

        private void BodyFrameReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            using (var frame = e.FrameReference.AcquireFrame())
            {
                if (frame == null) return;

                if (bodies == null)
                    bodies = new Body[frame.BodyCount];

                frame.GetAndRefreshBodyData(bodies);

                // Handle PushUpPage
                var pushUpPage = mainFrame.Content as PushUpPage;
                if (pushUpPage != null)
                {
                    var body = bodies.FirstOrDefault(b => b.IsTracked);
                    if (body != null)
                    {
                        // --- Get the latest depth and body index data ---
                        using (var depthFrame = depthFrameReader.AcquireLatestFrame())
                        {
                            if (depthFrame != null)
                            {
                                if (depthData == null)
                                {
                                    depthWidth = depthFrame.FrameDescription.Width;
                                    depthHeight = depthFrame.FrameDescription.Height;
                                    depthData = new ushort[depthWidth * depthHeight];
                                }
                                depthFrame.CopyFrameDataToArray(depthData);
                            }
                        }

                        using (var bodyIndexFrame = bodyIndexFrameReader.AcquireLatestFrame())
                        {
                            if (bodyIndexFrame != null)
                            {
                                if (bodyIndexData == null)
                                {
                                    bodyIndexData = new byte[depthWidth * depthHeight];
                                }
                                bodyIndexFrame.CopyFrameDataToArray(bodyIndexData);
                            }
                        }

                        int bodyIndex = 0;

                        pushUpPage.UpdateSkeletonAndFeedback(
                            body,
                            kinectSensor.CoordinateMapper,
                            depthData,
                            bodyIndexData,
                            depthWidth,
                            depthHeight,
                            bodyIndex
                        );
                    }
                }

                // Handle SquatPage
                var squatPage = mainFrame.Content as SquatPage;
                if (squatPage != null)
                {
                    var body = bodies.FirstOrDefault(b => b.IsTracked);
                    if (body != null)
                    {
                        // --- Get the latest depth and body index data ---
                        using (var depthFrame = depthFrameReader.AcquireLatestFrame())
                        {
                            if (depthFrame != null)
                            {
                                if (depthData == null)
                                {
                                    depthWidth = depthFrame.FrameDescription.Width;
                                    depthHeight = depthFrame.FrameDescription.Height;
                                    depthData = new ushort[depthWidth * depthHeight];
                                }
                                depthFrame.CopyFrameDataToArray(depthData);
                            }
                        }

                        using (var bodyIndexFrame = bodyIndexFrameReader.AcquireLatestFrame())
                        {
                            if (bodyIndexFrame != null)
                            {
                                if (bodyIndexData == null)
                                {
                                    bodyIndexData = new byte[depthWidth * depthHeight];
                                }
                                bodyIndexFrame.CopyFrameDataToArray(bodyIndexData);
                            }
                        }

                        int bodyIndex = 0;

                        squatPage.UpdateSkeletonAndFeedback(
                            body,
                            kinectSensor.CoordinateMapper,
                            depthData,
                            bodyIndexData,
                            depthWidth,
                            depthHeight,
                            bodyIndex
                        );
                    }
                }
                // else if (mainFrame.Content is SitUpPage sitUpPage)
                // { update sit-up... }
            }
        }
    }
}
using Microsoft.Kinect;

namespace WpfApplication1.Gesture
{
    public interface IGestureRecognizer
    {
        void Update(
            Body body,
            CoordinateMapper coordinateMapper,
            ushort[] depthData,
            byte[] bodyIndexData,
            int depthWidth,
            int depthHeight,
            int bodyIndex
        );

        string Feedback { get; }
    }
}
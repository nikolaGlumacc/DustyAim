using System;

namespace AimmyWPF
{
    internal class PredictionManager
    {
        public struct Detection
        {
            public int X;
            public int Y;
            public DateTime Timestamp;
        }

        private Detection prevDetection;
        private double prevVelocityX;
        private double prevVelocityY;
        private double prevAccelerationX;
        private double prevAccelerationY;
        private bool isInitialized;
        private double maxDistance = 400.0;
        private double predictionInterval = 0.035; // 35ms ahead

        public double PredictionStrength { get; set; } = 1.0;

        public PredictionManager()
        {
            Reset();
        }

        public void Reset()
        {
            isInitialized = false;
            prevVelocityX = 0;
            prevVelocityY = 0;
            prevAccelerationX = 0;
            prevAccelerationY = 0;
        }

        public void UpdateKalmanFilter(Detection detection)
        {
            // Method name kept for backward compatibility, but now uses Kinematic Physics
            DateTime currentTime = detection.Timestamp == default ? DateTime.UtcNow : detection.Timestamp;
            detection.Timestamp = currentTime;

            if (!isInitialized)
            {
                prevDetection = detection;
                isInitialized = true;
                return;
            }

            double deltaSeconds = (currentTime - prevDetection.Timestamp).TotalSeconds;
            if (deltaSeconds <= 0) deltaSeconds = 0.001;

            double distance = Math.Sqrt(Math.Pow(detection.X - prevDetection.X, 2) + Math.Pow(detection.Y - prevDetection.Y, 2));

            // Reset if target snaps too far or too much time elapses
            if (deltaSeconds > 0.3 || distance > maxDistance)
            {
                Reset();
                prevDetection = detection;
                isInitialized = true;
                return;
            }

            double velocityX = (detection.X - prevDetection.X) / deltaSeconds;
            double velocityY = (detection.Y - prevDetection.Y) / deltaSeconds;

            double accelerationX = (velocityX - prevVelocityX) / deltaSeconds;
            double accelerationY = (velocityY - prevVelocityY) / deltaSeconds;

            // Apply slight smoothing
            double alpha = 0.7;
            prevVelocityX = (alpha * velocityX) + ((1 - alpha) * prevVelocityX);
            prevVelocityY = (alpha * velocityY) + ((1 - alpha) * prevVelocityY);
            prevAccelerationX = (alpha * accelerationX) + ((1 - alpha) * prevAccelerationX);
            prevAccelerationY = (alpha * accelerationY) + ((1 - alpha) * prevAccelerationY);

            prevDetection = detection;
        }

        public Detection GetEstimatedPosition()
        {
            if (!isInitialized)
            {
                return new Detection { X = 0, Y = 0, Timestamp = DateTime.UtcNow };
            }

            double leadScale = Math.Clamp(PredictionStrength, 0.0, 1.0);
            if (leadScale <= 0.0) return prevDetection;

            double lookahead = predictionInterval * leadScale;

            // Kinematic equation: P = P0 + v*t + 0.5*a*t^2
            double predictedX = prevDetection.X + (prevVelocityX * lookahead) + (0.5 * prevAccelerationX * Math.Pow(lookahead, 2));
            double predictedY = prevDetection.Y + (prevVelocityY * lookahead) + (0.5 * prevAccelerationY * Math.Pow(lookahead, 2));

            return new Detection
            {
                X = (int)Math.Round(predictedX),
                Y = (int)Math.Round(predictedY),
                Timestamp = DateTime.UtcNow
            };
        }
    }
}

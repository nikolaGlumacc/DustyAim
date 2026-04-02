using Accord.Statistics.Running;
using System;

namespace AimmyWPF
{
    internal class PredictionManager
    {
        private const double DefaultFrameSeconds = 1.0 / 60.0;
        private const double MinSampleGapSeconds = 1.0 / 240.0;
        private const double MaxSampleGapSeconds = 0.25;
        private const double PredictionFramesAhead = 2.35;
        private const double MaxPredictionSeconds = 0.09;
        private const double MinVelocityForLead = 18.0;
        private const double RetargetResetDistance = 140.0;

        public struct Detection
        {
            public int X;
            public int Y;
            public DateTime Timestamp;
        }

        private KalmanFilter2D kalmanFilter;
        private DateTime lastUpdateTime;
        private Detection lastDetection;
        private bool hasLastDetection;
        private bool hasVelocitySample;
        private double smoothedVelocityX;
        private double smoothedVelocityY;
        private double averageFrameSeconds;

        public double PredictionStrength { get; set; } = 1.0;

        public PredictionManager()
        {
            Reset();
        }

        public void Reset()
        {
            kalmanFilter = new KalmanFilter2D();
            lastUpdateTime = DateTime.MinValue;
            lastDetection = default;
            hasLastDetection = false;
            hasVelocitySample = false;
            smoothedVelocityX = 0.0;
            smoothedVelocityY = 0.0;
            averageFrameSeconds = DefaultFrameSeconds;
        }

        public void UpdateKalmanFilter(Detection detection)
        {
            DateTime currentTime = detection.Timestamp == default ? DateTime.UtcNow : detection.Timestamp;
            detection.Timestamp = currentTime;

            if (hasLastDetection)
            {
                double deltaSeconds = (currentTime - lastDetection.Timestamp).TotalSeconds;
                double deltaX = detection.X - lastDetection.X;
                double deltaY = detection.Y - lastDetection.Y;
                double jumpDistance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));

                if (deltaSeconds > MaxSampleGapSeconds || jumpDistance > RetargetResetDistance)
                {
                    Reset();
                }
                else if (deltaSeconds >= MinSampleGapSeconds)
                {
                    double rawVelocityX = deltaX / deltaSeconds;
                    double rawVelocityY = deltaY / deltaSeconds;

                    if (!hasVelocitySample)
                    {
                        smoothedVelocityX = rawVelocityX;
                        smoothedVelocityY = rawVelocityY;
                        averageFrameSeconds = deltaSeconds;
                        hasVelocitySample = true;
                    }
                    else
                    {
                        double blend = Math.Clamp(deltaSeconds * 14.0, 0.18, 0.60);
                        smoothedVelocityX += (rawVelocityX - smoothedVelocityX) * blend;
                        smoothedVelocityY += (rawVelocityY - smoothedVelocityY) * blend;
                        averageFrameSeconds = (averageFrameSeconds * 0.72) + (deltaSeconds * 0.28);
                    }
                }
            }

            kalmanFilter.Push(detection.X, detection.Y);
            lastUpdateTime = currentTime;
            lastDetection = detection;
            hasLastDetection = true;
        }

        public Detection GetEstimatedPosition()
        {
            double currentX = kalmanFilter.X;
            double currentY = kalmanFilter.Y;
            double leadScale = Math.Clamp(PredictionStrength, 0.0, 1.0);

            if (leadScale <= 0.0)
            {
                return new Detection
                {
                    X = (int)Math.Round(currentX),
                    Y = (int)Math.Round(currentY),
                    Timestamp = DateTime.UtcNow
                };
            }

            double kalmanVelocityX = kalmanFilter.XAxisVelocity;
            double kalmanVelocityY = kalmanFilter.YAxisVelocity;

            double velocityX = hasVelocitySample
                ? (kalmanVelocityX * 0.60) + (smoothedVelocityX * 0.40)
                : kalmanVelocityX;
            double velocityY = hasVelocitySample
                ? (kalmanVelocityY * 0.60) + (smoothedVelocityY * 0.40)
                : kalmanVelocityY;

            double speed = Math.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
            if (speed < MinVelocityForLead)
            {
                return new Detection
                {
                    X = (int)Math.Round(currentX),
                    Y = (int)Math.Round(currentY),
                    Timestamp = DateTime.UtcNow
                };
            }

            double predictionSeconds = Math.Clamp(
                averageFrameSeconds * PredictionFramesAhead,
                DefaultFrameSeconds,
                MaxPredictionSeconds);

            if (lastUpdateTime != DateTime.MinValue)
            {
                double elapsedSinceUpdate = Math.Clamp(
                    (DateTime.UtcNow - lastUpdateTime).TotalSeconds,
                    0.0,
                    MaxPredictionSeconds);
                predictionSeconds = Math.Clamp(
                    predictionSeconds + elapsedSinceUpdate,
                    DefaultFrameSeconds,
                    MaxPredictionSeconds);
            }

            double predictedX = currentX + (velocityX * predictionSeconds * leadScale);
            double predictedY = currentY + (velocityY * predictionSeconds * leadScale);

            return new Detection
            {
                X = (int)Math.Round(predictedX),
                Y = (int)Math.Round(predictedY),
                Timestamp = DateTime.UtcNow
            };
        }
    }
}

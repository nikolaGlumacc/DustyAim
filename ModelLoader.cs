using System;

namespace AimmyAimbot
{
    public static class ModelLoader
    {
        public static AIModel Create(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("Model path cannot be empty.", nameof(modelPath));

            return new AIModel(modelPath);
        }
    }
}

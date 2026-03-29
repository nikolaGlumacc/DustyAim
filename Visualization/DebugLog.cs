using System;
using System.IO;

namespace Visualization
{
    public static class DebugLog
    {
        private static readonly object Sync = new();
        private static string _pendingMessage = string.Empty;
        private static string _pendingKey = string.Empty;
        private static string _pendingTimestamp = string.Empty;
        private static int _pendingCount = 0;
        private static bool _hasPending = false;

        public static string LogFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dusty_debug.log");

        public static void Write(string message)
        {
            string normalizedMessage = NormalizeMessage(message);
            if (normalizedMessage.Length == 0)
                return;

            lock (Sync)
            {
                string messageKey = GetMessageKey(normalizedMessage);

                if (!_hasPending)
                {
                    StartPending(normalizedMessage, messageKey);
                    return;
                }

                if (string.Equals(messageKey, _pendingKey, StringComparison.Ordinal))
                {
                    _pendingMessage = normalizedMessage;
                    _pendingCount++;
                    return;
                }

                FlushPendingLocked();
                StartPending(normalizedMessage, messageKey);
            }
        }

        public static void Flush()
        {
            lock (Sync)
            {
                FlushPendingLocked();
            }
        }

        private static void StartPending(string message, string key)
        {
            _pendingMessage = message;
            _pendingKey = key;
            _pendingTimestamp = DateTime.Now.ToString("HH:mm:ss");
            _pendingCount = 1;
            _hasPending = true;
        }

        private static void FlushPendingLocked()
        {
            if (!_hasPending)
                return;

            try
            {
                File.AppendAllText(LogFilePath, BuildLine(_pendingTimestamp, _pendingMessage, _pendingCount) + Environment.NewLine);
            }
            catch
            {
            }

            _hasPending = false;
            _pendingMessage = string.Empty;
            _pendingKey = string.Empty;
            _pendingTimestamp = string.Empty;
            _pendingCount = 0;
        }

        private static string BuildLine(string timestamp, string message, int count)
        {
            if (count > 1)
                return $"[{timestamp}] {message} X{count}";

            return $"[{timestamp}] {message}";
        }

        private static string NormalizeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return string.Empty;

            return message.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static string GetMessageKey(string message)
        {
            int colonIndex = message.IndexOf(':');
            if (colonIndex > 0)
                return message.Substring(0, colonIndex).TrimEnd();

            return message;
        }
    }
}

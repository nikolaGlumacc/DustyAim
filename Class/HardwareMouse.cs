using System;
using System.IO.Ports;
using System.Threading;
using AimmyWPF.Class;

namespace AimmyWPF.Class
{
    public static class HardwareMouse
    {
        private static SerialPort _serialPort;
        private static readonly object _lock = new object();

        public static bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public static void Initialize(string portName)
        {
            lock (_lock)
            {
                if (IsConnected) Dispose();

                try
                {
                    _serialPort = new SerialPort(portName, 115200)
                    {
                        ReadTimeout = 500,
                        WriteTimeout = 500
                    };
                    _serialPort.Open();
                    Console.WriteLine($"[HardwareMouse] Connected to {portName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HardwareMouse] Failed to connect to {portName}: {ex.Message}");
                    _serialPort = null;
                }
            }
        }

        public static void Move(int dx, int dy)
        {
            if (!IsConnected) return;

            try
            {
                // Protocol: M:dx,dy\n
                // dx,dy are bounded by signed byte (-128 to 127) for most Arduino scripts
                // We chunk it if larger
                while (dx != 0 || dy != 0)
                {
                    int stepX = Math.Clamp(dx, -127, 127);
                    int stepY = Math.Clamp(dy, -127, 127);
                    _serialPort.WriteLine($"M:{stepX},{stepY}");
                    dx -= stepX;
                    dy -= stepY;
                }
            }
            catch { Dispose(); }
        }

        public static void PressLeft() => SendCommand("L");
        public static void ReleaseLeft() => SendCommand("l");
        public static void PressRight() => SendCommand("R");
        public static void ReleaseRight() => SendCommand("r");

        public static bool TestConnection()
        {
            return TestConnection(out _);
        }

        public static bool TestConnection(out string message)
        {
            string portName = Bools.ArduinoComPort;
            try
            {
                if (IsConnected && string.Equals(_serialPort.PortName, portName, StringComparison.OrdinalIgnoreCase))
                {
                    message = $"Already connected to {portName}.";
                    return true;
                }

                using var testPort = new SerialPort(portName, 115200)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                testPort.Open();
                testPort.Close();
                message = $"Successfully connected to {portName}.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to connect to {portName}: {ex.Message}";
                return false;
            }
        }

        private static void SendCommand(string command)
        {
            if (!IsConnected) return;
            try { _serialPort.WriteLine(command); }
            catch { Dispose(); }
        }

        public static void Dispose()
        {
            lock (_lock)
            {
                if (_serialPort != null)
                {
                    try { _serialPort.Close(); _serialPort.Dispose(); } catch { }
                    _serialPort = null;
                }
            }
        }
    }
}

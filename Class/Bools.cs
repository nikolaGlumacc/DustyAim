namespace AimmyWPF.Class
{
    internal class Bools
    {
        public static bool AIAimAligner = false;
        public static bool ConstantTracking = false;
        public static bool AimOnlyWhenBindingHeld = false;

        public static bool AIAlwaysOn = false;
        public static bool AIPredictions = false;

        public static bool ShowFOV = false;
        public static bool TravellingFOV = false;

        public static bool ShowDetectedPlayerWindow = false;
        public static bool ShowCurrentDetectedPlayer = false;
        public static bool ShowUnfilteredDetectedPlayer = false;
        public static bool ShowPrediction = false;

        public static bool ThirdPersonAim = false;
        public static bool Triggerbot = false;
        public static bool CollectDataWhilePlaying = false;
        public static bool TopMost = false;
        public static bool RecoilControl = false;
        public static bool RecoilAdsOnly = false;
        public static bool RecoilRapidFire = false;

        public static bool UseHardwareMouse = false;
        public static string ArduinoComPort = "COM3";
        public static string AimHoldMode = "Hold";
        public static bool ShowMiniHud = false;
        public static int ActiveBindingSlot = 0;
        public static string[] AimBindingSlots = new[] { "Right", "None", "None" };
    }
}

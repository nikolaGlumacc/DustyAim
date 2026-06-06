using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SecondaryWindows
{
    /// <summary>
    /// Interaction logic for ConfigSaver.xaml
    /// </summary>
    public partial class ConfigSaver : Window
    {
        public Dictionary<string, dynamic> aimmySettings = new Dictionary<string, dynamic>();
        public Dictionary<string, bool> toggleState = new Dictionary<string, bool>();
        private static readonly string ConfigDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "configs");

        private string ExtraStrings = string.Empty;

        public ConfigSaver(Dictionary<string, dynamic> CurrentAimmySettings, string lastLoadedModel, Dictionary<string, bool> currentToggleState = null)
        {
            InitializeComponent();
            aimmySettings = CurrentAimmySettings;
            toggleState = currentToggleState ?? new Dictionary<string, bool>();

            if (lastLoadedModel != "N/A")
            {
                RecommendedModelNameTextBox.Text = lastLoadedModel.Split(".")[0];
            }
        }

        private void WriteJSON()
        {
            if (!TryGetConfigName(out string configName))
                return;

            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var extendedSettings = new Dictionary<string, object>();
                foreach (var kvp in aimmySettings)
                {
                    if (kvp.Key == "Suggested_Model")
                    {
                        if (RecommendedModelNameTextBox.Text != string.Empty)
                            extendedSettings[kvp.Key] = RecommendedModelNameTextBox.Text + ".onnx" + ExtraStrings;
                        else
                            extendedSettings[kvp.Key] = "";
                    }
                    else
                        extendedSettings[kvp.Key] = kvp.Value;
                }

                extendedSettings["ToggleState"] = toggleState;

                // Add topmost
                extendedSettings["TopMost"] = toggleState.TryGetValue("TopMost", out bool topMostState) && topMostState;

                string json = JsonConvert.SerializeObject(extendedSettings, Formatting.Indented);
                File.WriteAllText(Path.Combine(ConfigDirectory, $"{configName}.json"), json);
            }
            catch (Exception x)
            {
                Console.WriteLine("Error saving configuration: " + x.Message);
            }

            new NoticeBar("Config has been saved to bin/configs.").Show();

            this.Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetConfigName(out string configName))
                return;

            string targetPath = Path.Combine(ConfigDirectory, $"{configName}.json");
            if (File.Exists(targetPath))
            {
                if (MessageBox.Show("A config already exists with the same name, would you like to overwrite it?",
                    "Aimmy - Configuration Saver", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    WriteJSON();
            }
            else
                WriteJSON();
        }

        private bool TryGetConfigName(out string configName)
        {
            configName = ConfigNameTextbox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(configName))
            {
                MessageBox.Show("Please enter a config name before saving.", "Aimmy - Configuration Saver");
                return false;
            }

            if (configName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("Config name contains invalid filename characters.", "Aimmy - Configuration Saver");
                return false;
            }

            return true;
        }

        private void DownloadableModelCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ExtraStrings = " (Found in Downloadable Model menu)";
            Storyboard Animation = (Storyboard)TryFindResource("EnableSwitch");
            Animation.Begin();
        }

        private void DownloadableModelCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ExtraStrings = "";
            Storyboard Animation = (Storyboard)TryFindResource("DisableSwitch");
            Animation.Begin();
        }

        #region Window Controls

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        #endregion Window Controls
    }
}

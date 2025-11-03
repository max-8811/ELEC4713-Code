using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using WpfApplication1.Helpers;

namespace WpfApplication1.Views
{
    public partial class MenuPage : Page
    {
        private Frame _mainFrame;

        public MenuPage(Frame mainFrame)
        {
            InitializeComponent();
            _mainFrame = mainFrame;
            CoachPersonalityCombo.SelectedIndex = 0;
            LanguageCombo.SelectedIndex = 0; // Set default language selection
        }

        private void Start_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (GestureCombo.SelectedIndex == 0)
                _mainFrame.Navigate(new PushUpPage(_mainFrame));

            else if (GestureCombo.SelectedIndex == 2)
                _mainFrame.Navigate(new SquatPage(_mainFrame));
        }

        private void GestureCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Placeholder for future logic
        }

        private void CoachPersonalityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CoachPersonalityCombo == null) return;

            if (CoachPersonalityCombo.SelectedIndex == 0)
            {
                AppSettings.SelectedCoachModel = "newsum3bmcoach"; // Default
            }
            else if (CoachPersonalityCombo.SelectedIndex == 1)
            {
                AppSettings.SelectedCoachModel = "friendly3bmcoach"; // Friendly
            }
            else if (CoachPersonalityCombo.SelectedIndex == 2)
            {
                AppSettings.SelectedCoachModel = "strict3bmcoach"; // Strict
            }
        }

        // ADD THIS NEW EVENT HANDLER
        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageCombo == null) return;

            if (LanguageCombo.SelectedIndex == 0)
            {
                AppSettings.SelectedLanguage = "English";
            }
            else if (LanguageCombo.SelectedIndex == 1)
            {
                AppSettings.SelectedLanguage = "Chinese";
            }
            else if (LanguageCombo.SelectedIndex == 2)
            {
                AppSettings.SelectedLanguage = "Spanish";
            }
        }

        private void TestModel_Click(object sender, RoutedEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result = null;
                try
                {
                    var client = new WpfApplication1.OllamaClient();
                    result = client.TestCall();
                }
                catch (Exception ex)
                {
                    result = "Error: " + ex.Message;
                }

                Dispatcher.Invoke((Action)delegate
                {
                    MessageBox.Show(result ?? "No response", "Ollama Test");
                });
            });
        }
    }
}
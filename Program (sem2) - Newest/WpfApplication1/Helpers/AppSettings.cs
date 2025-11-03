namespace WpfApplication1.Helpers
{
    /// <summary>
    /// Holds global application settings that can be accessed from any part of the code.
    /// </summary>
    public static class AppSettings
    {
        /// <summary>
        /// Stores the name of the Ollama model to be used for coaching feedback.
        /// </summary>
        public static string SelectedCoachModel { get; set; }

        /// <summary>
        /// Stores the desired output language for the coaching feedback.
        /// </summary>
        public static string SelectedLanguage { get; set; }

        // Static constructor to initialize default values.
        static AppSettings()
        {
            SelectedCoachModel = "newsum3bmcoach";
            SelectedLanguage = "English";
        }
    }
}
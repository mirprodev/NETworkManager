using System;

namespace NETworkManager.Models.EventSystem
{
    public class EventSystem
    {
        // This will notify the mail window, to change the view to another application and redirect some data (hostname, ip)
        public static event EventHandler RedirectDataToApplicationEvent;

        public static void RedirectToApplication(ApplicationName application, string data)
        {
            RedirectDataToApplicationEvent?.Invoke(null, new EventSystemRedirectArgs(application, data));
        }

        // This will notify the main window, to change the view to the settings...
        public static event EventHandler RedirectToSettingsEvent;

        public static void RedirectToSettings()
        {
            RedirectToSettingsEvent?.Invoke(null, EventArgs.Empty);
        }
    }
}
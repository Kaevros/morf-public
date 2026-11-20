using System;

namespace Widgates
{
    // App opening only. Notification names, text, phone-like strings and URLs never enter these targets.
    internal static class WhatsAppNotificationActivation
    {
        internal static bool Open(string source, Func<string, bool> registeredOpen, Func<string, bool> packagedOpen)
        {
            if (!AppMessage.IsWhatsApp(source)) return false;
            if (EqualsId(source, AppMessage.WhatsAppBetaId) || EqualsId(source, AppMessage.WhatsAppCurrentBetaId))
            {
                string other = EqualsId(source, AppMessage.WhatsAppBetaId) ? AppMessage.WhatsAppCurrentBetaId : AppMessage.WhatsAppBetaId;
                return OpenPackage(source, registeredOpen, packagedOpen) || OpenPackage(other, registeredOpen, packagedOpen);
            }
            if (!EqualsId(source, AppMessage.WhatsAppId) && registeredOpen(source)) return true;
            return OpenPackage(AppMessage.WhatsAppId, registeredOpen, packagedOpen);
        }
        private static bool OpenPackage(string id, Func<string, bool> registeredOpen, Func<string, bool> packagedOpen)
        {
            return registeredOpen(id) || packagedOpen(id);
        }
        private static bool EqualsId(string first, string second) { return string.Equals(first, second, StringComparison.OrdinalIgnoreCase); }
    }
}

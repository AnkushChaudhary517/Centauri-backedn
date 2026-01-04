using CentauriSeo.Core.Models.Enums;

namespace CentauriSeo.Core.Models.Utilities
{

    public static class VoiceDetector
    {
        private static readonly string[] PassiveMarkers =
        {
        " was ", " were ", " is ", " are ", " be ", " been "
    };

        public static VoiceType Detect(string s)
        {
            return PassiveMarkers.Any(s.Contains) && s.Contains(" by ")
                ? VoiceType.Passive
                : VoiceType.Active;
        }
    }

}

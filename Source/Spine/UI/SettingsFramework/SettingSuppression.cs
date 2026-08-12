using System;
using Verse;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Declares that a setting is currently inert while another setting or an
    /// external integration owns the feature.
    /// </summary>
    /// <remarks>
    /// This is presentation metadata. The consumer supplies the condition and
    /// explanation; Spine does not attach gameplay meaning to either callback.
    /// </remarks>
    public sealed class SettingSuppression
    {
        /// <summary>Returns true while this suppression applies.</summary>
        public Func<object, bool> When;

        /// <summary>Explains why the setting is inert while the suppression applies.</summary>
        public Func<object, string> Reason;

        /// <summary>
        /// Optional id of the setting responsible for the suppression.
        /// </summary>
        public string SuppressorSettingId;

        /// <summary>Optional caption for the suppressor link.</summary>
        public string LinkLabel;

        /// <summary>Optional external action associated with the suppression.</summary>
        public string ExternalActionUrl;

        /// <summary>Optional caption for <see cref="ExternalActionUrl"/>.</summary>
        public string ExternalActionLabel;

        /// <summary>Optional tooltip for the external action.</summary>
        public string ExternalActionTooltip;

        /// <summary>
        /// Evaluates the condition. A broken consumer predicate is treated as
        /// inactive so it cannot permanently disable a setting.
        /// </summary>
        public bool IsActive(object settingsObject)
        {
            if (When == null)
            {
                return false;
            }

            try
            {
                return When(settingsObject);
            }
            catch (Exception ex)
            {
                Log.Warning("[Spine][Settings] Suppression predicate failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Resolves the explanation, tolerating a broken consumer callback.
        /// </summary>
        public string ResolveReason(object settingsObject)
        {
            if (Reason == null)
            {
                return null;
            }

            try
            {
                return Reason(settingsObject);
            }
            catch (Exception ex)
            {
                Log.Warning("[Spine][Settings] Suppression reason failed: " + ex.Message);
                return null;
            }
        }
    }
}

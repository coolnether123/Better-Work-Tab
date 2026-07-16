using System;
using Verse;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Declares that a setting has no effect while some other setting (or an external mod) overrides it.
    /// A suppressed setting stays visible but is drawn disabled, with an explanation underneath.
    /// </summary>
    /// <remarks>
    /// Set <see cref="SuppressorSettingId"/> to the id of the setting responsible and the explanation
    /// gains a link that jumps to that row, so the player can find and change the cause.
    /// Leave it empty when nothing in this settings window is responsible, such as when an external
    /// mod has taken over a feature outright.
    /// </remarks>
    public sealed class SettingSuppression
    {
        /// <summary>
        /// Required. Returns true while this suppression applies. Receives the settings object.
        /// </summary>
        public Func<object, bool> When;

        /// <summary>
        /// Required. Short sentence explaining why the setting is inert right now.
        /// </summary>
        public Func<object, string> Reason;

        /// <summary>
        /// Optional id of the setting responsible. When it resolves against the hierarchy the
        /// explanation renders a clickable link that focuses that row.
        /// </summary>
        public string SuppressorSettingId;

        /// <summary>
        /// Optional link caption. Defaults to the suppressing setting's own label.
        /// </summary>
        public string LinkLabel;

        /// <summary>
        /// Evaluates <see cref="When"/>, treating a throwing predicate as "not suppressing" so a
        /// broken rule cannot lock a player out of a setting.
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
        /// Resolves <see cref="Reason"/>, tolerating a throwing callback.
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

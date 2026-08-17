using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using UnityEngine;
using Verse;
using Better_Work_Tab.Features.Workloads;
using Better_Work_Tab.Features.Workloads.V2.Runtime;
using Better_Work_Tab.PawnOrganizer.Data;

namespace Better_Work_Tab.Mod_Support.LocalProfiles
{
    internal sealed class BWTLocalProfile : IExposable
    {
        internal const int CurrentProfileSchemaVersion = 1;

        private static readonly HashSet<string> KnownEnvelopeAttributes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Class",
                "IsNull",
                "MayRequire",
                "MayRequireAnyOf",
                "MayRequireAllOf"
            };

        private static readonly HashSet<string> KnownEnvelopeMembers =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "profileSchemaVersion",
                nameof(SaveKey),
                nameof(PlayerKey),
                nameof(Worklists),
                nameof(SelectedWorklistName),
                WorkloadV2PersistenceKeys.Envelope,
                nameof(ActiveDividers),
                nameof(PawnRowOrder),
                "PawnBackgroundColors",
                "PawnTextColors",
                nameof(AllowLayoutRequests),
                nameof(AllowLiveLayoutBroadcast),
                nameof(AllowPresenceBroadcast)
            };

        [NonSerialized]
        private bool _persistenceReadOnly;

        [NonSerialized]
        private string _persistenceDiagnostic = string.Empty;

        // Zero is retained as the missing marker for pre-2.0 MP profiles.
        // Such profiles remain readable and are stamped with the current
        // marker only when a safe save succeeds.
        public int ProfileSchemaVersion = CurrentProfileSchemaVersion;

        public string SaveKey;
        public string PlayerKey;

        public List<Worklist> Worklists = new();
        public string SelectedWorklistName;
        // V2 is a separate local-profile document member. It never shares or
        // migrates the legacy Worklists collection implicitly.
        public WorkloadV2PersistenceEnvelope WorkloadsV2 = WorkloadV2PersistenceEnvelope.CreateEmpty();

        public List<PawnDivider> ActiveDividers = new();

        // thingIDNumber -> display order (local only in MP)
        public Dictionary<int, int> PawnRowOrder = new();

        // pawn label (or a stable key you already use) -> colors
        public Dictionary<string, Color> PawnBackgroundColors = new();
        public Dictionary<string, Color> PawnTextColors = new();

        // privacy toggles (local-only)
        public bool AllowLayoutRequests = true;        // Respond to "send me your layout" requests
        public bool AllowLiveLayoutBroadcast = true;   // Push live updates to followers
        public bool AllowPresenceBroadcast = true;

        internal bool IsPersistenceReadOnly => _persistenceReadOnly;
        internal string PersistenceDiagnostic => _persistenceDiagnostic;

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (_persistenceReadOnly)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrEmpty(_persistenceDiagnostic)
                            ? "The Better Work Tab local profile is read-only for diagnostics."
                            : _persistenceDiagnostic);
                }

                if (ProfileSchemaVersion < 0 || ProfileSchemaVersion > CurrentProfileSchemaVersion)
                {
                    throw new InvalidOperationException(
                        "The Better Work Tab local profile uses an unsupported schema version and cannot be rewritten.");
                }

                if (ProfileSchemaVersion == 0)
                {
                    ProfileSchemaVersion = CurrentProfileSchemaVersion;
                }
            }

            XmlNode profileXml = Scribe.mode == LoadSaveMode.LoadingVars
                ? Scribe.loader?.curXmlParent
                : null;
            bool unknownEnvelopeShape = Scribe.mode == LoadSaveMode.LoadingVars &&
                                        HasUnknownEnvelopeShape(profileXml);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                _persistenceReadOnly = false;
                _persistenceDiagnostic = string.Empty;
                ProfileSchemaVersion = 0;

                if (unknownEnvelopeShape)
                {
                    MarkReadOnly("The Better Work Tab local profile contains fields this build cannot preserve.");
                }
            }

            Scribe_Values.Look(
                ref ProfileSchemaVersion,
                "profileSchemaVersion",
                0);

            if (Scribe.mode == LoadSaveMode.LoadingVars &&
                !_persistenceReadOnly &&
                (ProfileSchemaVersion < 0 || ProfileSchemaVersion > CurrentProfileSchemaVersion))
            {
                MarkReadOnly(ProfileSchemaVersion > CurrentProfileSchemaVersion
                    ? "The Better Work Tab local profile uses a newer schema and cannot be safely rewritten."
                    : "The Better Work Tab local profile uses an unsupported schema version and cannot be safely rewritten.");
            }

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Missing V2 is distinct from a newly-created current envelope;
                // legacy profile members must remain loadable without stamping
                // a modern document into the profile.
                WorkloadsV2 = WorkloadV2PersistenceEnvelope.CreateMissing();
            }

            Scribe_Values.Look(ref SaveKey, nameof(SaveKey));
            Scribe_Values.Look(ref PlayerKey, nameof(PlayerKey));

            Scribe_Collections.Look(ref Worklists, nameof(Worklists), LookMode.Deep);
            Scribe_Values.Look(ref SelectedWorklistName, nameof(SelectedWorklistName));
            if (Scribe.mode != LoadSaveMode.Saving ||
                WorkloadsV2 == null ||
                WorkloadsV2.ShouldPersist)
            {
                Scribe_Deep.Look(ref WorkloadsV2, WorkloadV2PersistenceKeys.Envelope);
            }

            Scribe_Collections.Look(ref ActiveDividers, nameof(ActiveDividers), LookMode.Deep);

            Scribe_Collections.Look(ref PawnRowOrder, nameof(PawnRowOrder), LookMode.Value, LookMode.Value);

            // Serialize colors as hex strings for reliable save/load
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var bgColorStrings = PawnBackgroundColors?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ColorUtility.ToHtmlStringRGBA(kvp.Value)) ?? new Dictionary<string, string>();
                var textColorStrings = PawnTextColors?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ColorUtility.ToHtmlStringRGBA(kvp.Value)) ?? new Dictionary<string, string>();

                Scribe_Collections.Look(ref bgColorStrings, "PawnBackgroundColors", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref textColorStrings, "PawnTextColors", LookMode.Value, LookMode.Value);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Dictionary<string, string> bgColorStrings = null;
                Dictionary<string, string> textColorStrings = null;

                Scribe_Collections.Look(ref bgColorStrings, "PawnBackgroundColors", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref textColorStrings, "PawnTextColors", LookMode.Value, LookMode.Value);

                PawnBackgroundColors = bgColorStrings?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ParseColor(kvp.Value)) ?? new Dictionary<string, Color>();
                PawnTextColors = textColorStrings?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ParseColor(kvp.Value)) ?? new Dictionary<string, Color>();
            }

            Scribe_Values.Look(ref AllowLayoutRequests, nameof(AllowLayoutRequests), true);
            Scribe_Values.Look(ref AllowLiveLayoutBroadcast, nameof(AllowLiveLayoutBroadcast), true);
            Scribe_Values.Look(ref AllowPresenceBroadcast, nameof(AllowPresenceBroadcast), true);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Worklists ??= new List<Worklist>();
                WorkloadsV2 ??= WorkloadV2PersistenceEnvelope.CreateMissing();
                WorkloadsV2.RefreshDiagnostics();
                ActiveDividers ??= new List<PawnDivider>();
                PawnRowOrder ??= new Dictionary<int, int>();
                PawnBackgroundColors ??= new Dictionary<string, Color>();
                PawnTextColors ??= new Dictionary<string, Color>();
            }
        }

        private void MarkReadOnly(string diagnostic)
        {
            _persistenceReadOnly = true;
            _persistenceDiagnostic = diagnostic ?? string.Empty;
        }

        private static bool HasUnknownEnvelopeShape(XmlNode profileXml)
        {
            if (profileXml == null)
            {
                return false;
            }

            if (profileXml.Attributes != null)
            {
                foreach (XmlAttribute attribute in profileXml.Attributes)
                {
                    if (attribute == null || KnownEnvelopeAttributes.Contains(attribute.Name))
                    {
                        continue;
                    }

                    return true;
                }
            }

            foreach (XmlNode child in profileXml.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element && !KnownEnvelopeMembers.Contains(child.Name))
                {
                    return true;
                }
            }

            return false;
        }

        private static Color ParseColor(string hexString)
        {
            if (ColorUtility.TryParseHtmlString("#" + hexString, out Color color))
                return color;
            return Color.white; // fallback
        }
    }
}

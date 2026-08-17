using System;
using System.Collections.Generic;
using System.Xml;
using Verse;

namespace Better_Work_Tab.Features.Workloads.V2.Runtime
{
    public enum WorkloadV2SchemaState
    {
        Missing = 0,
        KnownOld = 1,
        Current = 2,
        Newer = 3,
        Unsupported = 4,
        Opaque = 5
    }

    public static class WorkloadV2SchemaPolicy
    {
        public static WorkloadV2SchemaState Classify(int version)
        {
            if (version == 0) return WorkloadV2SchemaState.Missing;
            if (version < 0) return WorkloadV2SchemaState.Unsupported;
            if (version < WorkloadSchema.CurrentVersion) return WorkloadV2SchemaState.KnownOld;
            if (version > WorkloadSchema.CurrentVersion) return WorkloadV2SchemaState.Newer;
            return WorkloadV2SchemaState.Current;
        }

        public static string Describe(WorkloadV2SchemaState state)
        {
            switch (state)
            {
                case WorkloadV2SchemaState.Missing:
                    return "missing";
                case WorkloadV2SchemaState.KnownOld:
                    return "known-old";
                case WorkloadV2SchemaState.Current:
                    return "current";
                case WorkloadV2SchemaState.Newer:
                    return "newer";
                case WorkloadV2SchemaState.Opaque:
                    return "opaque";
                default:
                    return "unsupported";
            }
        }
    }

    /// <summary>
    /// Scribe names for the modern workload document. These are deliberately
    /// independent from SavedWorklists/CurrentWorklist and from the legacy
    /// Worklist member names.
    /// </summary>
    public static class WorkloadV2PersistenceKeys
    {
        public const string Envelope = "WorkloadsV2";
        public const string SchemaVersion = "workloadsV2SchemaVersion";
        public const string CurrentWorkloadId = "currentWorkloadV2Id";
        public const string Records = "workloadV2Records";
    }

    public sealed class WorkloadV2PersistenceEnvelope : IExposable
    {
        // A deserialized envelope must prove its schema. New runtime stores use
        // CreateEmpty(), which stamps the current version explicitly.
        public int SchemaVersion;
        public string CurrentWorkloadId = string.Empty;
        public List<WorkloadV2PersistenceRecord> Records = new List<WorkloadV2PersistenceRecord>();

        public WorkloadV2SchemaState SchemaState { get; private set; } = WorkloadV2SchemaState.Missing;
        public bool IsReadOnlyDiagnostic { get; private set; }
        public WorkloadDiagnosticCode DiagnosticCode { get; private set; }
        public string Diagnostic { get; private set; }
        public bool HasPersistedDocument { get; private set; }
        public bool HasOpaqueData { get; private set; }
        public bool ShouldPersist => HasPersistedDocument ||
                                     SchemaState != WorkloadV2SchemaState.Missing ||
                                     HasPendingData;

        private bool HasPendingData =>
            (Records != null && Records.Count > 0) ||
            !string.IsNullOrEmpty(CurrentWorkloadId);

        public static WorkloadV2PersistenceEnvelope CreateEmpty()
        {
            var result = new WorkloadV2PersistenceEnvelope
            {
                SchemaVersion = WorkloadSchema.CurrentVersion,
                SchemaState = WorkloadV2SchemaState.Current
            };
            result.RefreshDiagnostics();
            return result;
        }

        public static WorkloadV2PersistenceEnvelope CreateMissing()
        {
            var result = new WorkloadV2PersistenceEnvelope();
            result.RefreshDiagnostics();
            return result;
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Scribe invokes this object only when the deep node exists. A
                // missing node is intentionally left as CreateMissing() so an
                // old legacy save is not silently upgraded by serialization.
                HasPersistedDocument = true;
                HasOpaqueData |= WorkloadV2OpaqueShape.HasUnknownElements(
                    Scribe.loader?.curXmlParent);
            }

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                RefreshDiagnostics();
                if (SchemaState == WorkloadV2SchemaState.Missing && !HasPersistedDocument)
                {
                    if (!HasPendingData)
                    {
                        // A legacy save/profile had no V2 document. Keep it
                        // absent until the player creates or selects modern
                        // workload data; ordinary loading must not stamp a
                        // new schema into old persistence.
                        return;
                    }

                    // The first actual V2 mutation is the explicit migration
                    // boundary for an otherwise missing document.
                    SchemaVersion = WorkloadSchema.CurrentVersion;
                    RefreshDiagnostics();
                }

                if (IsReadOnlyDiagnostic)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrEmpty(Diagnostic)
                            ? "The Workloads V2 document is read-only for diagnostics."
                            : Diagnostic);
                }

                HasPersistedDocument = true;
            }

            Scribe_Values.Look(
                ref SchemaVersion,
                WorkloadV2PersistenceKeys.SchemaVersion,
                0);
            Scribe_Values.Look(
                ref CurrentWorkloadId,
                WorkloadV2PersistenceKeys.CurrentWorkloadId,
                string.Empty);
            Scribe_Collections.Look(
                ref Records,
                WorkloadV2PersistenceKeys.Records,
                LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Records ??= new List<WorkloadV2PersistenceRecord>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeAfterLoad();
            }
        }

        /// <summary>
        /// Normalizes only a schema this build understands. Newer documents are
        /// retained as loaded and marked read-only so a later UI/backend cannot
        /// silently reinterpret or mutate them.
        /// </summary>
        public void NormalizeAfterLoad()
        {
            Records ??= new List<WorkloadV2PersistenceRecord>();
            CurrentWorkloadId ??= string.Empty;

            RefreshDiagnostics();
            if (IsReadOnlyDiagnostic) return;

            Records.Sort(CompareRecords);
            for (int i = 0; i < Records.Count; i++)
            {
                Records[i]?.NormalizeStableState();
            }
        }

        /// <summary>
        /// Re-checks the version and shape without normalizing record contents.
        /// Runtime callers use this before mutation so a malformed or newer
        /// document is fail-closed even if a host skipped PostLoadInit.
        /// </summary>
        public void RefreshDiagnostics()
        {
            IsReadOnlyDiagnostic = false;
            DiagnosticCode = WorkloadDiagnosticCode.None;
            Diagnostic = string.Empty;
            SchemaState = WorkloadV2SchemaPolicy.Classify(SchemaVersion);

            if (SchemaState == WorkloadV2SchemaState.Missing && !HasPersistedDocument)
            {
                Records ??= new List<WorkloadV2PersistenceRecord>();
                CurrentWorkloadId ??= string.Empty;
                return;
            }

            switch (SchemaState)
            {
                case WorkloadV2SchemaState.Missing:
                    MarkReadOnly(
                        WorkloadDiagnosticCode.UnsupportedSchema,
                        "The saved Workloads V2 schema version is missing; an explicit migration is required.");
                    return;
                case WorkloadV2SchemaState.KnownOld:
                    MarkReadOnly(
                        WorkloadDiagnosticCode.UnsupportedSchema,
                        "The saved Workloads V2 schema is older than this build and has no registered migration.");
                    return;
                case WorkloadV2SchemaState.Newer:
                    MarkReadOnly(
                        WorkloadDiagnosticCode.NewerSchema,
                        "The saved Workloads V2 schema is newer than this build.");
                    return;
                case WorkloadV2SchemaState.Unsupported:
                    MarkReadOnly(
                        WorkloadDiagnosticCode.UnsupportedSchema,
                        "The saved Workloads V2 schema version is unsupported.");
                    return;
            }

            Records ??= new List<WorkloadV2PersistenceRecord>();
            CurrentWorkloadId ??= string.Empty;

            var stableIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Records.Count; i++)
            {
                WorkloadV2PersistenceRecord record = Records[i];
                if (record == null)
                {
                    MarkReadOnly(
                        WorkloadDiagnosticCode.InvalidState,
                        "The saved Workloads V2 document contains a missing workload record.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(record.StableId))
                {
                    MarkReadOnly(
                        WorkloadDiagnosticCode.MissingStableId,
                        "The saved Workloads V2 document contains a workload without a stable ID.");
                    return;
                }

                if (!stableIds.Add(record.StableId))
                {
                    MarkReadOnly(
                        WorkloadDiagnosticCode.AmbiguousStableId,
                        "The saved Workloads V2 document contains duplicate workload stable IDs.");
                    return;
                }

                WorkloadV2SchemaState recordState = WorkloadV2SchemaPolicy.Classify(record.SchemaVersion);
                switch (recordState)
                {
                    case WorkloadV2SchemaState.Missing:
                        MarkReadOnly(
                            WorkloadDiagnosticCode.UnsupportedSchema,
                            "At least one V2 workload record has a missing schema version.");
                        return;
                    case WorkloadV2SchemaState.KnownOld:
                        MarkReadOnly(
                            WorkloadDiagnosticCode.UnsupportedSchema,
                            "At least one V2 workload record uses an older schema with no registered migration.");
                        return;
                    case WorkloadV2SchemaState.Newer:
                        MarkReadOnly(
                            WorkloadDiagnosticCode.NewerSchema,
                            "At least one V2 workload record uses a newer schema.");
                        return;
                    case WorkloadV2SchemaState.Unsupported:
                        MarkReadOnly(
                            WorkloadDiagnosticCode.UnsupportedSchema,
                            "At least one V2 workload record uses an unsupported schema version.");
                        return;
                }

                int knownOwnershipBits = (int)WorkloadOwnershipDimensions.All;
                if ((record.OwnershipDimensions & ~knownOwnershipBits) != 0)
                {
                    MarkReadOnly(
                        WorkloadDiagnosticCode.UnsupportedSchema,
                        "At least one V2 workload record uses an unsupported schema or ownership dimension.");
                    return;
                }

                if (record.ScopeMode < (int)WorkloadScopeMode.ExplicitPawnIds ||
                    record.ScopeMode > (int)WorkloadScopeMode.CurrentMapFreeColonists)
                {
                    MarkReadOnly(
                        WorkloadDiagnosticCode.InvalidScopeMode,
                        "At least one V2 workload record uses an unsupported scope mode.");
                    return;
                }

                string duplicateMessage;
                if (HasDuplicateKeys(
                    record.ExplicitPawnIds,
                    value => value,
                    "explicit pawn IDs",
                    out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.ExcludedPawnIds,
                        value => value,
                        "excluded pawn IDs",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.ParentPriorities,
                        value => PairKey(value?.PawnId, value?.WorkTypeDefName),
                        "parent-priority records",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.ManualModes,
                        value => PairKey(value?.PawnId, value?.WorkTypeDefName),
                        "manual-mode records",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.Schedules,
                        value => value?.PawnId,
                        "schedule records",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.SpecificJobOverrides,
                        value => TripleKey(value?.PawnId, value?.WorkTypeDefName, value?.WorkGiverDefName),
                        "specific-job override records",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.SpecificJobOrder,
                        value => TripleKey(value?.PawnId, value?.WorkTypeDefName, value?.WorkGiverDefName),
                        "specific-job order records",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.PresentationSettings,
                        value => value?.Key,
                        "presentation-setting records",
                        out duplicateMessage))
                {
                    MarkReadOnly(WorkloadDiagnosticCode.InvalidState, duplicateMessage);
                    return;
                }

                WorkloadOperationResult<WorkloadTemplate> recordValidation =
                    WorkloadV2RecordConverter.TryToTemplate(record);
                if (!recordValidation.Succeeded)
                {
                    WorkloadDiagnosticCode diagnosticCode = recordValidation.Code == WorkloadDiagnosticCode.None
                        ? WorkloadDiagnosticCode.InvalidState
                        : recordValidation.Code;
                    MarkReadOnly(
                        diagnosticCode,
                        "A saved Workloads V2 record is not safe to rewrite: " + recordValidation.Message);
                    return;
                }
            }

            if (HasOpaqueData)
            {
                SchemaState = WorkloadV2SchemaState.Opaque;
                MarkReadOnly(
                    WorkloadDiagnosticCode.ReadOnlyDiagnostic,
                    "The saved Workloads V2 document contains fields this build cannot preserve.");
            }
        }

        public WorkloadV2PersistenceRecord Find(string stableId)
        {
            if (string.IsNullOrEmpty(stableId) || Records == null)
            {
                return null;
            }

            WorkloadV2PersistenceRecord result = null;
            for (int i = 0; i < Records.Count; i++)
            {
                WorkloadV2PersistenceRecord record = Records[i];
                if (record?.StableId != stableId) continue;
                if (result != null) return null;
                result = record;
            }

            return result;
        }

        public bool HasDuplicateStableId(string stableId)
        {
            if (string.IsNullOrEmpty(stableId) || Records == null) return false;

            int count = 0;
            for (int i = 0; i < Records.Count; i++)
            {
                if (Records[i]?.StableId == stableId) count++;
            }

            return count > 1;
        }

        public bool HasNewerSchema
        {
            get
            {
                if (SchemaVersion > WorkloadSchema.CurrentVersion) return true;
                if (Records == null) return false;
                for (int i = 0; i < Records.Count; i++)
                {
                    if (Records[i]?.SchemaVersion > WorkloadSchema.CurrentVersion) return true;
                }

                return false;
            }
        }

        private void MarkReadOnly(WorkloadDiagnosticCode code, string message)
        {
            IsReadOnlyDiagnostic = true;
            DiagnosticCode = code;
            Diagnostic = message ?? string.Empty;
        }

        private static bool HasDuplicateKeys<T>(
            IList<T> values,
            Func<T, string> keySelector,
            string description,
            out string message)
        {
            message = string.Empty;
            if (values == null || values.Count == 0)
            {
                return false;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                T value = values[i];
                if (ReferenceEquals(value, null))
                {
                    message = "The saved Workloads V2 document contains a missing " + description + ".";
                    return true;
                }

                string key = keySelector == null ? string.Empty : keySelector(value) ?? string.Empty;
                if (!keys.Add(key))
                {
                    message = "The saved Workloads V2 document contains duplicate " + description + ".";
                    return true;
                }
            }

            return false;
        }

        private static string PairKey(string first, string second)
        {
            return EncodeKey(first) + EncodeKey(second);
        }

        private static string TripleKey(string first, string second, string third)
        {
            return EncodeKey(first) + EncodeKey(second) + EncodeKey(third);
        }

        private static string EncodeKey(string value)
        {
            string safe = value ?? string.Empty;
            return safe.Length.ToString() + ":" + safe;
        }

        private static int CompareRecords(WorkloadV2PersistenceRecord left, WorkloadV2PersistenceRecord right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            int id = StringComparer.Ordinal.Compare(left.StableId ?? string.Empty, right.StableId ?? string.Empty);
            return id != 0
                ? id
                : StringComparer.Ordinal.Compare(left.Label ?? string.Empty, right.Label ?? string.Empty);
        }
    }

    /// <summary>
    /// Uses Scribe's current XML node only to detect fields this build cannot
    /// round-trip. It does not deserialize or replace Scribe data. A detected
    /// opaque field makes the envelope read-only, so an atomic caller can keep
    /// the original document intact.
    /// </summary>
    internal static class WorkloadV2OpaqueShape
    {
        // These attributes are emitted by Scribe for deep objects and optional
        // cross-mod requirements. They describe the object shape itself rather
        // than a workload field. Any other attribute is treated like an unknown
        // element: this build cannot prove that it can round-trip it safely.
        private static readonly HashSet<string> KnownAttributeNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Class",
                "IsNull",
                "MayRequire",
                "MayRequireAnyOf",
                "MayRequireAllOf"
            };

        private static readonly HashSet<string> KnownElementNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                WorkloadV2PersistenceKeys.SchemaVersion,
                WorkloadV2PersistenceKeys.CurrentWorkloadId,
                WorkloadV2PersistenceKeys.Records,
                "stableId",
                "label",
                "schemaVersion",
                "ownershipDimensions",
                "scopeMode",
                "explicitPawnIds",
                "excludedPawnIds",
                "parentPriorities",
                "manualModes",
                "schedules",
                "specificJobOverrides",
                "specificJobOrder",
                "presentationSettings",
                "pawnId",
                "workTypeDefName",
                "priority",
                "manual",
                "schedule",
                "workGiverDefName",
                "order",
                "key",
                "value",
                "kind",
                "booleanValue",
                "integerValue",
                "stringValue",
                "li"
            };

        internal static bool HasUnknownElements(XmlNode node)
        {
            if (node == null) return false;

            XmlNode envelope = FindEnvelope(node);
            return envelope != null && HasUnknownElement(envelope);
        }

        private static XmlNode FindEnvelope(XmlNode node)
        {
            if (string.Equals(
                node.Name,
                WorkloadV2PersistenceKeys.Envelope,
                StringComparison.Ordinal))
            {
                return node;
            }

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                XmlNode envelope = FindEnvelope(child);
                if (envelope != null) return envelope;
            }

            return null;
        }

        private static bool HasUnknownElement(XmlNode node)
        {
            if (HasUnknownAttributes(node)) return true;

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (!KnownElementNames.Contains(child.Name)) return true;
                if (HasUnknownElement(child)) return true;
            }

            return false;
        }

        private static bool HasUnknownAttributes(XmlNode node)
        {
            if (node?.Attributes == null) return false;

            foreach (XmlAttribute attribute in node.Attributes)
            {
                if (attribute == null) continue;
                if (!KnownAttributeNames.Contains(attribute.Name)) return true;
            }

            return false;
        }
    }

    public sealed class WorkloadV2PersistenceRecord : IExposable
    {
        public string StableId = string.Empty;
        public string Label = string.Empty;
        // Missing record schema must remain distinguishable from a newly created
        // current record. Converters and the envelope reject it explicitly.
        public int SchemaVersion;
        public int OwnershipDimensions;
        public int ScopeMode;
        public List<string> ExplicitPawnIds = new List<string>();
        public List<string> ExcludedPawnIds = new List<string>();
        public List<WorkloadV2ParentPriorityRecord> ParentPriorities = new List<WorkloadV2ParentPriorityRecord>();
        public List<WorkloadV2ManualModeRecord> ManualModes = new List<WorkloadV2ManualModeRecord>();
        public List<WorkloadV2ScheduleRecord> Schedules = new List<WorkloadV2ScheduleRecord>();
        public List<WorkloadV2SpecificJobOverrideRecord> SpecificJobOverrides = new List<WorkloadV2SpecificJobOverrideRecord>();
        public List<WorkloadV2SpecificJobOrderRecord> SpecificJobOrder = new List<WorkloadV2SpecificJobOrderRecord>();
        public List<WorkloadV2PresentationSettingRecord> PresentationSettings = new List<WorkloadV2PresentationSettingRecord>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref StableId, "stableId", string.Empty);
            Scribe_Values.Look(ref Label, "label", string.Empty);
            Scribe_Values.Look(ref SchemaVersion, "schemaVersion", 0);
            Scribe_Values.Look(ref OwnershipDimensions, "ownershipDimensions", 0);
            Scribe_Values.Look(ref ScopeMode, "scopeMode", 0);
            Scribe_Collections.Look(ref ExplicitPawnIds, "explicitPawnIds", LookMode.Value);
            Scribe_Collections.Look(ref ExcludedPawnIds, "excludedPawnIds", LookMode.Value);
            Scribe_Collections.Look(ref ParentPriorities, "parentPriorities", LookMode.Deep);
            Scribe_Collections.Look(ref ManualModes, "manualModes", LookMode.Deep);
            Scribe_Collections.Look(ref Schedules, "schedules", LookMode.Deep);
            Scribe_Collections.Look(ref SpecificJobOverrides, "specificJobOverrides", LookMode.Deep);
            Scribe_Collections.Look(ref SpecificJobOrder, "specificJobOrder", LookMode.Deep);
            Scribe_Collections.Look(ref PresentationSettings, "presentationSettings", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureCollections();
            }
        }

        public void EnsureCollections()
        {
            ExplicitPawnIds ??= new List<string>();
            ExcludedPawnIds ??= new List<string>();
            ParentPriorities ??= new List<WorkloadV2ParentPriorityRecord>();
            ManualModes ??= new List<WorkloadV2ManualModeRecord>();
            Schedules ??= new List<WorkloadV2ScheduleRecord>();
            SpecificJobOverrides ??= new List<WorkloadV2SpecificJobOverrideRecord>();
            SpecificJobOrder ??= new List<WorkloadV2SpecificJobOrderRecord>();
            PresentationSettings ??= new List<WorkloadV2PresentationSettingRecord>();
        }

        public void NormalizeStableState()
        {
            if (WorkloadV2SchemaPolicy.Classify(SchemaVersion) != WorkloadV2SchemaState.Current)
            {
                return;
            }

            EnsureCollections();
            StableId ??= string.Empty;
            Label ??= string.Empty;

            NormalizeStrings(ExplicitPawnIds);
            NormalizeStrings(ExcludedPawnIds);
            ParentPriorities.Sort(CompareParentPriorities);
            ManualModes.Sort(CompareManualModes);
            Schedules.Sort(CompareSchedules);
            SpecificJobOverrides.Sort(CompareSpecificJobOverrides);
            SpecificJobOrder.Sort(CompareSpecificJobOrders);
            PresentationSettings.Sort(ComparePresentationSettings);
        }

        private static void NormalizeStrings(List<string> values)
        {
            values.Sort(StringComparer.Ordinal);
            for (int i = values.Count - 1; i > 0; i--)
            {
                if (StringComparer.Ordinal.Equals(values[i], values[i - 1])) values.RemoveAt(i);
            }
        }

        private static int CompareParentPriorities(WorkloadV2ParentPriorityRecord left, WorkloadV2ParentPriorityRecord right)
        {
            return StringComparer.Ordinal.Compare(
                ParentKey(left?.PawnId, left?.WorkTypeDefName),
                ParentKey(right?.PawnId, right?.WorkTypeDefName));
        }

        private static int CompareManualModes(WorkloadV2ManualModeRecord left, WorkloadV2ManualModeRecord right)
        {
            return StringComparer.Ordinal.Compare(
                ParentKey(left?.PawnId, left?.WorkTypeDefName),
                ParentKey(right?.PawnId, right?.WorkTypeDefName));
        }

        private static int CompareSchedules(WorkloadV2ScheduleRecord left, WorkloadV2ScheduleRecord right)
        {
            return StringComparer.Ordinal.Compare(left?.PawnId ?? string.Empty, right?.PawnId ?? string.Empty);
        }

        private static int CompareSpecificJobOverrides(WorkloadV2SpecificJobOverrideRecord left, WorkloadV2SpecificJobOverrideRecord right)
        {
            return StringComparer.Ordinal.Compare(SpecificKey(left), SpecificKey(right));
        }

        private static int CompareSpecificJobOrders(WorkloadV2SpecificJobOrderRecord left, WorkloadV2SpecificJobOrderRecord right)
        {
            return StringComparer.Ordinal.Compare(SpecificKey(left), SpecificKey(right));
        }

        private static int ComparePresentationSettings(WorkloadV2PresentationSettingRecord left, WorkloadV2PresentationSettingRecord right)
        {
            return StringComparer.Ordinal.Compare(left?.Key ?? string.Empty, right?.Key ?? string.Empty);
        }

        private static string ParentKey(string pawnId, string workTypeDefName)
        {
            return (pawnId ?? string.Empty) + "\u001f" + (workTypeDefName ?? string.Empty);
        }

        private static string SpecificKey(WorkloadV2SpecificJobOverrideRecord value)
        {
            return (value?.PawnId ?? string.Empty) + "\u001f"
                + (value?.WorkTypeDefName ?? string.Empty) + "\u001f"
                + (value?.WorkGiverDefName ?? string.Empty);
        }

        private static string SpecificKey(WorkloadV2SpecificJobOrderRecord value)
        {
            return (value?.PawnId ?? string.Empty) + "\u001f"
                + (value?.WorkTypeDefName ?? string.Empty) + "\u001f"
                + (value?.WorkGiverDefName ?? string.Empty);
        }
    }

    public sealed class WorkloadV2ParentPriorityRecord : IExposable
    {
        public string PawnId = string.Empty;
        public string WorkTypeDefName = string.Empty;
        public int Priority;

        public void ExposeData()
        {
            Scribe_Values.Look(ref PawnId, "pawnId", string.Empty);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName", string.Empty);
            Scribe_Values.Look(ref Priority, "priority", 0);
        }
    }

    public sealed class WorkloadV2ManualModeRecord : IExposable
    {
        public string PawnId = string.Empty;
        public string WorkTypeDefName = string.Empty;
        public bool Manual;

        public void ExposeData()
        {
            Scribe_Values.Look(ref PawnId, "pawnId", string.Empty);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName", string.Empty);
            Scribe_Values.Look(ref Manual, "manual", false);
        }
    }

    public sealed class WorkloadV2ScheduleRecord : IExposable
    {
        public string PawnId = string.Empty;
        public int Schedule;

        public void ExposeData()
        {
            Scribe_Values.Look(ref PawnId, "pawnId", string.Empty);
            Scribe_Values.Look(ref Schedule, "schedule", -1);
        }
    }

    public sealed class WorkloadV2SpecificJobOverrideRecord : IExposable
    {
        public string PawnId = string.Empty;
        public string WorkTypeDefName = string.Empty;
        public string WorkGiverDefName = string.Empty;
        public WorkloadV2ScalarRecord Value = new WorkloadV2ScalarRecord();

        public void ExposeData()
        {
            Scribe_Values.Look(ref PawnId, "pawnId", string.Empty);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName", string.Empty);
            Scribe_Values.Look(ref WorkGiverDefName, "workGiverDefName", string.Empty);
            Scribe_Deep.Look(ref Value, "value");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Value ??= new WorkloadV2ScalarRecord();
        }
    }

    public sealed class WorkloadV2SpecificJobOrderRecord : IExposable
    {
        public string PawnId = string.Empty;
        public string WorkTypeDefName = string.Empty;
        public string WorkGiverDefName = string.Empty;
        public int Order;

        public void ExposeData()
        {
            Scribe_Values.Look(ref PawnId, "pawnId", string.Empty);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName", string.Empty);
            Scribe_Values.Look(ref WorkGiverDefName, "workGiverDefName", string.Empty);
            Scribe_Values.Look(ref Order, "order", 0);
        }
    }

    public sealed class WorkloadV2PresentationSettingRecord : IExposable
    {
        public string Key = string.Empty;
        public WorkloadV2ScalarRecord Value = new WorkloadV2ScalarRecord();

        public void ExposeData()
        {
            Scribe_Values.Look(ref Key, "key", string.Empty);
            Scribe_Deep.Look(ref Value, "value");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Value ??= new WorkloadV2ScalarRecord();
        }
    }

    public sealed class WorkloadV2ScalarRecord : IExposable
    {
        public int Kind;
        public bool BooleanValue;
        public int IntegerValue;
        public string StringValue = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Kind, "kind", 0);
            Scribe_Values.Look(ref BooleanValue, "booleanValue", false);
            Scribe_Values.Look(ref IntegerValue, "integerValue", 0);
            Scribe_Values.Look(ref StringValue, "stringValue", string.Empty);
        }
    }
}

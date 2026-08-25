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
        public const string PersistenceRevision = "workloadsV2PersistenceRevision";
        public const string PersistenceFingerprint = "workloadsV2PersistenceFingerprint";
    }

    public sealed class WorkloadV2PersistenceEnvelope : IExposable
    {
        // A deserialized envelope must prove its schema. New runtime stores use
        // CreateEmpty(), which stamps the current version explicitly.
        public int SchemaVersion;
        public int PersistenceRevision;
        public string PersistenceFingerprint = string.Empty;
        public string CurrentWorkloadId = string.Empty;
        public List<WorkloadV2PersistenceRecord> Records = new List<WorkloadV2PersistenceRecord>();

        public WorkloadV2SchemaState SchemaState { get; private set; } = WorkloadV2SchemaState.Missing;
        public bool IsReadOnlyDiagnostic { get; private set; }
        public WorkloadDiagnosticCode DiagnosticCode { get; private set; }
        public string Diagnostic { get; private set; }
        public bool HasPersistedDocument { get; private set; }
        public bool HasOpaqueData { get; private set; }
        private bool _diagnosticsCurrent;
        private long _diagnosticsRevision;

        internal long DiagnosticsRevision => _diagnosticsRevision;
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
                PersistenceRevision = 0,
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
                ref PersistenceRevision,
                WorkloadV2PersistenceKeys.PersistenceRevision,
                0);
            Scribe_Values.Look(
                ref PersistenceFingerprint,
                WorkloadV2PersistenceKeys.PersistenceFingerprint,
                string.Empty);
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

            string migrationError;
            if (!WorkloadV2Migration.TryMigrateEnvelope(this, out migrationError))
            {
                MarkReadOnly(
                    WorkloadDiagnosticCode.ReadOnlyDiagnostic,
                    migrationError);
                return;
            }

            RefreshDiagnostics();
            if (IsReadOnlyDiagnostic) return;

            Records.Sort(CompareRecords);
            for (int i = 0; i < Records.Count; i++)
            {
                Records[i]?.NormalizeStableState();
            }

            RefreshPersistenceMetadata(false);
        }

        /// <summary>
        /// Re-checks the version and shape without normalizing record contents.
        /// Runtime callers use this before mutation so a malformed or newer
        /// document is fail-closed even if a host skipped PostLoadInit.
        /// </summary>
        public void RefreshDiagnostics()
        {
            _diagnosticsCurrent = false;
            RefreshDiagnosticsCore();
            _diagnosticsCurrent = true;
            unchecked
            {
                _diagnosticsRevision++;
            }
        }

        internal void EnsureDiagnosticsCurrent()
        {
            if (!_diagnosticsCurrent)
            {
                RefreshDiagnostics();
            }
        }

        private void RefreshDiagnosticsCore()
        {
            IsReadOnlyDiagnostic = false;
            DiagnosticCode = WorkloadDiagnosticCode.None;
            Diagnostic = string.Empty;
            SchemaState = WorkloadV2SchemaPolicy.Classify(SchemaVersion);

            if (PersistenceRevision < 0)
            {
                MarkReadOnly(
                    WorkloadDiagnosticCode.InvalidState,
                    "The saved Workloads V2 persistence revision is invalid.");
                return;
            }

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
                        "The saved Workloads V2 schema has not completed its required document-load migration.");
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
                            "At least one V2 workload record has not completed required document-load migration.");
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

                // NoOpinion is dictionary absence. Normalize this typed
                // boundary before duplicate validation so stale serialized
                // NoOpinion nodes cannot make a valid target dirty or collide
                // with its real Set/Clear record.
                record.NormalizeStableState();

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

                if (HasDuplicateKeys(
                        record.ParentPriorityIntents,
                        value => PairKey(value?.PawnId, value?.WorkTypeDefName),
                        "typed parent-priority intent records",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.ManualModeIntents,
                        value => PairKey(value?.PawnId, value?.WorkTypeDefName),
                        "typed manual-mode intent records",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.ScheduleIntents,
                        value => ScheduleIntentKey(value),
                        "typed schedule intent records",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.SpecificPriorityIntents,
                        value => SpecificPriorityIntentKey(value),
                        "typed specific-priority intent records",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.WorkTypeOrderIntents,
                        value => WorkTypeOrderIntentKey(value),
                        "typed WorkType order intent records",
                        out duplicateMessage) ||
                    HasDuplicateKeys(
                        record.PresentationSettingIntents,
                        value => value?.Key,
                        "typed presentation-setting intent records",
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
                        "A saved Workloads V2 record is not safe to rewrite. Code=" + recordValidation.Code + ".");
                    return;
                }

                if (record.LegacyScheduleRequiresReview || record.LegacyOrderRequiresReview)
                {
                    SchemaState = WorkloadV2SchemaState.Opaque;
                    MarkReadOnly(
                        WorkloadDiagnosticCode.ReadOnlyDiagnostic,
                        string.IsNullOrWhiteSpace(record.MigrationDiagnostic)
                            ? "The workload contains legacy schedule or order data that cannot be losslessly represented by the typed schema."
                            : record.MigrationDiagnostic);
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

        /// <summary>
        /// Monotonic persistence metadata for same-ID Update/Fork callers.
        /// The fingerprint covers workload content, not the revision fields.
        /// </summary>
        public string ComputeContentFingerprint()
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(WorkloadCanonical.Integer(SchemaVersion));
            builder.Append(WorkloadCanonical.Encode(CurrentWorkloadId));
            var records = new List<WorkloadV2PersistenceRecord>(Records ?? new List<WorkloadV2PersistenceRecord>());
            records.Sort(CompareRecords);
            for (int i = 0; i < records.Count; i++)
            {
                builder.Append(WorkloadV2PersistenceCanonical.ForRecord(records[i]));
            }

            return WorkloadCanonical.Fingerprint(builder.ToString());
        }

        public bool HasCasMetadata =>
            PersistenceRevision > 0 &&
            !string.IsNullOrWhiteSpace(PersistenceFingerprint);

        public bool TryValidateCompareAndSwap(
            int expectedRevision,
            string expectedFingerprint,
            out string error)
        {
            error = string.Empty;
            if (expectedRevision < 0)
            {
                error = "The expected workload persistence revision is invalid.";
                return false;
            }

            string actualFingerprint = ComputeContentFingerprint();
            if (expectedRevision != PersistenceRevision)
            {
                error = "The workload persistence revision changed while the preview was open.";
                return false;
            }

            if (!string.IsNullOrEmpty(expectedFingerprint) &&
                !StringComparer.Ordinal.Equals(expectedFingerprint, actualFingerprint))
            {
                error = "The workload persistence fingerprint changed while the preview was open.";
                return false;
            }

            return true;
        }

        public bool TryCommitRevision(
            int expectedRevision,
            string expectedFingerprint,
            out string error)
        {
            if (!TryValidateCompareAndSwap(expectedRevision, expectedFingerprint, out error))
            {
                return false;
            }

            if (expectedRevision == int.MaxValue)
            {
                error = "The workload persistence revision cannot advance further.";
                return false;
            }

            PersistenceRevision = expectedRevision + 1;
            PersistenceFingerprint = ComputeContentFingerprint();
            return true;
        }

        public void RefreshPersistenceMetadata(bool initializeMissingRevision)
        {
            if (initializeMissingRevision && PersistenceRevision <= 0 && HasPendingData)
            {
                PersistenceRevision = 1;
            }

            PersistenceFingerprint = ComputeContentFingerprint();
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

        private static string ScheduleIntentKey(WorkloadV2ScheduleIntentRecord value)
        {
            return (value?.Scope ?? 0) + "\u001f"
                + (value?.TargetKind ?? 0) + "\u001f"
                + (value?.PawnId ?? string.Empty) + "\u001f"
                + (value?.WorkTypeDefName ?? string.Empty) + "\u001f"
                + (value?.WorkGiverDefName ?? string.Empty);
        }

        private static string SpecificPriorityIntentKey(WorkloadV2SpecificPriorityIntentRecord value)
        {
            return (value?.Scope ?? 0) + "\u001f"
                + (value?.PawnId ?? string.Empty) + "\u001f"
                + (value?.WorkTypeDefName ?? string.Empty) + "\u001f"
                + (value?.WorkGiverDefName ?? string.Empty);
        }

        private static string WorkTypeOrderIntentKey(WorkloadV2WorkTypeOrderIntentRecord value)
        {
            return (value?.Scope ?? 0) + "\u001f"
                + (value?.PawnId ?? string.Empty) + "\u001f"
                + (value?.WorkTypeDefName ?? string.Empty);
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
                WorkloadV2PersistenceKeys.PersistenceRevision,
                WorkloadV2PersistenceKeys.PersistenceFingerprint,
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
                "parentPriorityIntents",
                "manualModeIntents",
                "scheduleIntents",
                "specificPriorityIntents",
                "workTypeOrderIntents",
                "presentationSettingIntents",
                "legacyScheduleRequiresReview",
                "legacyOrderRequiresReview",
                "migrationDiagnostic",
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
                "intentState",
                "scope",
                "targetKind",
                "priorities",
                "pinnedHourMask",
                "orderedWorkGiverDefNames",
                "isComplete",
                "ownership",
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
        public List<WorkloadV2ParentPriorityIntentRecord> ParentPriorityIntents = new List<WorkloadV2ParentPriorityIntentRecord>();
        public List<WorkloadV2ManualModeIntentRecord> ManualModeIntents = new List<WorkloadV2ManualModeIntentRecord>();
        public List<WorkloadV2ScheduleIntentRecord> ScheduleIntents = new List<WorkloadV2ScheduleIntentRecord>();
        public List<WorkloadV2SpecificPriorityIntentRecord> SpecificPriorityIntents = new List<WorkloadV2SpecificPriorityIntentRecord>();
        public List<WorkloadV2WorkTypeOrderIntentRecord> WorkTypeOrderIntents = new List<WorkloadV2WorkTypeOrderIntentRecord>();
        public List<WorkloadV2PresentationSettingIntentRecord> PresentationSettingIntents = new List<WorkloadV2PresentationSettingIntentRecord>();
        public bool LegacyScheduleRequiresReview;
        public bool LegacyOrderRequiresReview;
        public string MigrationDiagnostic = string.Empty;

        // The schema-2 in-memory model lost whether the typed XML member was
        // present. Keep that loading fact through migration so an explicit
        // empty list is not mistaken for scalar-only data.
        internal bool PresentationSettingIntentsWerePersisted { get; private set; }

        internal bool HasTypedPresentationSettingSource =>
            PresentationSettingIntentsWerePersisted ||
            (PresentationSettingIntents != null && PresentationSettingIntents.Count > 0);

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                PresentationSettingIntentsWerePersisted = HasMember(
                    Scribe.loader?.curXmlParent,
                    "presentationSettingIntents");
            }

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
            Scribe_Collections.Look(ref ParentPriorityIntents, "parentPriorityIntents", LookMode.Deep);
            Scribe_Collections.Look(ref ManualModeIntents, "manualModeIntents", LookMode.Deep);
            Scribe_Collections.Look(ref ScheduleIntents, "scheduleIntents", LookMode.Deep);
            Scribe_Collections.Look(ref SpecificPriorityIntents, "specificPriorityIntents", LookMode.Deep);
            Scribe_Collections.Look(ref WorkTypeOrderIntents, "workTypeOrderIntents", LookMode.Deep);
            Scribe_Collections.Look(ref PresentationSettingIntents, "presentationSettingIntents", LookMode.Deep);
            Scribe_Values.Look(ref LegacyScheduleRequiresReview, "legacyScheduleRequiresReview", false);
            Scribe_Values.Look(ref LegacyOrderRequiresReview, "legacyOrderRequiresReview", false);
            Scribe_Values.Look(ref MigrationDiagnostic, "migrationDiagnostic", string.Empty);

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
            ParentPriorityIntents ??= new List<WorkloadV2ParentPriorityIntentRecord>();
            ManualModeIntents ??= new List<WorkloadV2ManualModeIntentRecord>();
            ScheduleIntents ??= new List<WorkloadV2ScheduleIntentRecord>();
            SpecificPriorityIntents ??= new List<WorkloadV2SpecificPriorityIntentRecord>();
            WorkTypeOrderIntents ??= new List<WorkloadV2WorkTypeOrderIntentRecord>();
            PresentationSettingIntents ??= new List<WorkloadV2PresentationSettingIntentRecord>();
            MigrationDiagnostic ??= string.Empty;
        }

        internal void MarkPresentationSettingIntentsPersisted()
        {
            PresentationSettingIntentsWerePersisted = true;
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
            ParentPriorityIntents.Sort(CompareParentPriorityIntents);
            ManualModeIntents.Sort(CompareManualModeIntents);
            ScheduleIntents.Sort(CompareScheduleIntents);
            SpecificPriorityIntents.Sort(CompareSpecificPriorityIntents);
            WorkTypeOrderIntents.Sort(CompareWorkTypeOrderIntents);
            PresentationSettingIntents.Sort(ComparePresentationSettingIntents);

            for (int i = SpecificPriorityIntents.Count - 1; i >= 0; i--)
            {
                WorkloadV2SpecificPriorityIntentRecord value = SpecificPriorityIntents[i];
                if (value != null && value.IntentState == (int)WorkloadIntentState.NoOpinion)
                {
                    SpecificPriorityIntents.RemoveAt(i);
                }
            }

            for (int i = WorkTypeOrderIntents.Count - 1; i >= 0; i--)
            {
                WorkloadV2WorkTypeOrderIntentRecord value = WorkTypeOrderIntents[i];
                if (value != null && value.IntentState == (int)WorkloadIntentState.NoOpinion)
                {
                    WorkTypeOrderIntents.RemoveAt(i);
                }
            }
        }

        private static void NormalizeStrings(List<string> values)
        {
            values.Sort(StringComparer.Ordinal);
            for (int i = values.Count - 1; i > 0; i--)
            {
                if (StringComparer.Ordinal.Equals(values[i], values[i - 1])) values.RemoveAt(i);
            }
        }

        private static bool HasMember(XmlNode node, string name)
        {
            if (node == null || string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element &&
                    string.Equals(child.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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

        private static int CompareParentPriorityIntents(
            WorkloadV2ParentPriorityIntentRecord left,
            WorkloadV2ParentPriorityIntentRecord right)
        {
            return StringComparer.Ordinal.Compare(
                ParentKey(left?.PawnId, left?.WorkTypeDefName),
                ParentKey(right?.PawnId, right?.WorkTypeDefName));
        }

        private static int CompareManualModeIntents(
            WorkloadV2ManualModeIntentRecord left,
            WorkloadV2ManualModeIntentRecord right)
        {
            return StringComparer.Ordinal.Compare(
                ParentKey(left?.PawnId, left?.WorkTypeDefName),
                ParentKey(right?.PawnId, right?.WorkTypeDefName));
        }

        private static int CompareScheduleIntents(
            WorkloadV2ScheduleIntentRecord left,
            WorkloadV2ScheduleIntentRecord right)
        {
            return StringComparer.Ordinal.Compare(
                ScheduleKey(left),
                ScheduleKey(right));
        }

        private static int CompareSpecificPriorityIntents(
            WorkloadV2SpecificPriorityIntentRecord left,
            WorkloadV2SpecificPriorityIntentRecord right)
        {
            return StringComparer.Ordinal.Compare(
                SpecificPriorityKey(left),
                SpecificPriorityKey(right));
        }

        private static int CompareWorkTypeOrderIntents(
            WorkloadV2WorkTypeOrderIntentRecord left,
            WorkloadV2WorkTypeOrderIntentRecord right)
        {
            return StringComparer.Ordinal.Compare(
                WorkTypeOrderKey(left),
                WorkTypeOrderKey(right));
        }

        private static int ComparePresentationSettingIntents(
            WorkloadV2PresentationSettingIntentRecord left,
            WorkloadV2PresentationSettingIntentRecord right)
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

        private static string ScheduleKey(WorkloadV2ScheduleIntentRecord value)
        {
            return (value?.Scope ?? 0) + "\u001f"
                + (value?.TargetKind ?? 0) + "\u001f"
                + (value?.PawnId ?? string.Empty) + "\u001f"
                + (value?.WorkTypeDefName ?? string.Empty) + "\u001f"
                + (value?.WorkGiverDefName ?? string.Empty);
        }

        private static string SpecificPriorityKey(WorkloadV2SpecificPriorityIntentRecord value)
        {
            return (value?.Scope ?? 0) + "\u001f"
                + (value?.PawnId ?? string.Empty) + "\u001f"
                + (value?.WorkTypeDefName ?? string.Empty) + "\u001f"
                + (value?.WorkGiverDefName ?? string.Empty);
        }

        private static string WorkTypeOrderKey(WorkloadV2WorkTypeOrderIntentRecord value)
        {
            return (value?.Scope ?? 0) + "\u001f"
                + (value?.PawnId ?? string.Empty) + "\u001f"
                + (value?.WorkTypeDefName ?? string.Empty);
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

    public sealed class WorkloadV2ParentPriorityIntentRecord : IExposable
    {
        public string PawnId = string.Empty;
        public string WorkTypeDefName = string.Empty;
        public int IntentState;
        public int Priority;

        public void ExposeData()
        {
            Scribe_Values.Look(ref PawnId, "pawnId", string.Empty);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName", string.Empty);
            Scribe_Values.Look(ref IntentState, "intentState", (int)WorkloadIntentState.NoOpinion);
            Scribe_Values.Look(ref Priority, "priority", 0);
        }
    }

    public sealed class WorkloadV2ManualModeIntentRecord : IExposable
    {
        public string PawnId = string.Empty;
        public string WorkTypeDefName = string.Empty;
        public int IntentState;
        public bool Manual;

        public void ExposeData()
        {
            Scribe_Values.Look(ref PawnId, "pawnId", string.Empty);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName", string.Empty);
            Scribe_Values.Look(ref IntentState, "intentState", (int)WorkloadIntentState.NoOpinion);
            Scribe_Values.Look(ref Manual, "manual", false);
        }
    }

    public sealed class WorkloadV2ScheduleIntentRecord : IExposable
    {
        public int Scope;
        public string PawnId = string.Empty;
        public int TargetKind;
        public string WorkTypeDefName = string.Empty;
        public string WorkGiverDefName = string.Empty;
        public int IntentState;
        public List<int> Priorities = new List<int>();
        public int PinnedHourMask;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Scope, "scope", (int)WorkloadTargetScope.PawnLocal);
            Scribe_Values.Look(ref PawnId, "pawnId", string.Empty);
            Scribe_Values.Look(ref TargetKind, "targetKind", (int)WorkloadScheduleTargetKind.ParentWorkType);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName", string.Empty);
            Scribe_Values.Look(ref WorkGiverDefName, "workGiverDefName", string.Empty);
            Scribe_Values.Look(ref IntentState, "intentState", (int)WorkloadIntentState.NoOpinion);
            Scribe_Collections.Look(ref Priorities, "priorities", LookMode.Value);
            Scribe_Values.Look(ref PinnedHourMask, "pinnedHourMask", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Priorities ??= new List<int>();
            }
        }
    }

    public sealed class WorkloadV2SpecificPriorityIntentRecord : IExposable
    {
        public int Scope;
        public string PawnId = string.Empty;
        public string WorkTypeDefName = string.Empty;
        public string WorkGiverDefName = string.Empty;
        public int IntentState;
        public int Priority;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Scope, "scope", (int)WorkloadTargetScope.PawnLocal);
            Scribe_Values.Look(ref PawnId, "pawnId", string.Empty);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName", string.Empty);
            Scribe_Values.Look(ref WorkGiverDefName, "workGiverDefName", string.Empty);
            Scribe_Values.Look(ref IntentState, "intentState", (int)WorkloadIntentState.NoOpinion);
            Scribe_Values.Look(ref Priority, "priority", 0);
        }
    }

    public sealed class WorkloadV2WorkTypeOrderIntentRecord : IExposable
    {
        public int Scope;
        public string PawnId = string.Empty;
        public string WorkTypeDefName = string.Empty;
        public int IntentState;
        public List<string> OrderedWorkGiverDefNames = new List<string>();
        public bool IsComplete;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Scope, "scope", (int)WorkloadTargetScope.PawnLocal);
            Scribe_Values.Look(ref PawnId, "pawnId", string.Empty);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName", string.Empty);
            Scribe_Values.Look(ref IntentState, "intentState", (int)WorkloadIntentState.NoOpinion);
            Scribe_Collections.Look(ref OrderedWorkGiverDefNames, "orderedWorkGiverDefNames", LookMode.Value);
            Scribe_Values.Look(ref IsComplete, "isComplete", false);
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                OrderedWorkGiverDefNames ??= new List<string>();
            }
        }
    }

    public sealed class WorkloadV2PresentationSettingIntentRecord : IExposable
    {
        public string Key = string.Empty;
        public int IntentState;
        public int Ownership;
        public WorkloadV2ScalarRecord Value = new WorkloadV2ScalarRecord();

        public void ExposeData()
        {
            Scribe_Values.Look(ref Key, "key", string.Empty);
            Scribe_Values.Look(ref IntentState, "intentState", (int)WorkloadIntentState.NoOpinion);
            Scribe_Values.Look(ref Ownership, "ownership", (int)WorkloadSettingOwnership.WorkloadOwned);
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

    /// <summary>
    /// Loss-aware migration from the original positive-value V2 document.
    /// Version 1 had no explicit clear records and its schedule/order payloads
    /// were not lossless. Those values are retained and marked read-only
    /// instead of being guessed into the typed schema.
    /// </summary>
    internal static class WorkloadV2Migration
    {
        internal static bool TryMigrateEnvelope(
            WorkloadV2PersistenceEnvelope envelope,
            out string error)
        {
            error = string.Empty;
            if (envelope == null) return true;

            bool migrateEnvelope = IsMigratableSchema(envelope.SchemaVersion);
            if (envelope.SchemaVersion == 0 && envelope.HasPersistedDocument)
            {
                error = "The saved Workloads V2 document has no schema version and cannot be migrated safely.";
                return false;
            }

            if (!migrateEnvelope && envelope.SchemaVersion != WorkloadSchema.CurrentVersion)
            {
                return true;
            }

            List<WorkloadV2PersistenceRecord> records =
                envelope.Records ?? new List<WorkloadV2PersistenceRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                WorkloadV2PersistenceRecord record = records[i];
                if (record == null)
                {
                    error = "The saved Workloads V2 document contains a missing record.";
                    return false;
                }

                if (record.SchemaVersion == 0)
                {
                    error = "A saved Workloads V2 record has no schema version and cannot be migrated safely.";
                    return false;
                }

                if (record.SchemaVersion < WorkloadSchema.LegacyVersion ||
                    record.SchemaVersion > WorkloadSchema.CurrentVersion)
                {
                    return true;
                }

                if (IsMigratableSchema(record.SchemaVersion) &&
                    !ValidateMigratableRecord(record, out error))
                {
                    return false;
                }
            }

            // All records have passed validation, so the in-memory migration
            // cannot leave a partially converted envelope.
            for (int i = 0; i < records.Count; i++)
            {
                if (IsMigratableSchema(records[i].SchemaVersion))
                {
                    MigrateKnownRecord(records[i]);
                }
            }

            if (migrateEnvelope) envelope.SchemaVersion = WorkloadSchema.CurrentVersion;
            envelope.Records = records;
            return true;
        }

        private static bool IsMigratableSchema(int schemaVersion)
        {
            return schemaVersion >= WorkloadSchema.LegacyVersion &&
                   schemaVersion < WorkloadSchema.CurrentVersion;
        }

        private static bool ValidateMigratableRecord(
            WorkloadV2PersistenceRecord record,
            out string error)
        {
            if (record.SchemaVersion == WorkloadSchema.LegacyVersion)
            {
                return ValidateLegacyRecord(record, out error);
            }

            if (record.SchemaVersion == WorkloadSchema.PresentationIntentVersion)
            {
                return ValidatePresentationSettings(record, out error);
            }

            error = "This Workloads V2 record version has no document-load migration.";
            return false;
        }

        private static void MigrateKnownRecord(WorkloadV2PersistenceRecord record)
        {
            if (record.SchemaVersion == WorkloadSchema.LegacyVersion)
            {
                MigrateLegacyRecord(record);
                return;
            }

            if (record.SchemaVersion == WorkloadSchema.PresentationIntentVersion)
            {
                MigratePresentationIntentRecord(record);
            }
        }

        private static bool ValidateLegacyRecord(
            WorkloadV2PersistenceRecord record,
            out string error)
        {
            error = string.Empty;
            record.EnsureCollections();
            if (string.IsNullOrWhiteSpace(record.StableId))
            {
                error = "A legacy Workloads V2 record has no stable ID.";
                return false;
            }

            for (int i = 0; i < record.ParentPriorities.Count; i++)
            {
                WorkloadV2ParentPriorityRecord value = record.ParentPriorities[i];
                if (value == null || string.IsNullOrWhiteSpace(value.PawnId) ||
                    string.IsNullOrWhiteSpace(value.WorkTypeDefName))
                {
                    error = "A legacy parent-priority record has an incomplete identity.";
                    return false;
                }
            }

            for (int i = 0; i < record.ManualModes.Count; i++)
            {
                WorkloadV2ManualModeRecord value = record.ManualModes[i];
                if (value == null || string.IsNullOrWhiteSpace(value.PawnId) ||
                    string.IsNullOrWhiteSpace(value.WorkTypeDefName))
                {
                    error = "A legacy manual-mode record has an incomplete identity.";
                    return false;
                }
            }

            for (int i = 0; i < record.Schedules.Count; i++)
            {
                WorkloadV2ScheduleRecord value = record.Schedules[i];
                if (value == null || string.IsNullOrWhiteSpace(value.PawnId) || value.Schedule < 0)
                {
                    error = "A legacy schedule record has an incomplete identity or invalid key.";
                    return false;
                }
            }

            for (int i = 0; i < record.SpecificJobOverrides.Count; i++)
            {
                WorkloadV2SpecificJobOverrideRecord value = record.SpecificJobOverrides[i];
                if (value == null || string.IsNullOrWhiteSpace(value.PawnId) ||
                    string.IsNullOrWhiteSpace(value.WorkTypeDefName) ||
                    string.IsNullOrWhiteSpace(value.WorkGiverDefName))
                {
                    error = "A legacy specific-job override has an incomplete identity.";
                    return false;
                }

                if (value.Value == null ||
                    value.Value.Kind != (int)WorkloadScalarKind.Integer)
                {
                    error = "A legacy specific-job override does not contain a recoverable integer priority.";
                    return false;
                }
            }

            for (int i = 0; i < record.SpecificJobOrder.Count; i++)
            {
                WorkloadV2SpecificJobOrderRecord value = record.SpecificJobOrder[i];
                if (value == null || string.IsNullOrWhiteSpace(value.PawnId) ||
                    string.IsNullOrWhiteSpace(value.WorkTypeDefName) ||
                    string.IsNullOrWhiteSpace(value.WorkGiverDefName) || value.Order < 0)
                {
                    error = "A legacy specific-job order record has an incomplete identity or invalid rank.";
                    return false;
                }
            }

            return ValidatePresentationSettings(record, out error);
        }

        private static bool ValidatePresentationSettings(
            WorkloadV2PersistenceRecord record,
            out string error)
        {
            error = string.Empty;
            record.EnsureCollections();
            for (int i = 0; i < record.PresentationSettings.Count; i++)
            {
                WorkloadV2PresentationSettingRecord value = record.PresentationSettings[i];
                if (value == null || string.IsNullOrWhiteSpace(value.Key))
                {
                    error = "A legacy presentation setting has no key.";
                    return false;
                }

                if (!ValidateLegacyScalar(value.Value, out error)) return false;
            }

            return true;
        }

        private static bool ValidateLegacyScalar(
            WorkloadV2ScalarRecord value,
            out string error)
        {
            error = string.Empty;
            if (value == null || !Enum.IsDefined(typeof(WorkloadScalarKind), value.Kind))
            {
                error = "A legacy presentation setting contains an unknown scalar kind.";
                return false;
            }

            if (value.Kind == (int)WorkloadScalarKind.String && value.StringValue == null)
            {
                error = "A legacy presentation setting contains a missing string scalar.";
                return false;
            }

            return true;
        }

        private static void MigrateLegacyRecord(WorkloadV2PersistenceRecord record)
        {
            record.EnsureCollections();
            string legacyOrderError = string.Empty;
            List<WorkloadV2WorkTypeOrderIntentRecord> migratedOrderIntents;
            bool legacyOrderSafe = TryBuildLegacySpecificJobOrderIntents(
                record,
                out migratedOrderIntents,
                out legacyOrderError);

            for (int i = 0; i < record.ParentPriorities.Count; i++)
            {
                WorkloadV2ParentPriorityRecord value = record.ParentPriorities[i];
                record.ParentPriorityIntents.Add(new WorkloadV2ParentPriorityIntentRecord
                {
                    PawnId = value.PawnId,
                    WorkTypeDefName = value.WorkTypeDefName,
                    IntentState = (int)WorkloadIntentState.Set,
                    Priority = value.Priority
                });
            }

            for (int i = 0; i < record.ManualModes.Count; i++)
            {
                WorkloadV2ManualModeRecord value = record.ManualModes[i];
                record.ManualModeIntents.Add(new WorkloadV2ManualModeIntentRecord
                {
                    PawnId = value.PawnId,
                    WorkTypeDefName = value.WorkTypeDefName,
                    IntentState = (int)WorkloadIntentState.Set,
                    Manual = value.Manual
                });
            }

            for (int i = 0; i < record.SpecificJobOverrides.Count; i++)
            {
                WorkloadV2SpecificJobOverrideRecord value = record.SpecificJobOverrides[i];
                record.SpecificPriorityIntents.Add(new WorkloadV2SpecificPriorityIntentRecord
                {
                    Scope = (int)WorkloadTargetScope.PawnLocal,
                    PawnId = value.PawnId,
                    WorkTypeDefName = value.WorkTypeDefName,
                    WorkGiverDefName = value.WorkGiverDefName,
                    IntentState = (int)WorkloadIntentState.Set,
                    Priority = value.Value?.IntegerValue ?? 0
                });
            }

            for (int i = 0; i < record.PresentationSettings.Count; i++)
            {
                WorkloadV2PresentationSettingRecord value = record.PresentationSettings[i];
                record.PresentationSettingIntents.Add(new WorkloadV2PresentationSettingIntentRecord
                {
                    Key = value.Key,
                    IntentState = (int)WorkloadIntentState.Set,
                    Ownership = (int)WorkloadSettingOwnership.WorkloadOwned,
                    Value = value.Value ?? new WorkloadV2ScalarRecord()
                });
            }

            record.MarkPresentationSettingIntentsPersisted();

            if (record.Schedules.Count > 0)
            {
                record.LegacyScheduleRequiresReview = true;
            }

            if (record.SpecificJobOrder.Count > 0 && legacyOrderSafe)
            {
                record.WorkTypeOrderIntents.AddRange(migratedOrderIntents);
            }
            else if (record.SpecificJobOrder.Count > 0)
            {
                record.LegacyOrderRequiresReview = true;
            }

            if (record.LegacyScheduleRequiresReview || record.LegacyOrderRequiresReview)
            {
                var reasons = new List<string>();
                if (record.LegacyScheduleRequiresReview)
                {
                    reasons.Add("legacy schedule records lack 24-hour linked/pinned state");
                }
                if (record.LegacyOrderRequiresReview)
                {
                    reasons.Add(
                        "legacy order records are not proven complete WorkType permutations" +
                        (string.IsNullOrWhiteSpace(legacyOrderError)
                            ? string.Empty
                            : ": " + legacyOrderError));
                }

                record.MigrationDiagnostic =
                    "The workload was migrated to the typed schema but remains read-only until " +
                    string.Join(" and ", reasons.ToArray()) + ".";
            }

            record.SchemaVersion = WorkloadSchema.CurrentVersion;
        }

        private static void MigratePresentationIntentRecord(WorkloadV2PersistenceRecord record)
        {
            record.EnsureCollections();
            if (!record.HasTypedPresentationSettingSource)
            {
                for (int i = 0; i < record.PresentationSettings.Count; i++)
                {
                    WorkloadV2PresentationSettingRecord value = record.PresentationSettings[i];
                    record.PresentationSettingIntents.Add(
                        new WorkloadV2PresentationSettingIntentRecord
                        {
                            Key = value.Key,
                            IntentState = (int)WorkloadIntentState.Set,
                            Ownership = (int)WorkloadSettingOwnership.WorkloadOwned,
                            Value = value.Value ?? new WorkloadV2ScalarRecord()
                        });
                }
            }

            record.MarkPresentationSettingIntentsPersisted();
            record.SchemaVersion = WorkloadSchema.CurrentVersion;
        }

        private static bool TryBuildLegacySpecificJobOrderIntents(
            WorkloadV2PersistenceRecord record,
            out List<WorkloadV2WorkTypeOrderIntentRecord> converted,
            out string error)
        {
            converted = new List<WorkloadV2WorkTypeOrderIntentRecord>();
            error = string.Empty;
            if (record == null || record.SpecificJobOrder == null || record.SpecificJobOrder.Count == 0)
            {
                return true;
            }

            var groups = new Dictionary<string, List<WorkloadV2SpecificJobOrderRecord>>(StringComparer.Ordinal);
            for (int i = 0; i < record.SpecificJobOrder.Count; i++)
            {
                WorkloadV2SpecificJobOrderRecord value = record.SpecificJobOrder[i];
                string key = LegacyParentKey(value?.PawnId, value?.WorkTypeDefName);
                if (!groups.TryGetValue(key, out List<WorkloadV2SpecificJobOrderRecord> group))
                {
                    group = new List<WorkloadV2SpecificJobOrderRecord>();
                    groups.Add(key, group);
                }

                group.Add(value);
            }

            var existingTypedTargets = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < record.WorkTypeOrderIntents.Count; i++)
            {
                WorkloadV2WorkTypeOrderIntentRecord value = record.WorkTypeOrderIntents[i];
                if (value == null)
                {
                    error = "an existing typed WorkType order record is missing";
                    return false;
                }

                string key = ((int)WorkloadTargetScope.PawnLocal) + "\u001f" +
                    LegacyParentKey(value.PawnId, value.WorkTypeDefName);
                if (!existingTypedTargets.Add(key))
                {
                    error = "an existing typed WorkType order target is duplicated";
                    return false;
                }
            }

            var groupKeys = new List<string>(groups.Keys);
            groupKeys.Sort(StringComparer.Ordinal);
            for (int groupIndex = 0; groupIndex < groupKeys.Count; groupIndex++)
            {
                string groupKey = groupKeys[groupIndex];
                List<WorkloadV2SpecificJobOrderRecord> group = groups[groupKey];
                group.Sort((left, right) =>
                {
                    int order = (left?.Order ?? -1).CompareTo(right?.Order ?? -1);
                    return order != 0
                        ? order
                        : StringComparer.Ordinal.Compare(
                            left?.WorkGiverDefName ?? string.Empty,
                            right?.WorkGiverDefName ?? string.Empty);
                });

                var workGiverNames = new HashSet<string>(StringComparer.Ordinal);
                var orderedNames = new List<string>(group.Count);
                for (int orderIndex = 0; orderIndex < group.Count; orderIndex++)
                {
                    WorkloadV2SpecificJobOrderRecord value = group[orderIndex];
                    if (value == null || string.IsNullOrWhiteSpace(value.PawnId) ||
                        string.IsNullOrWhiteSpace(value.WorkTypeDefName) ||
                        string.IsNullOrWhiteSpace(value.WorkGiverDefName) || value.Order < 0)
                    {
                        error = "an order group contains an incomplete identity";
                        return false;
                    }

                    if (value.Order != orderIndex)
                    {
                        error = "order ranks must be a unique zero-based sequence";
                        return false;
                    }

                    if (!workGiverNames.Add(value.WorkGiverDefName))
                    {
                        error = "an order group contains a duplicate WorkGiver identity";
                        return false;
                    }

                    orderedNames.Add(value.WorkGiverDefName);
                }

                string typedKey = ((int)WorkloadTargetScope.PawnLocal) + "\u001f" + groupKey;
                if (existingTypedTargets.Contains(typedKey))
                {
                    error = "legacy order overlaps an existing typed WorkType order target";
                    return false;
                }

                converted.Add(new WorkloadV2WorkTypeOrderIntentRecord
                {
                    Scope = (int)WorkloadTargetScope.PawnLocal,
                    PawnId = group[0].PawnId,
                    WorkTypeDefName = group[0].WorkTypeDefName,
                    IntentState = (int)WorkloadIntentState.Set,
                    IsComplete = true,
                    OrderedWorkGiverDefNames = orderedNames
                });
            }

            return true;
        }

        private static string LegacyParentKey(string pawnId, string workTypeDefName)
        {
            return (pawnId ?? string.Empty) + "\u001f" + (workTypeDefName ?? string.Empty);
        }
    }

    internal static class WorkloadV2PersistenceCanonical
    {
        internal static string ForRecord(WorkloadV2PersistenceRecord record)
        {
            if (record == null) return "<null>";
            record.NormalizeStableState();
            var builder = new System.Text.StringBuilder();
            builder.Append(WorkloadCanonical.Encode(record.StableId));
            builder.Append(WorkloadCanonical.Encode(record.Label));
            builder.Append(record.SchemaVersion).Append(':');
            builder.Append(record.OwnershipDimensions).Append(':').Append(record.ScopeMode).Append(':');
            AppendStrings(builder, record.ExplicitPawnIds);
            AppendStrings(builder, record.ExcludedPawnIds);
            for (int i = 0; i < record.ParentPriorities.Count; i++)
            {
                WorkloadV2ParentPriorityRecord value = record.ParentPriorities[i];
                builder.Append("p|").Append(WorkloadCanonical.Encode(value?.PawnId))
                    .Append(WorkloadCanonical.Encode(value?.WorkTypeDefName)).Append(value?.Priority ?? 0).Append(';');
            }
            for (int i = 0; i < record.ManualModes.Count; i++)
            {
                WorkloadV2ManualModeRecord value = record.ManualModes[i];
                builder.Append("m|").Append(WorkloadCanonical.Encode(value?.PawnId))
                    .Append(WorkloadCanonical.Encode(value?.WorkTypeDefName))
                    .Append(value?.Manual == true ? '1' : '0').Append(';');
            }
            for (int i = 0; i < record.Schedules.Count; i++)
            {
                WorkloadV2ScheduleRecord value = record.Schedules[i];
                builder.Append("s|").Append(WorkloadCanonical.Encode(value?.PawnId))
                    .Append(value?.Schedule ?? -1).Append(';');
            }
            for (int i = 0; i < record.SpecificJobOverrides.Count; i++)
            {
                WorkloadV2SpecificJobOverrideRecord value = record.SpecificJobOverrides[i];
                builder.Append("o|").Append(WorkloadCanonical.Encode(value?.PawnId))
                    .Append(WorkloadCanonical.Encode(value?.WorkTypeDefName))
                    .Append(WorkloadCanonical.Encode(value?.WorkGiverDefName))
                    .Append(ScalarCanonical(value?.Value)).Append(';');
            }
            for (int i = 0; i < record.SpecificJobOrder.Count; i++)
            {
                WorkloadV2SpecificJobOrderRecord value = record.SpecificJobOrder[i];
                builder.Append("r|").Append(WorkloadCanonical.Encode(value?.PawnId))
                    .Append(WorkloadCanonical.Encode(value?.WorkTypeDefName))
                    .Append(WorkloadCanonical.Encode(value?.WorkGiverDefName))
                    .Append(value?.Order ?? -1).Append(';');
            }
            for (int i = 0; i < record.PresentationSettings.Count; i++)
            {
                WorkloadV2PresentationSettingRecord value = record.PresentationSettings[i];
                builder.Append("t|").Append(WorkloadCanonical.Encode(value?.Key))
                    .Append(ScalarCanonical(value?.Value)).Append(';');
            }
            for (int i = 0; i < record.ParentPriorityIntents.Count; i++)
            {
                WorkloadV2ParentPriorityIntentRecord value = record.ParentPriorityIntents[i];
                builder.Append("pi|").Append(WorkloadCanonical.Encode(value?.PawnId))
                    .Append(WorkloadCanonical.Encode(value?.WorkTypeDefName))
                    .Append(value?.IntentState ?? 0).Append(':').Append(value?.Priority ?? 0).Append(';');
            }
            for (int i = 0; i < record.ManualModeIntents.Count; i++)
            {
                WorkloadV2ManualModeIntentRecord value = record.ManualModeIntents[i];
                builder.Append("mi|").Append(WorkloadCanonical.Encode(value?.PawnId))
                    .Append(WorkloadCanonical.Encode(value?.WorkTypeDefName))
                    .Append(value?.IntentState ?? 0).Append(':')
                    .Append(value?.Manual == true ? '1' : '0').Append(';');
            }
            for (int i = 0; i < record.ScheduleIntents.Count; i++)
            {
                WorkloadV2ScheduleIntentRecord value = record.ScheduleIntents[i];
                builder.Append("si|").Append(ScheduleIntentCanonical(value)).Append(';');
            }
            for (int i = 0; i < record.SpecificPriorityIntents.Count; i++)
            {
                WorkloadV2SpecificPriorityIntentRecord value = record.SpecificPriorityIntents[i];
                builder.Append("oi|").Append(SpecificPriorityIntentCanonical(value)).Append(';');
            }
            for (int i = 0; i < record.WorkTypeOrderIntents.Count; i++)
            {
                WorkloadV2WorkTypeOrderIntentRecord value = record.WorkTypeOrderIntents[i];
                builder.Append("ri|").Append(WorkTypeOrderIntentCanonical(value)).Append(';');
            }
            for (int i = 0; i < record.PresentationSettingIntents.Count; i++)
            {
                WorkloadV2PresentationSettingIntentRecord value = record.PresentationSettingIntents[i];
                builder.Append("ti|").Append(WorkloadCanonical.Encode(value?.Key))
                    .Append(value?.IntentState ?? 0).Append(':').Append(value?.Ownership ?? 0)
                    .Append(':').Append(ScalarCanonical(value?.Value)).Append(';');
            }
            builder.Append(record.LegacyScheduleRequiresReview ? "legacy-schedule;" : string.Empty);
            builder.Append(record.LegacyOrderRequiresReview ? "legacy-order;" : string.Empty);
            builder.Append(WorkloadCanonical.Encode(record.MigrationDiagnostic));
            return builder.ToString();
        }

        private static void AppendStrings(System.Text.StringBuilder builder, List<string> values)
        {
            if (values == null) return;
            for (int i = 0; i < values.Count; i++)
            {
                builder.Append(WorkloadCanonical.Encode(values[i])).Append(';');
            }
        }

        private static string ScheduleIntentCanonical(WorkloadV2ScheduleIntentRecord value)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(value?.Scope ?? 0).Append(':').Append(value?.TargetKind ?? 0)
                .Append(':').Append(WorkloadCanonical.Encode(value?.PawnId))
                .Append(WorkloadCanonical.Encode(value?.WorkTypeDefName))
                .Append(WorkloadCanonical.Encode(value?.WorkGiverDefName))
                .Append(':').Append(value?.IntentState ?? 0)
                .Append(':').Append(value?.PinnedHourMask ?? 0).Append(':');
            AppendInts(builder, value?.Priorities);
            return builder.ToString();
        }

        private static string SpecificPriorityIntentCanonical(WorkloadV2SpecificPriorityIntentRecord value)
        {
            return (value?.Scope ?? 0) + ":" + WorkloadCanonical.Encode(value?.PawnId) +
                WorkloadCanonical.Encode(value?.WorkTypeDefName) +
                WorkloadCanonical.Encode(value?.WorkGiverDefName) + ":" +
                (value?.IntentState ?? 0) + ":" + (value?.Priority ?? 0);
        }

        private static string WorkTypeOrderIntentCanonical(WorkloadV2WorkTypeOrderIntentRecord value)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(value?.Scope ?? 0).Append(':')
                .Append(WorkloadCanonical.Encode(value?.PawnId))
                .Append(WorkloadCanonical.Encode(value?.WorkTypeDefName))
                .Append(':').Append(value?.IntentState ?? 0)
                .Append(':').Append(value?.IsComplete == true ? '1' : '0').Append(':');
            AppendStrings(builder, value?.OrderedWorkGiverDefNames);
            return builder.ToString();
        }

        private static void AppendInts(System.Text.StringBuilder builder, List<int> values)
        {
            if (values == null) return;
            for (int i = 0; i < values.Count; i++) builder.Append(values[i]).Append(';');
        }

        private static string ScalarCanonical(WorkloadV2ScalarRecord value)
        {
            if (value == null) return "<null>";
            var builder = new System.Text.StringBuilder();
            builder.Append(value.Kind).Append(':')
                .Append(value.BooleanValue ? '1' : '0').Append(':')
                .Append(value.IntegerValue).Append(':')
                .Append(WorkloadCanonical.Encode(value.StringValue));
            return builder.ToString();
        }
    }
}

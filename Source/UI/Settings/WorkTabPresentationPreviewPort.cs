using System;
using System.Collections.Generic;

namespace Better_Work_Tab.UI.Settings
{
    internal interface IWorkTabPresentationPreviewPort
    {
        bool IsPreviewActive { get; }

        bool TryReadPresentationPreview(
            out WorkTabPresentationPreviewState preview,
            out string reason);

        bool TryMutatePresentation(
            WorkTabPresentationPreviewMutation mutation,
            out string reason);
    }

    internal enum PresentationValueKind
    {
        Empty = 0,
        Boolean = 1,
        Integer = 2,
        String = 3
    }

    /// <summary>Presentation-owned scalar that does not expose storage types.</summary>
    internal readonly struct PresentationValue : IEquatable<PresentationValue>
    {
        private PresentationValue(
            PresentationValueKind kind,
            bool booleanValue,
            int integerValue,
            string stringValue)
        {
            Kind = kind;
            BooleanValue = booleanValue;
            IntegerValue = integerValue;
            StringValue = stringValue;
        }

        internal PresentationValueKind Kind { get; }
        internal bool BooleanValue { get; }
        internal int IntegerValue { get; }
        internal string StringValue { get; }

        internal static PresentationValue Empty =>
            new PresentationValue(PresentationValueKind.Empty, false, 0, null);

        internal static PresentationValue FromBoolean(bool value) =>
            new PresentationValue(PresentationValueKind.Boolean, value, 0, null);

        internal static PresentationValue FromInteger(int value) =>
            new PresentationValue(PresentationValueKind.Integer, false, value, null);

        internal static PresentationValue FromString(string value) =>
            value == null
                ? Empty
                : new PresentationValue(PresentationValueKind.String, false, 0, value);

        public bool Equals(PresentationValue other)
        {
            return Kind == other.Kind &&
                   BooleanValue == other.BooleanValue &&
                   IntegerValue == other.IntegerValue &&
                   StringComparer.Ordinal.Equals(StringValue, other.StringValue);
        }

        public override bool Equals(object obj)
        {
            return obj is PresentationValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ (BooleanValue ? 1 : 0);
                hash = (hash * 397) ^ IntegerValue;
                return (hash * 397) ^
                       (StringValue == null ? 0 : StringComparer.Ordinal.GetHashCode(StringValue));
            }
        }
    }

    internal enum PresentationOwnership
    {
        Global = 0,
        PreviewOwned = 1
    }

    internal enum PresentationIntentKind
    {
        NoOpinion = 0,
        Set = 1,
        Clear = 2
    }

    /// <summary>
    /// Presentation intent detached from the storage model used by the active
    /// preview implementation.
    /// </summary>
    internal readonly struct PresentationIntent : IEquatable<PresentationIntent>
    {
        private PresentationIntent(
            PresentationIntentKind kind,
            PresentationOwnership ownership,
            PresentationValue value)
        {
            Kind = kind;
            Ownership = ownership;
            Value = value;
        }

        internal PresentationIntentKind Kind { get; }
        internal PresentationOwnership Ownership { get; }
        internal PresentationValue Value { get; }
        internal bool HasValue => Kind == PresentationIntentKind.Set;
        internal bool IsNoOpinion => Kind == PresentationIntentKind.NoOpinion;
        internal bool IsClear => Kind == PresentationIntentKind.Clear;
        internal bool IsOwned =>
            IsClear || (HasValue && Ownership == PresentationOwnership.PreviewOwned);

        internal static PresentationIntent NoOpinion =>
            new PresentationIntent(
                PresentationIntentKind.NoOpinion,
                PresentationOwnership.Global,
                PresentationValue.Empty);

        internal static PresentationIntent Clear =>
            new PresentationIntent(
                PresentationIntentKind.Clear,
                PresentationOwnership.PreviewOwned,
                PresentationValue.Empty);

        internal static PresentationIntent Set(
            PresentationValue value,
            PresentationOwnership ownership)
        {
            return new PresentationIntent(PresentationIntentKind.Set, ownership, value);
        }

        public bool Equals(PresentationIntent other)
        {
            return Kind == other.Kind &&
                   Ownership == other.Ownership &&
                   Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            return obj is PresentationIntent other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (((int)Kind * 397) ^ (int)Ownership) * 397 ^ Value.GetHashCode();
            }
        }
    }

    internal sealed class PresentationIntentEntry
    {
        internal PresentationIntentEntry(string key, PresentationIntent intent)
        {
            Key = key ?? string.Empty;
            Intent = intent;
        }

        internal string Key { get; }
        internal PresentationIntent Intent { get; }
    }

    internal enum WorkTabPresentationPreviewMutationKind
    {
        Set = 0,
        Acquire = 1,
        Release = 2
    }

    internal readonly struct WorkTabPresentationPreviewMutation
    {
        internal WorkTabPresentationPreviewMutation(
            WorkTabPresentationPreviewMutationKind kind,
            string settingId,
            PresentationValue value)
        {
            Kind = kind;
            SettingId = settingId ?? string.Empty;
            Value = value;
        }

        internal WorkTabPresentationPreviewMutationKind Kind { get; }
        internal string SettingId { get; }
        internal PresentationValue Value { get; }
    }

    internal readonly struct WorkTabPresentationPreviewState
    {
        internal WorkTabPresentationPreviewState(
            string identity,
            IReadOnlyList<PresentationIntentEntry> beforeIntents,
            IReadOnlyList<PresentationIntentEntry> afterIntents)
        {
            Identity = identity ?? string.Empty;
            BeforeIntents = beforeIntents ?? new PresentationIntentEntry[0];
            AfterIntents = afterIntents ?? new PresentationIntentEntry[0];
        }

        internal string Identity { get; }
        internal IReadOnlyList<PresentationIntentEntry> BeforeIntents { get; }
        internal IReadOnlyList<PresentationIntentEntry> AfterIntents { get; }
    }
}

using System;

namespace Better_Work_Tab.Features.Workloads.V2
{
    [Flags]
    public enum WorkloadOwnershipDimensions
    {
        None = 0,
        ParentPriorities = 1,
        ManualModes = 2,
        Schedules = 4,
        SpecificJobOverrides = 8,
        SpecificJobOrder = 16,
        PresentationSettings = 32,
        All = ParentPriorities
            | ManualModes
            | Schedules
            | SpecificJobOverrides
            | SpecificJobOrder
            | PresentationSettings
    }

    public enum WorkloadStateDimension
    {
        ParentPriorities = 0,
        ManualModes = 1,
        Schedules = 2,
        SpecificJobOverrides = 3,
        SpecificJobOrder = 4,
        PresentationSettings = 5,
        Membership = 6
    }

    public static class WorkloadOwnership
    {
        public static bool Owns(this WorkloadOwnershipDimensions ownership, WorkloadStateDimension dimension)
        {
            WorkloadOwnershipDimensions bit;
            switch (dimension)
            {
                case WorkloadStateDimension.ParentPriorities:
                    bit = WorkloadOwnershipDimensions.ParentPriorities;
                    break;
                case WorkloadStateDimension.ManualModes:
                    bit = WorkloadOwnershipDimensions.ManualModes;
                    break;
                case WorkloadStateDimension.Schedules:
                    bit = WorkloadOwnershipDimensions.Schedules;
                    break;
                case WorkloadStateDimension.SpecificJobOverrides:
                    bit = WorkloadOwnershipDimensions.SpecificJobOverrides;
                    break;
                case WorkloadStateDimension.SpecificJobOrder:
                    bit = WorkloadOwnershipDimensions.SpecificJobOrder;
                    break;
                case WorkloadStateDimension.PresentationSettings:
                    bit = WorkloadOwnershipDimensions.PresentationSettings;
                    break;
                default:
                    return false;
            }

            return (ownership & bit) == bit;
        }
    }

    public static class WorkloadSchema
    {
        public const int CurrentVersion = 1;
    }

    public sealed class WorkloadDefinition
    {
        public WorkloadDefinition(
            string stableId,
            string label,
            int schemaVersion,
            WorkloadOwnershipDimensions ownershipDimensions,
            WorkloadScope scope)
        {
            StableId = stableId ?? string.Empty;
            Label = label ?? string.Empty;
            SchemaVersion = schemaVersion;
            OwnershipDimensions = ownershipDimensions;
            Scope = scope ?? WorkloadScope.Empty;
        }

        public static WorkloadDefinition Empty
        {
            get
            {
                return new WorkloadDefinition(
                    string.Empty,
                    string.Empty,
                    0,
                    WorkloadOwnershipDimensions.None,
                    WorkloadScope.Empty);
            }
        }

        public string StableId { get; private set; }
        public string Label { get; private set; }
        public int SchemaVersion { get; private set; }
        public WorkloadOwnershipDimensions OwnershipDimensions { get; private set; }
        public WorkloadScope Scope { get; private set; }

        public string CanonicalForm
        {
            get
            {
                return WorkloadCanonical.Encode(StableId)
                    + WorkloadCanonical.Encode(Label)
                    + WorkloadCanonical.Integer(SchemaVersion)
                    + WorkloadCanonical.Integer((int)OwnershipDimensions)
                    + WorkloadCanonical.Encode(Scope.CanonicalForm);
            }
        }
    }

    public sealed class WorkloadTemplate
    {
        public WorkloadTemplate(WorkloadDefinition definition, WorkloadProjectedState projectedState)
        {
            Definition = definition ?? WorkloadDefinition.Empty;
            ProjectedState = projectedState ?? WorkloadProjectedState.Empty;
        }

        public static WorkloadTemplate Empty
        {
            get { return new WorkloadTemplate(WorkloadDefinition.Empty, WorkloadProjectedState.Empty); }
        }

        public WorkloadDefinition Definition { get; private set; }
        public WorkloadProjectedState ProjectedState { get; private set; }
        public string StableId => Definition.StableId;
        public string Label => Definition.Label;
        public int SchemaVersion => Definition.SchemaVersion;

        public string SemanticFingerprint
        {
            get { return ProjectedState.GetSemanticFingerprint(Definition.OwnershipDimensions); }
        }

        public WorkloadTemplate WithState(WorkloadProjectedState projectedState)
        {
            return new WorkloadTemplate(Definition, projectedState ?? WorkloadProjectedState.Empty);
        }

        public WorkloadTemplate WithDefinition(WorkloadDefinition definition)
        {
            return new WorkloadTemplate(definition ?? WorkloadDefinition.Empty, ProjectedState);
        }
    }
}

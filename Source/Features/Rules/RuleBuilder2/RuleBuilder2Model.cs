using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Rules.RuleBuilder2
{
    public enum RuleBuilder2SourceType
    {
        Blank,
        Duplicated,
        DefaultCopy,
        GeneratedDraft,
        Migrated,
        Imported
    }

    public enum RuleBuilder2TargetSource
    {
        BuilderList,
        WorkTabDrag,
        WorkTabClick,
        PriorityCell,
        Generated
    }

    public enum RuleBuilder2ConditionMode
    {
        All,
        AnyFuture
    }

    public enum RuleBuilder2ConditionKind
    {
        SkillMinimum,
        SkillMaximum,
        PassionAtLeast,
        Trait,
        CapacityMinimum,
        Xenotype,
        Gender,
        ExistingPriorityAtLeast,
        ExistingPriorityEquals,
        CurrentAssignedWork,
        NoteOnly
    }

    public enum RuleBuilder2ActionKind
    {
        SetPriority,
        Disable,
        FollowGlobal,
        SetTimeSchedule,
        SetSubWorkSchedule
    }

    public sealed class RuleBuilder2Ruleset : IExposable
    {
        public string StableId = Guid.NewGuid().ToString("N");
        public string Name = "Rule Builder 2.0 Ruleset";
        public string Description = "";
        public bool Enabled = true;
        public RuleBuilder2SourceType Source = RuleBuilder2SourceType.Blank;
        public int DataVersion = 2;
        public List<RuleBuilder2Card> Cards = new List<RuleBuilder2Card>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref StableId, "stableId", Guid.NewGuid().ToString("N"));
            Scribe_Values.Look(ref Name, "name", "Rule Builder 2.0 Ruleset");
            Scribe_Values.Look(ref Description, "description", "");
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref Source, "source", RuleBuilder2SourceType.Blank);
            Scribe_Values.Look(ref DataVersion, "dataVersion", 2);
            Scribe_Collections.Look(ref Cards, "cards", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (string.IsNullOrEmpty(StableId))
                {
                    StableId = Guid.NewGuid().ToString("N");
                }

                Cards ??= new List<RuleBuilder2Card>();
                for (int i = 0; i < Cards.Count; i++)
                {
                    Cards[i]?.EnsureStableState(i);
                }
            }
        }

        public RuleBuilder2Ruleset Copy(string suffix = " (Copy)")
        {
            return new RuleBuilder2Ruleset
            {
                StableId = Guid.NewGuid().ToString("N"),
                Name = (Name ?? "Ruleset") + suffix,
                Description = Description,
                Enabled = Enabled,
                Source = RuleBuilder2SourceType.Duplicated,
                DataVersion = DataVersion,
                Cards = Cards?.Select(card => card?.Copy()).Where(card => card != null).ToList() ?? new List<RuleBuilder2Card>()
            };
        }

        public void EnsureOpenBlankCard()
        {
            Cards ??= new List<RuleBuilder2Card>();
            if (Cards.Count == 0 || Cards[Cards.Count - 1].IsConfirmed)
            {
                Cards.Add(RuleBuilder2Card.CreateBlank(Cards.Count));
            }
        }
    }

    public sealed class RuleBuilder2Card : IExposable
    {
        public string StableId = Guid.NewGuid().ToString("N");
        public string Name = "";
        public string Summary = "";
        public bool Enabled = true;
        public bool IsConfirmed;
        public bool IsCollapsed;
        public int SortOrder;
        public string Notes = "";
        public List<string> Warnings = new List<string>();
        public RuleBuilder2Target Target = new RuleBuilder2Target();
        public RuleBuilder2ConditionGroup Conditions = new RuleBuilder2ConditionGroup();
        public RuleBuilder2Action Action = new RuleBuilder2Action();

        public static RuleBuilder2Card CreateBlank(int sortOrder)
        {
            return new RuleBuilder2Card
            {
                SortOrder = sortOrder,
                Name = "New rule",
                Summary = "Choose target"
            };
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref StableId, "stableId", Guid.NewGuid().ToString("N"));
            Scribe_Values.Look(ref Name, "name", "");
            Scribe_Values.Look(ref Summary, "summary", "");
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref IsConfirmed, "confirmed", false);
            Scribe_Values.Look(ref IsCollapsed, "collapsed", false);
            Scribe_Values.Look(ref SortOrder, "sortOrder", 0);
            Scribe_Values.Look(ref Notes, "notes", "");
            Scribe_Collections.Look(ref Warnings, "warnings", LookMode.Value);
            Scribe_Deep.Look(ref Target, "target");
            Scribe_Deep.Look(ref Conditions, "conditions");
            Scribe_Deep.Look(ref Action, "action");

            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                EnsureStableState(SortOrder);
            }
        }

        public void EnsureStableState(int fallbackSortOrder)
        {
            StableId = string.IsNullOrEmpty(StableId) ? Guid.NewGuid().ToString("N") : StableId;
            Warnings ??= new List<string>();
            Target ??= new RuleBuilder2Target();
            Conditions ??= new RuleBuilder2ConditionGroup();
            Action ??= new RuleBuilder2Action();
            NormalizeActionForTarget();
            Conditions.NormalizePrioritiesForCurrentSettings();
            SortOrder = SortOrder < 0 ? fallbackSortOrder : SortOrder;
        }

        public void NormalizeActionForTarget()
        {
            Target ??= new RuleBuilder2Target();
            Action ??= new RuleBuilder2Action();
            Action.NormalizeKindForTarget(Target);
            RuleBuilder2PriorityRange.NormalizeAction(Action);
        }

        public RuleBuilder2Card Copy()
        {
            return new RuleBuilder2Card
            {
                StableId = Guid.NewGuid().ToString("N"),
                Name = Name,
                Summary = Summary,
                Enabled = Enabled,
                IsConfirmed = IsConfirmed,
                IsCollapsed = IsCollapsed,
                SortOrder = SortOrder,
                Notes = Notes,
                Warnings = Warnings?.ToList() ?? new List<string>(),
                Target = Target?.Copy() ?? new RuleBuilder2Target(),
                Conditions = Conditions?.Copy() ?? new RuleBuilder2ConditionGroup(),
                Action = Action?.Copy() ?? new RuleBuilder2Action()
            };
        }
    }

    public sealed class RuleBuilder2Target : IExposable
    {
        public string WorkTypeDefName = "";
        public string WorkGiverDefName = "";
        public string DisplayLabel = "";
        public string IconPath = "";
        public RuleBuilder2TargetSource Source = RuleBuilder2TargetSource.BuilderList;

        public bool HasTarget => !string.IsNullOrEmpty(WorkTypeDefName) || !string.IsNullOrEmpty(WorkGiverDefName);
        public bool IsSubWorkTarget => !string.IsNullOrEmpty(WorkGiverDefName);

        public void ExposeData()
        {
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName", "");
            Scribe_Values.Look(ref WorkGiverDefName, "workGiverDefName", "");
            Scribe_Values.Look(ref DisplayLabel, "displayLabel", "");
            Scribe_Values.Look(ref IconPath, "iconPath", "");
            Scribe_Values.Look(ref Source, "source", RuleBuilder2TargetSource.BuilderList);
        }

        public WorkTypeDef ResolveWorkType()
        {
            if (!string.IsNullOrEmpty(WorkTypeDefName))
            {
                return DefDatabase<WorkTypeDef>.GetNamedSilentFail(WorkTypeDefName);
            }

            return ResolveWorkGiver()?.workType;
        }

        public WorkGiverDef ResolveWorkGiver()
        {
            return string.IsNullOrEmpty(WorkGiverDefName)
                ? null
                : DefDatabase<WorkGiverDef>.GetNamedSilentFail(WorkGiverDefName);
        }

        public RuleBuilder2Target Copy()
        {
            return new RuleBuilder2Target
            {
                WorkTypeDefName = WorkTypeDefName,
                WorkGiverDefName = WorkGiverDefName,
                DisplayLabel = DisplayLabel,
                IconPath = IconPath,
                Source = Source
            };
        }
    }

    public sealed class RuleBuilder2ConditionGroup : IExposable
    {
        public RuleBuilder2ConditionMode Mode = RuleBuilder2ConditionMode.All;
        public List<RuleBuilder2Condition> Conditions = new List<RuleBuilder2Condition>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref Mode, "mode", RuleBuilder2ConditionMode.All);
            Scribe_Collections.Look(ref Conditions, "conditions", LookMode.Deep);
            Conditions ??= new List<RuleBuilder2Condition>();
        }

        public RuleBuilder2ConditionGroup Copy()
        {
            return new RuleBuilder2ConditionGroup
            {
                Mode = Mode,
                Conditions = Conditions?.Select(condition => condition?.Copy()).Where(condition => condition != null).ToList()
                    ?? new List<RuleBuilder2Condition>()
            };
        }

        public void NormalizePrioritiesForCurrentSettings()
        {
            Conditions ??= new List<RuleBuilder2Condition>();
            foreach (RuleBuilder2Condition condition in Conditions)
            {
                RuleBuilder2PriorityRange.NormalizeCondition(condition);
            }
        }
    }

    public sealed class RuleBuilder2Condition : IExposable
    {
        public string StableId = Guid.NewGuid().ToString("N");
        public RuleBuilder2ConditionKind Kind = RuleBuilder2ConditionKind.SkillMinimum;
        public bool Enabled = true;
        public string DefName = "";
        public int IntValue;
        public float FloatValue;
        public bool BoolValue;
        public string TextValue = "";
        public string DisplayText = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref StableId, "stableId", Guid.NewGuid().ToString("N"));
            Scribe_Values.Look(ref Kind, "kind", RuleBuilder2ConditionKind.SkillMinimum);
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref DefName, "defName", "");
            Scribe_Values.Look(ref IntValue, "intValue", 0);
            Scribe_Values.Look(ref FloatValue, "floatValue", 0f);
            Scribe_Values.Look(ref BoolValue, "boolValue", false);
            Scribe_Values.Look(ref TextValue, "textValue", "");
            Scribe_Values.Look(ref DisplayText, "displayText", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                StableId = string.IsNullOrEmpty(StableId) ? Guid.NewGuid().ToString("N") : StableId;
                RuleBuilder2PriorityRange.NormalizeCondition(this);
            }
        }

        public RuleBuilder2Condition Copy()
        {
            return new RuleBuilder2Condition
            {
                StableId = Guid.NewGuid().ToString("N"),
                Kind = Kind,
                Enabled = Enabled,
                DefName = DefName,
                IntValue = IntValue,
                FloatValue = FloatValue,
                BoolValue = BoolValue,
                TextValue = TextValue,
                DisplayText = DisplayText
            };
        }
    }

    public sealed class RuleBuilder2Action : IExposable
    {
        public RuleBuilder2ActionKind Kind = RuleBuilder2ActionKind.SetPriority;
        public int Priority = WorkPrioritySystem.GetDefaultEnabledPriority();
        public List<int> HourlyPriorities = new List<int>();
        public bool HasElseBehavior;
        public int ElsePriority;

        [Unsaved]
        public string PriorityBuffer = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref Kind, "kind", RuleBuilder2ActionKind.SetPriority);
            Scribe_Values.Look(ref Priority, "priority", WorkPrioritySystem.GetDefaultEnabledPriority());
            Scribe_Collections.Look(ref HourlyPriorities, "hourlyPriorities", LookMode.Value);
            Scribe_Values.Look(ref HasElseBehavior, "hasElseBehavior", false);
            Scribe_Values.Look(ref ElsePriority, "elsePriority", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                RuleBuilder2PriorityRange.NormalizeAction(this);
            }
        }

        public void EnsureSchedule(int fallbackPriority)
        {
            HourlyPriorities ??= new List<int>();
            fallbackPriority = RuleBuilder2PriorityRange.Clamp(fallbackPriority);

            while (HourlyPriorities.Count < 24)
            {
                HourlyPriorities.Add(fallbackPriority);
            }

            if (HourlyPriorities.Count > 24)
            {
                HourlyPriorities.RemoveRange(24, HourlyPriorities.Count - 24);
            }

            for (int i = 0; i < HourlyPriorities.Count; i++)
            {
                HourlyPriorities[i] = RuleBuilder2PriorityRange.Clamp(HourlyPriorities[i]);
            }
        }

        public void NormalizeKindForTarget(RuleBuilder2Target target)
        {
            bool isSubWorkTarget = target?.IsSubWorkTarget == true;
            if (isSubWorkTarget && Kind == RuleBuilder2ActionKind.SetTimeSchedule)
            {
                Kind = RuleBuilder2ActionKind.SetSubWorkSchedule;
            }
            else if (!isSubWorkTarget && Kind == RuleBuilder2ActionKind.SetSubWorkSchedule)
            {
                Kind = RuleBuilder2ActionKind.SetTimeSchedule;
            }
        }

        public RuleBuilder2Action Copy()
        {
            return new RuleBuilder2Action
            {
                Kind = Kind,
                Priority = Priority,
                HourlyPriorities = HourlyPriorities?.ToList() ?? new List<int>(),
                HasElseBehavior = HasElseBehavior,
                ElsePriority = ElsePriority
            };
        }
    }

    public sealed class RuleBuilder2PreviewResult
    {
        public Pawn Pawn;
        public RuleBuilder2Target Target;
        public bool Matched;
        public List<string> ConditionsMet = new List<string>();
        public List<string> ConditionsFailed = new List<string>();
        public int CurrentPriority;
        public int NewPriority;
        public string ActionText = "";
        public string Warning = "";

        public string PawnLabel => PawnCompat.LabelShortCap(Pawn) ?? "Unknown pawn";
    }
}

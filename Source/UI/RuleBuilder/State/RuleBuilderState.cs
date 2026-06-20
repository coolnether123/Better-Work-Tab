using Better_Work_Tab.Features;
using Better_Work_Tab.Features.Rules;
using Better_Work_Tab.UI.RuleBuilder.Services;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using UnityEngine;

namespace Better_Work_Tab.UI.RuleBuilder.State
{
    /// <summary>
    /// Central state container for the priority-first rule builder.
    /// Tracks selections across all three columns and provides
    /// computed views of rules grouped by (WorkType, Priority).
    /// </summary>
    public class RuleBuilderState
    {
        // ═══════════════════════════════════════════════════════════════
        // EVENTS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Fired when selected work type changes.</summary>
        public event Action<WorkTypeDef> OnWorkTypeChanged;

        /// <summary>Fired when selected priority changes.</summary>
        public event Action<int> OnPriorityChanged;

        /// <summary>Fired when selected ruleset changes.</summary>
        public event Action<WorkAssignmentRuleset> OnRulesetChanged;

        /// <summary>Fired when any rule is modified, added, or removed.</summary>
        public event Action OnRulesModified;

        // ═══════════════════════════════════════════════════════════════
        // RULESET SELECTION
        // ═══════════════════════════════════════════════════════════════

        private WorkAssignmentRuleset _selectedRuleset;
        /// <summary>
        /// Currently selected ruleset being edited.
        /// </summary>
        public WorkAssignmentRuleset SelectedRuleset
        {
            get => _selectedRuleset;
            set
            {
                if (_selectedRuleset != value)
                {
                    _selectedRuleset = value;
                    // Reset downstream selections
                    SelectedWorkType = null;
                    SelectedPriority = -1;
                    SelectedRule = null;
                    SyncPriorityOrderFromRuleset();
                    OnRulesetChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// Whether the current ruleset is a default (non-editable) ruleset.
        /// </summary>
        public bool IsRulesetReadOnly => SelectedRuleset?.IsDefault ?? true;

        /// <summary>
        /// Current wizard step for the rule builder UI.
        /// </summary>
        public RuleBuilderStep CurrentStep { get; private set; } = RuleBuilderStep.SelectWorkType;

        /// <summary>
        /// When true, the work type selector shows only work types that already have rules configured.
        /// </summary>
        public bool ShowOnlyConfiguredWorkTypes { get; set; }

        // ═══════════════════════════════════════════════════════════════
        // COLUMN 1: WORK TYPE SELECTION
        // ═══════════════════════════════════════════════════════════════

        private WorkTypeDef _selectedWorkType;
        /// <summary>
        /// Currently selected work type (column 1).
        /// </summary>
        public WorkTypeDef SelectedWorkType
        {
            get => _selectedWorkType;
            set
            {
                if (_selectedWorkType != value)
                {
                    _selectedWorkType = value;
                    // Reset downstream selections
                    SelectedPriority = -1;
                    SelectedRule = null;
                    SelectFirstPriorityWithRules();
                    OnWorkTypeChanged?.Invoke(value);
                }
            }
        }

        /// <summary>Search filter for work type list.</summary>
        public string WorkTypeSearchFilter { get; set; } = "";

        // ═══════════════════════════════════════════════════════════════
        // COLUMN 2: PRIORITY SELECTION
        // ═══════════════════════════════════════════════════════════════

        private int _selectedPriority = -1;
        /// <summary>
        /// Currently selected priority level (column 2).
        /// -1 means no priority selected.
        /// </summary>
        public int SelectedPriority
        {
            get => _selectedPriority;
            set
            {
                if (_selectedPriority != value)
                {
                    _selectedPriority = value;
                    SelectedRule = null;
                    OnPriorityChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// Maximum priority level supported.
        /// Default is 4, but can be extended.
        /// </summary>
        public int MaxPriority { get; set; } = 4;

        /// <summary>
        /// Current UI ordering of priorities (0..MaxPriority). Defaults to 1..MaxPriority then 0 (disabled).
        /// </summary>
        public List<int> PriorityOrder { get; private set; } = new List<int>();

        // ═══════════════════════════════════════════════════════════════
        // COLUMN 3: RULE/CONDITION SELECTION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Currently selected rule for detailed editing (optional).
        /// </summary>
        public WorkAssignmentRule SelectedRule { get; set; }

        public RuleDragController DragController { get; private set; }

        public RuleBuilderState()
        {
            DragController = new RuleDragController(this);
        }

        // ═══════════════════════════════════════════════════════════════
        // COMPUTED QUERIES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets all rules that apply to a specific work type.
        /// </summary>
        public List<WorkAssignmentRule> GetRulesForWorkType(WorkTypeDef workType)
        {
            if (SelectedRuleset?.Rules == null || workType == null)
                return new List<WorkAssignmentRule>();

            return SelectedRuleset.Rules
                .Where(r => RuleAppliesToWorkType(r, workType))
                .ToList();
        }

        /// <summary>
        /// Gets all rules for a specific (WorkType, Priority) pair.
        /// This is the primary grouping for the new UI.
        /// </summary>
        public List<WorkAssignmentRule> GetRulesFor(WorkTypeDef workType, int priority)
        {
            if (SelectedRuleset?.Rules == null || workType == null)
                return new List<WorkAssignmentRule>();

            return SelectedRuleset.Rules
                .Where(r => RuleAppliesToWorkType(r, workType) &&
                            r.Parameters?.Priority == priority)
                .ToList();
        }

        /// <summary>
        /// Gets rules for currently selected work type and priority.
        /// </summary>
        public List<WorkAssignmentRule> CurrentRules
        {
            get
            {
                if (SelectedWorkType == null || SelectedPriority < 0)
                    return new List<WorkAssignmentRule>();

                return GetRulesFor(SelectedWorkType, SelectedPriority);
            }
        }

        /// <summary>
        /// All rules that apply to the currently selected work type.
        /// </summary>
        public List<WorkAssignmentRule> RulesForSelectedWorkType =>
            SelectedWorkType == null ? new List<WorkAssignmentRule>() : GetRulesForWorkType(SelectedWorkType);

        /// <summary>
        /// Gets the count of rules for each priority level for the selected work type.
        /// Key: priority, Value: rule count
        /// </summary>
        public Dictionary<int, int> GetPriorityRuleCounts(WorkTypeDef workType)
        {
            var counts = new Dictionary<int, int>();
            if (SelectedRuleset?.Rules == null)
                return counts;

            foreach (var rule in SelectedRuleset.Rules)
            {
                if (RuleAppliesToWorkType(rule, workType) && rule.Parameters?.Priority >= 0)
                {
                    int priority = rule.Parameters.Priority;
                    counts[priority] = (counts.ContainsKey(priority) ? counts[priority] : 0) + 1;
                }
            }
            return counts;
        }

        /// <summary>
        /// Gets all work types that have at least one rule configured.
        /// </summary>
        public HashSet<WorkTypeDef> ConfiguredWorkTypes
        {
            get
            {
                if (SelectedRuleset?.Rules == null)
                    return new HashSet<WorkTypeDef>();

                var result = new HashSet<WorkTypeDef>();

                foreach (var rule in SelectedRuleset.Rules)
                {
                    var workType = ResolveWorkType(rule);
                    if (workType != null)
                    {
                        result.Add(workType);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Gets the total rule count for a work type across all priorities.
        /// </summary>
        public int GetTotalRuleCount(WorkTypeDef workType)
        {
            return GetRulesForWorkType(workType).Count;
        }

        // ═══════════════════════════════════════════════════════════════
        // RULE MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a new rule for the currently selected work type and priority.
        /// </summary>
        public WorkAssignmentRule CreateRule()
        {
            if (SelectedRuleset == null || SelectedWorkType == null || SelectedPriority < 0)
                return null;

            if (IsRulesetReadOnly)
                return null;

            var parameters = new WorkAssignmentParameters(
                ruleName: GenerateRuleName(),
                priority: SelectedPriority,
                worktype: SelectedWorkType
            );

            var rule = new WorkAssignmentRule(parameters, SelectedWorkType);

            SelectedRuleset.Rules.Add(rule);
            SelectedRule = rule;
            NotifyRulesModified();

            return rule;
        }

        /// <summary>
        /// Deletes the specified rule from the current ruleset.
        /// </summary>
        public bool DeleteRule(WorkAssignmentRule rule)
        {
            if (SelectedRuleset?.Rules == null || rule == null)
                return false;

            if (IsRulesetReadOnly)
                return false;

            bool removed = SelectedRuleset.Rules.Remove(rule);

            if (removed)
            {
                if (SelectedRule == rule)
                {
                    SelectedRule = null;
                }

                NotifyRulesModified();
            }

            return removed;
        }

        /// <summary>
        /// Duplicates the specified rule.
        /// </summary>
        public WorkAssignmentRule DuplicateRule(WorkAssignmentRule rule)
        {
            if (SelectedRuleset == null || rule == null || IsRulesetReadOnly)
                return null;

            var copy = rule.Copy();
            copy.Name = copy.Name + " (Copy)";

            SelectedRuleset.Rules.Add(copy);
            SelectedRule = copy;
            NotifyRulesModified();

            return copy;
        }

        /// <summary>
        /// Notifies listeners that rules have been modified.
        /// Call after any rule changes.
        /// </summary>
        public void NotifyRulesModified()
        {
            OnRulesModified?.Invoke();
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Checks if a rule applies to a specific work type.
        /// Rules with no worktype set apply to ALL work types.
        /// </summary>
        public bool RuleAppliesToWorkType(WorkAssignmentRule rule, WorkTypeDef workType)
        {
            if (rule?.Parameters == null || workType == null)
                return false;

            // Rule with no specific worktype = applies to all
            if (rule.Parameters.Worktype == null &&
                string.IsNullOrEmpty(rule.Parameters.WorktypeString))
            {
                return true;
            }

            // Direct match
            if (rule.Parameters.Worktype == workType)
                return true;

            // String match (for unresolved defs)
            if (!string.IsNullOrEmpty(rule.Parameters.WorktypeString) &&
                rule.Parameters.WorktypeString == workType.defName)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves the work type from a rule's parameters.
        /// </summary>
        private WorkTypeDef ResolveWorkType(WorkAssignmentRule rule)
        {
            if (rule?.Parameters == null)
                return null;

            if (rule.Parameters.Worktype != null)
                return rule.Parameters.Worktype;

            if (!string.IsNullOrEmpty(rule.Parameters.WorktypeString))
            {
                return DefDatabase<WorkTypeDef>.GetNamedSilentFail(rule.Parameters.WorktypeString);
            }

            return null;
        }

        /// <summary>
        /// Generates a default name for a new rule.
        /// </summary>
        private string GenerateRuleName()
        {
            int count = CurrentRules.Count + 1;
            return $"Rule {count}";
        }

        /// <summary>
        /// Resets all selections to initial state.
        /// </summary>
        public void Reset()
        {
            SelectedWorkType = null;
            SelectedPriority = -1;
            SelectedRule = null;
            WorkTypeSearchFilter = "";
            CurrentStep = RuleBuilderStep.SelectWorkType;
            EnsurePriorityOrder();
        }

        /// <summary>
        /// Advance the UI to the specified step.
        /// </summary>
        public void NavigateTo(RuleBuilderStep step)
        {
            CurrentStep = step;
        }

        /// <summary>
        /// Navigate back to work type selection and clear downstream selections.
        /// </summary>
        public void NavigateBack()
        {
            CurrentStep = RuleBuilderStep.SelectWorkType;
            SelectedPriority = -1;
            SelectedRule = null;
        }

        /// <summary>
        /// Ensures PriorityOrder is initialized in the desired default order (1..MaxPriority, then 0).
        /// </summary>
        public void EnsurePriorityOrder()
        {
            MaxPriority = Mathf.Max(1, BetterWorkTabMod.Settings?.maxPriorityInt ?? MaxPriority);

            if ((PriorityOrder == null || PriorityOrder.Count == 0) &&
                SelectedRuleset?.PriorityOrder != null &&
                SelectedRuleset.PriorityOrder.Count > 0)
            {
                PriorityOrder = SelectedRuleset.PriorityOrder.ToList();
            }

            if (PriorityOrder == null || PriorityOrder.Count == 0)
            {
                PriorityOrder = Enumerable.Range(1, MaxPriority).ToList();
                if (!PriorityOrder.Contains(0))
                {
                    PriorityOrder.Add(0);
                }
            }

            // Make sure any new priorities or missing disabled are added.
            for (int p = 0; p <= MaxPriority; p++)
            {
                if (!PriorityOrder.Contains(p))
                {
                    PriorityOrder.Add(p);
                }
            }

            if (SelectedRuleset != null)
            {
                SelectedRuleset.PriorityOrder = PriorityOrder.ToList();
            }
        }

        /// <summary>
        /// Moves a priority to a new index within the UI order.
        /// </summary>
        public void MovePriority(int priority, int targetIndex)
        {
            EnsurePriorityOrder();
            int currentIndex = PriorityOrder.IndexOf(priority);
            if (currentIndex < 0 || targetIndex < 0 || targetIndex > PriorityOrder.Count)
                return;

            PriorityOrder.RemoveAt(currentIndex);
            targetIndex = Mathf.Clamp(targetIndex, 0, PriorityOrder.Count);
            PriorityOrder.Insert(targetIndex, priority);

            if (SelectedRuleset != null)
            {
                SelectedRuleset.PriorityOrder = PriorityOrder.ToList();
            }
        }

        /// <summary>
        /// Selects the first priority (in UI order) that has at least one rule for the current work type.
        /// Falls back to the first priority in order if none have rules.
        /// </summary>
        public void SelectFirstPriorityWithRules()
        {
            EnsurePriorityOrder();
            if (SelectedRuleset?.Rules == null || SelectedWorkType == null)
            {
                return;
            }

            var rules = SelectedRuleset.Rules.Where(r => RuleAppliesToWorkType(r, SelectedWorkType)).ToList();
            foreach (var p in PriorityOrder)
            {
                if (rules.Any(r => r.Parameters?.Priority == p))
                {
                    SelectedPriority = p;
                    return;
                }
            }

            // Fallback to the first available priority
            if (PriorityOrder.Count > 0)
            {
                SelectedPriority = PriorityOrder[0];
            }
        }

        private void SyncPriorityOrderFromRuleset()
        {
            if (SelectedRuleset?.PriorityOrder != null && SelectedRuleset.PriorityOrder.Count > 0)
            {
                PriorityOrder = SelectedRuleset.PriorityOrder.ToList();
            }

            EnsurePriorityOrder();
        }
    }
}

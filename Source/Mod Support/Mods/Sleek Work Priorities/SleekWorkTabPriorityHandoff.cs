using System;
using System.Collections.Generic;
using System.Linq;
using Better_Work_Tab.Features;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Better_Work_Tab.Features.WorkGiverReassignments;
using Better_Work_Tab.ModSupport;
using RimWorld;
using Verse;

namespace Better_Work_Tab.ModSupport.Mods.SleekWorkPriorities
{
    /// <summary>
    /// Translates Sleek's child ranks into BWT's child-only persistence model.
    /// Parent Pawn_WorkSettings values and BWT hourly schedules are deliberately not read or written.
    /// </summary>
    internal static class SleekWorkTabPriorityHandoff
    {
        private sealed class RankedWorkGiver
        {
            internal WorkGiverDef Def;
            internal int Rank;
            internal int OriginalIndex;
        }

        internal static int ImportChildRanksToBetterWorkTab()
        {
            if (!SleekWorkTabGateway.IsPresent || Current.Game == null)
            {
                return 0;
            }

            int changed = 0;
            try
            {
                using (ExternalPriorityMirror.Suspend())
                {
                    List<WorkGiverDef> workGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading;
                    foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
                    {
                        if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
                        {
                            continue;
                        }

                        foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                        {
                            if (workType == null || pawn.WorkTypeIsDisabled(workType))
                            {
                                continue;
                            }

                            var ranked = new List<RankedWorkGiver>();
                            for (int index = 0; index < workGivers.Count; index++)
                            {
                                WorkGiverDef workGiver = workGivers[index];
                                if (workGiver?.workType != workType ||
                                    !SleekWorkTabGateway.TryGetSleekWorkGiverOverride(
                                        pawn,
                                        workGiver,
                                        out int rank) ||
                                    rank < 0)
                                {
                                    continue;
                                }

                                if (rank == 0)
                                {
                                    WorkGiverReassignmentManager.SetPawnOverrideSynced(
                                        pawn.thingIDNumber,
                                        workGiver.defName,
                                        WorkPrioritySystem.DisabledPriority);
                                    changed++;
                                    continue;
                                }

                                ranked.Add(new RankedWorkGiver
                                {
                                    Def = workGiver,
                                    Rank = rank,
                                    OriginalIndex = index
                                });
                            }

                            if (ranked.Count == 0)
                            {
                                continue;
                            }

                            ranked.Sort((left, right) =>
                            {
                                int result = left.Rank.CompareTo(right.Rank);
                                return result != 0
                                    ? result
                                    : left.OriginalIndex.CompareTo(right.OriginalIndex);
                            });

                            var orderedNames = ranked
                                .Select(item => item.Def.defName)
                                .ToList();
                            for (int index = 0; index < ranked.Count; index++)
                            {
                                // A Sleek rank is an order, not a BWT priority. Clear an old BWT
                                // child override for this explicitly ranked giver so stale data
                                // cannot silently disable or reprioritize the translated order.
                                WorkGiverReassignmentManager.ClearPawnOverrideSynced(
                                    pawn.thingIDNumber,
                                    ranked[index].Def.defName);
                            }

                            WorkGiverReassignmentManager.SetPawnWorkGiverOrderSynced(
                                pawn.thingIDNumber,
                                workType.defName,
                                orderedNames);
                            changed++;
                        }
                    }
                }

                if (changed > 0)
                {
                    WorkGiverReassignmentManager.InvalidateCaches();
                    WorkExecutionOrder.MarkAllPawnsWorkGiversDirty();
                    MainTabWindowUtility.NotifyAllPawnTables_PawnsChanged();
                }

                return changed;
            }
            catch (Exception exception)
            {
                BetterWorkTabMod.DebugLog(
                    "[SleekWorkTab] Child-rank handoff failed: " + exception.GetBaseException().Message,
                    DebugFeature.ModSupport);
                return changed;
            }
        }
    }
}

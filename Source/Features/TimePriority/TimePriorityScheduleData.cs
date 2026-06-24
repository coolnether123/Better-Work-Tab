using System.Collections.Generic;
using Better_Work_Tab.Features.RaisedPriorityMaximum;
using Verse;

namespace Better_Work_Tab.Features.TimePriority
{
    public sealed class TimePriorityScheduleData : IExposable
    {
        public string Key;
        public int PawnId;
        public TimePriorityTargetKind Kind;
        public string WorkTypeDefName;
        public string TargetDefName;
        public List<int> HourlyPriorities = new List<int>(TimePriorityService.HoursPerDay);

        public void ExposeData()
        {
            Scribe_Values.Look(ref Key, "key");
            Scribe_Values.Look(ref PawnId, "pawnId", TimePriorityTarget.GlobalPawnId);
            Scribe_Values.Look(ref Kind, "kind", TimePriorityTargetKind.WorkType);
            Scribe_Values.Look(ref WorkTypeDefName, "workTypeDefName");
            Scribe_Values.Look(ref TargetDefName, "targetDefName");
            Scribe_Collections.Look(ref HourlyPriorities, "hourlyPriorities", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureValid();
            }
        }

        internal void EnsureValid()
        {
            if (HourlyPriorities == null)
            {
                HourlyPriorities = new List<int>(TimePriorityService.HoursPerDay);
            }

            while (HourlyPriorities.Count < TimePriorityService.HoursPerDay)
            {
                HourlyPriorities.Add(WorkPrioritySystem.GetDefaultEnabledPriority());
            }

            if (HourlyPriorities.Count > TimePriorityService.HoursPerDay)
            {
                HourlyPriorities.RemoveRange(TimePriorityService.HoursPerDay, HourlyPriorities.Count - TimePriorityService.HoursPerDay);
            }

            for (int i = 0; i < HourlyPriorities.Count; i++)
            {
                HourlyPriorities[i] = WorkPrioritySystem.ClampPriority(HourlyPriorities[i]);
            }

            Key = TimePriorityService.BuildKey(PawnId, Kind, WorkTypeDefName, TargetDefName);
        }
    }
}

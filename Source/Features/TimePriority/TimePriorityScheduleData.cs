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
            Better_Work_Tab.ScribeCompat.LookValue(ref Key, "key");
            Better_Work_Tab.ScribeCompat.LookValue(ref PawnId, "pawnId", TimePriorityTarget.GlobalPawnId);
            Better_Work_Tab.ScribeCompat.LookValue(ref Kind, "kind", TimePriorityTargetKind.WorkType);
            Better_Work_Tab.ScribeCompat.LookValue(ref WorkTypeDefName, "workTypeDefName");
            Better_Work_Tab.ScribeCompat.LookValue(ref TargetDefName, "targetDefName");
            Better_Work_Tab.ScribeCompat.LookCollection(ref HourlyPriorities, "hourlyPriorities", LookMode.Value);

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

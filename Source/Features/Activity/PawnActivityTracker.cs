using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Better_Work_Tab.Features.Activity
{
    /// <summary>
    /// Lightweight tracker that records the most recent jobs each pawn has performed so we can render
    /// a simple heat-map style overlay on the work tab.
    /// </summary>
    public sealed class PawnActivityTracker
    {
        private const int MaxSamplesPerPawn = 6;
        private static readonly Color IdleColor = new Color(0.45f, 0.45f, 0.45f, 0.35f);

        private readonly Dictionary<Pawn, ActivityState> _states = new Dictionary<Pawn, ActivityState>();
        private readonly Dictionary<JobDef, Color> _jobColors = new Dictionary<JobDef, Color>();
        private readonly HashSet<Pawn> _visibleScratch = new HashSet<Pawn>();

        public static PawnActivityTracker Instance { get; } = new PawnActivityTracker();

        private PawnActivityTracker()
        {
        }

        public ActivitySnapshot Observe(Pawn pawn)
        {
            if (pawn == null || pawn.DestroyedOrNull())
            {
                return ActivitySnapshot.Empty;
            }

            int ticks = Find.TickManager?.TicksGame ?? 0;
            ActivityState state = GetOrCreateState(pawn);
            state.LastSeenTick = ticks;
            UpdateState(state, pawn, ticks);
            return state.BuildSnapshot(ticks);
        }

        public void PruneInvisible(IEnumerable<Pawn> visiblePawns)
        {
            if (_states.Count == 0)
            {
                return;
            }

            _visibleScratch.Clear();
            if (visiblePawns != null)
            {
                foreach (var pawn in visiblePawns)
                {
                    if (pawn != null && !pawn.DestroyedOrNull())
                    {
                        _visibleScratch.Add(pawn);
                    }
                }
            }

            if (_visibleScratch.Count == 0)
            {
                _states.Clear();
                return;
            }

            var keys = new List<Pawn>(_states.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                Pawn pawn = keys[i];
                if (!_visibleScratch.Contains(pawn) || pawn.DestroyedOrNull())
                {
                    _states.Remove(pawn);
                }
            }

            _visibleScratch.Clear();
        }

        private ActivityState GetOrCreateState(Pawn pawn)
        {
            if (!_states.TryGetValue(pawn, out var state))
            {
                state = new ActivityState(pawn);
                _states[pawn] = state;
            }
            return state;
        }

        private void UpdateState(ActivityState state, Pawn pawn, int ticks)
        {
            JobDef currentJob = pawn.CurJobDef;
            TaggedString jobReport = pawn.jobs?.curDriver?.GetReport() ?? TaggedString.Empty;
            TaggedString fallbackLabel = currentJob != null
                ? (TaggedString)currentJob.LabelCap
                : "Idle".Translate();
            TaggedString labelSource = jobReport.NullOrEmpty() ? fallbackLabel : jobReport;
            string label = labelSource.Resolve();

            if (state.ActiveSample == null)
            {
                state.ActiveSample = CreateSample(currentJob, label, ticks);
                return;
            }

            bool jobChanged = state.ActiveSample.Job != currentJob || state.ActiveSample.Label != label;
            if (jobChanged)
            {
                state.CommitActiveSample(ticks);
                state.ActiveSample = CreateSample(currentJob, label, ticks);
            }
            else
            {
                state.ActiveSample.EndTick = ticks;
            }
        }

        private ActivitySample CreateSample(JobDef job, string label, int startTick)
        {
            return new ActivitySample
            {
                Job = job,
                Label = label ?? string.Empty,
                StartTick = startTick,
                EndTick = startTick,
                Color = ResolveColor(job),
                IsActive = true
            };
        }

        private Color ResolveColor(JobDef job)
        {
            if (job == null)
            {
                return IdleColor;
            }

            if (_jobColors.TryGetValue(job, out var color))
            {
                return color;
            }

            Rand.PushState(job.shortHash);
            float r = Mathf.Lerp(0.2f, 0.9f, Rand.Value);
            float g = Mathf.Lerp(0.2f, 0.9f, Rand.Value);
            float b = Mathf.Lerp(0.2f, 0.9f, Rand.Value);
            Rand.PopState();

            color = new Color(r, g, b, 0.5f);
            _jobColors[job] = color;
            return color;
        }

        private sealed class ActivityState
        {
            public readonly Pawn Pawn;
            public readonly List<ActivitySample> History = new List<ActivitySample>(MaxSamplesPerPawn);
            public readonly List<ActivitySegment> RenderBuffer = new List<ActivitySegment>(MaxSamplesPerPawn + 1);
            public ActivitySample ActiveSample;
            public int LastSeenTick;

            public ActivityState(Pawn pawn)
            {
                Pawn = pawn;
            }

            public void CommitActiveSample(int ticks)
            {
                if (ActiveSample == null)
                {
                    return;
                }

                ActiveSample.EndTick = ticks;
                ActiveSample.IsActive = false;
                if (History.Count >= MaxSamplesPerPawn)
                {
                    History.RemoveAt(0);
                }
                History.Add(ActiveSample);
                ActiveSample = null;
            }

            public ActivitySnapshot BuildSnapshot(int currentTick)
            {
                RenderBuffer.Clear();
                float total = 0f;

                for (int i = 0; i < History.Count; i++)
                {
                    var sample = History[i];
                    float duration = sample.Duration;
                    total += duration;
                    RenderBuffer.Add(new ActivitySegment(sample.Color, duration, false, sample.Label));
                }

                if (ActiveSample != null)
                {
                    ActiveSample.EndTick = currentTick;
                    float duration = ActiveSample.Duration;
                    total += duration;
                    RenderBuffer.Add(new ActivitySegment(ActiveSample.Color, duration, true, ActiveSample.Label));
                }

                IReadOnlyList<ActivitySegment> segments = RenderBuffer.Count == 0
                    ? Array.Empty<ActivitySegment>()
                    : RenderBuffer.ToArray();
                var label = ActiveSample != null ? ActiveSample.Label : string.Empty;
                var color = ActiveSample != null ? ActiveSample.Color : IdleColor;

                return new ActivitySnapshot(segments, total, label, color);
            }
        }

        private sealed class ActivitySample
        {
            public JobDef Job;
            public string Label;
            public int StartTick;
            public int EndTick;
            public Color Color;
            public bool IsActive;

            public float Duration => Mathf.Max(1f, EndTick - StartTick);
        }
    }

    public readonly struct ActivitySnapshot
    {
        public static readonly ActivitySnapshot Empty = new ActivitySnapshot(Array.Empty<ActivitySegment>(), 0f, string.Empty, Color.clear);

        public ActivitySnapshot(IReadOnlyList<ActivitySegment> segments, float totalTicks, string currentLabel, Color currentColor)
        {
            Segments = segments;
            TotalTicks = totalTicks;
            CurrentLabel = currentLabel ?? string.Empty;
            CurrentColor = currentColor;
        }

        public IReadOnlyList<ActivitySegment> Segments { get; }
        public float TotalTicks { get; }
        public string CurrentLabel { get; }
        public Color CurrentColor { get; }
        public bool HasData => Segments != null && Segments.Count > 0 && TotalTicks > 0f;
    }

    public readonly struct ActivitySegment
    {
        public ActivitySegment(Color color, float duration, bool isActive, string label)
        {
            Color = color;
            Duration = duration;
            IsActive = isActive;
            Label = label ?? string.Empty;
        }

        public Color Color { get; }
        public float Duration { get; }
        public bool IsActive { get; }
        public string Label { get; }
    }
}

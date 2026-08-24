using System;
using Better_Work_Tab.UI.Schedule;

namespace Better_Work_Tab.Features.TimePriority
{
    internal sealed class TimePriorityScheduleSnapshot
    {
        internal TimePriorityScheduleSnapshot(string label, TimePriorityScheduleValue value)
        {
            Label = string.IsNullOrEmpty(label) ? "time priorities" : label;
            Value = value ?? TimePriorityScheduleValue.AllLinked;
        }

        internal string Label { get; }
        internal TimePriorityScheduleValue Value { get; }
    }

    internal static class TimePriorityScheduleClipboard
    {
        private static TimePriorityScheduleSnapshot _snapshot;

        internal static bool HasSnapshot => _snapshot != null;

        internal static TimePriorityScheduleSnapshot CopyFrom(
            TimePriorityTarget target,
            int fallbackPriority,
            string label)
        {
            TimePriorityScheduleValue schedule = ScheduleProjection.ReadSchedule(
                target,
                fallbackPriority);
            _snapshot = new TimePriorityScheduleSnapshot(
                label,
                schedule);
            return _snapshot;
        }

        internal static bool TryGetSnapshot(out TimePriorityScheduleSnapshot snapshot)
        {
            snapshot = _snapshot;
            return snapshot != null;
        }
    }

    internal enum TimePriorityScheduleTransferKind
    {
        Copy,
        Paste
    }

    internal static class TimePriorityScheduleTransferFeedback
    {
        private const float TotalSeconds = 0.72f;
        private const float CellPulseSeconds = 0.20f;
        private const float HourDelaySeconds = 0.018f;

        private static TransferAnimation _animation;
        private static bool _closeRequested;

        internal static void ResetForWindowClose()
        {
            _animation = null;
            _closeRequested = false;
        }

        internal static void StartCopy(TimePriorityTarget target)
        {
            _animation = new TransferAnimation(
                target.Key,
                TimePriorityScheduleTransferKind.Copy,
                null,
                null,
                false,
                UnityEngine.Time.realtimeSinceStartup);
        }

        internal static void StartPaste(
            TimePriorityTarget target,
            int[] beforePriorities,
            int[] afterPriorities,
            bool closeWhenComplete)
        {
            _animation = new TransferAnimation(
                target.Key,
                TimePriorityScheduleTransferKind.Paste,
                beforePriorities,
                afterPriorities,
                closeWhenComplete,
                UnityEngine.Time.realtimeSinceStartup);
        }

        internal static bool TryGetDisplayPriority(TimePriorityTarget target, int hour, int currentPriority, out int displayPriority)
        {
            displayPriority = currentPriority;
            if (!IsActiveFor(target) ||
                _animation.Kind != TimePriorityScheduleTransferKind.Paste ||
                _animation.BeforePriorities == null ||
                _animation.AfterPriorities == null)
            {
                return false;
            }

            hour = UnityEngine.Mathf.Clamp(hour, 0, TimePriorityService.HoursPerDay - 1);
            displayPriority = HourWaveProgress(hour) >= 0.5f
                ? _animation.AfterPriorities[hour]
                : _animation.BeforePriorities[hour];
            return true;
        }

        internal static bool TryGetCellPulse(TimePriorityTarget target, int hour, out float scale, out float alpha)
        {
            scale = 1f;
            alpha = 0f;
            if (!IsActiveFor(target))
            {
                return false;
            }

            float pulse = UnityEngine.Mathf.Sin(UnityEngine.Mathf.Clamp01(HourWaveProgress(hour)) * UnityEngine.Mathf.PI);
            if (pulse <= 0.001f)
            {
                return false;
            }

            scale = 1f + 0.10f * pulse;
            alpha = (_animation.Kind == TimePriorityScheduleTransferKind.Copy ? 0.22f : 0.30f) * pulse;
            return true;
        }

        internal static bool ConsumeCloseRequest()
        {
            Tick();
            if (!_closeRequested)
            {
                return false;
            }

            _closeRequested = false;
            return true;
        }

        internal static bool Tick()
        {
            if (_animation == null)
            {
                return false;
            }

            if (UnityEngine.Time.realtimeSinceStartup - _animation.StartedAt < TotalSeconds)
            {
                return true;
            }

            if (_animation.CloseWhenComplete)
            {
                _closeRequested = true;
            }

            _animation = null;
            return true;
        }

        private static bool IsActiveFor(TimePriorityTarget target)
        {
            Tick();
            return _animation != null &&
                string.Equals(_animation.TargetKey, target.Key, StringComparison.Ordinal);
        }

        private static float HourWaveProgress(int hour)
        {
            hour = UnityEngine.Mathf.Clamp(hour, 0, TimePriorityService.HoursPerDay - 1);
            float age = UnityEngine.Time.realtimeSinceStartup - _animation.StartedAt;
            float localAge = age - hour * HourDelaySeconds;
            return UnityEngine.Mathf.Clamp01(localAge / CellPulseSeconds);
        }

        private sealed class TransferAnimation
        {
            internal TransferAnimation(
                string targetKey,
                TimePriorityScheduleTransferKind kind,
                int[] beforePriorities,
                int[] afterPriorities,
                bool closeWhenComplete,
                float startedAt)
            {
                TargetKey = targetKey;
                Kind = kind;
                BeforePriorities = Normalize(beforePriorities);
                AfterPriorities = Normalize(afterPriorities);
                CloseWhenComplete = closeWhenComplete;
                StartedAt = startedAt;
            }

            internal string TargetKey { get; }

            internal TimePriorityScheduleTransferKind Kind { get; }

            internal int[] BeforePriorities { get; }

            internal int[] AfterPriorities { get; }

            internal bool CloseWhenComplete { get; }

            internal float StartedAt { get; }

            private static int[] Normalize(int[] priorities)
            {
                if (priorities == null)
                {
                    return null;
                }

                var normalized = new int[TimePriorityService.HoursPerDay];
                for (int i = 0; i < normalized.Length; i++)
                {
                    normalized[i] = priorities != null && i < priorities.Length
                        ? priorities[i]
                        : 0;
                }

                return normalized;
            }
        }
    }
}

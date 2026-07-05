using UnityEngine;

namespace Spine.UI.Animation
{
    public static class SpineEasing
    {
        public static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        public static float Move01(float current, float target, float durationSeconds, bool animated)
        {
            if (!animated || durationSeconds <= 0f)
            {
                return Mathf.Clamp01(target);
            }

            float step = Mathf.Clamp01(Time.unscaledDeltaTime / durationSeconds);
            return Mathf.MoveTowards(Mathf.Clamp01(current), Mathf.Clamp01(target), step);
        }
    }
}

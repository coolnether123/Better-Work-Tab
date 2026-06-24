using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer.Data;
using UnityEngine;

namespace Better_Work_Tab.Features.Dividers
{
    /// <summary>
    /// Transient UI state for divider section collapse/expand animation. The divider's
    /// saved collapsed value remains authoritative; this only controls visual row height.
    /// </summary>
    internal static class DividerCollapseAnimationState
    {
        private const float AnimationSeconds = 0.24f;
        private static readonly Dictionary<PawnDivider, Animation> Active = new Dictionary<PawnDivider, Animation>();
        private static int _version;

        internal static int LayoutSignature
        {
            get
            {
                if (Active.Count == 0)
                {
                    return 0;
                }

                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + _version;
                    foreach (var pair in Active)
                    {
                        hash = hash * 31 + pair.Key.DisplayOrder;
                        hash = hash * 31 + (pair.Value.Collapsing ? 1 : 0);
                        hash = hash * 31 + Mathf.RoundToInt(GetRawProgress(pair.Value) * 24f);
                    }

                    return hash;
                }
            }
        }

        internal static void Start(PawnDivider divider, bool collapsing)
        {
            if (divider == null)
            {
                return;
            }

            if (!AnimationsEnabled)
            {
                Active.Remove(divider);
                return;
            }

            Active[divider] = new Animation(collapsing, Time.realtimeSinceStartup);
            _version++;
        }

        internal static bool Tick()
        {
            if (Active.Count == 0)
            {
                return false;
            }

            if (!AnimationsEnabled)
            {
                Active.Clear();
                _version++;
                return true;
            }

            bool removed = false;
            var expired = new List<PawnDivider>();
            foreach (var pair in Active)
            {
                if (GetRawProgress(pair.Value) >= 1f)
                {
                    expired.Add(pair.Key);
                }
            }

            for (int i = 0; i < expired.Count; i++)
            {
                Active.Remove(expired[i]);
                removed = true;
            }

            if (removed)
            {
                _version++;
            }

            // Active collapse/expand animations change row heights every repaint.
            // The layout and PawnTable cached size must follow those frames instead
            // of waiting until the animation expires, or the Work tab jumps/flickers.
            return true;
        }

        internal static bool ShouldRenderCollapsedSection(PawnDivider divider)
        {
            return divider != null &&
                divider.IsCollapsed &&
                Active.TryGetValue(divider, out Animation animation) &&
                animation.Collapsing &&
                GetRawProgress(animation) < 1f;
        }

        internal static bool TryGetSectionHeightMultiplier(PawnDivider divider, out float multiplier)
        {
            multiplier = 1f;
            if (divider == null || !Active.TryGetValue(divider, out Animation animation))
            {
                return false;
            }

            float eased = Mathf.SmoothStep(0f, 1f, GetRawProgress(animation));
            multiplier = animation.Collapsing ? 1f - eased : eased;
            multiplier = Mathf.Clamp01(multiplier);
            return true;
        }

        private static float GetRawProgress(Animation animation)
        {
            return Mathf.Clamp01((Time.realtimeSinceStartup - animation.StartedAt) / AnimationSeconds);
        }

        private static bool AnimationsEnabled =>
            BetterWorkTabMod.Settings?.enableDividerAnimations ?? DefaultSettings.enableDividerAnimations;

        private readonly struct Animation
        {
            internal readonly bool Collapsing;
            internal readonly float StartedAt;

            internal Animation(bool collapsing, float startedAt)
            {
                Collapsing = collapsing;
                StartedAt = startedAt;
            }
        }
    }
}

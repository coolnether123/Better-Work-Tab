using System.Collections.Generic;
using Better_Work_Tab.PawnOrganizer.Data;
using UnityEngine;

namespace Better_Work_Tab.Features.Dividers
{
    /// <summary>
    /// Transient reveal animation for newly inserted dividers. This only affects row
    /// height while the divider is appearing; the saved divider data stays unchanged.
    /// </summary>
    internal static class DividerInsertionAnimationState
    {
        private const float AnimationSeconds = 0.22f;
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
                        hash = hash * 31 + (pair.Value.RevealUp ? 1 : 0);
                        hash = hash * 31 + Mathf.RoundToInt(GetProgress(pair.Value) * 24f);
                    }

                    return hash;
                }
            }
        }

        internal static void Start(PawnDivider divider, bool revealUp)
        {
            if (divider == null)
            {
                return;
            }

            Active[divider] = new Animation(revealUp, Time.realtimeSinceStartup);
            _version++;
        }

        internal static bool Tick()
        {
            if (Active.Count == 0)
            {
                return false;
            }

            bool removed = false;
            var expired = new List<PawnDivider>();
            foreach (var pair in Active)
            {
                if (GetProgress(pair.Value) >= 1f)
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

            return removed;
        }

        internal static bool TryGetHeightMultiplier(PawnDivider divider, out float multiplier)
        {
            multiplier = 1f;
            if (divider == null || !Active.TryGetValue(divider, out Animation animation))
            {
                return false;
            }

            multiplier = Mathf.SmoothStep(0f, 1f, GetProgress(animation));
            return true;
        }

        internal static bool TryGetRevealUp(PawnDivider divider, out bool revealUp)
        {
            revealUp = false;
            if (divider == null || !Active.TryGetValue(divider, out Animation animation))
            {
                return false;
            }

            revealUp = animation.RevealUp;
            return true;
        }

        private static float GetProgress(Animation animation)
        {
            return Mathf.Clamp01((Time.realtimeSinceStartup - animation.StartedAt) / AnimationSeconds);
        }

        private readonly struct Animation
        {
            internal readonly bool RevealUp;
            internal readonly float StartedAt;

            internal Animation(bool revealUp, float startedAt)
            {
                RevealUp = revealUp;
                StartedAt = startedAt;
            }
        }
    }
}

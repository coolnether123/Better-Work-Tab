using UnityEngine;

namespace Spine.UI.Layout
{
    public readonly struct SpineLayoutTrack
    {
        private SpineLayoutTrack(float fixedSize, float weight, float minSize, bool flexible)
        {
            FixedSize = fixedSize;
            Weight = weight;
            MinSize = minSize;
            Flexible = flexible;
        }

        public float FixedSize { get; }
        public float Weight { get; }
        public float MinSize { get; }
        public bool Flexible { get; }

        public static SpineLayoutTrack Fixed(float size)
        {
            return new SpineLayoutTrack(Mathf.Max(0f, size), 0f, 0f, false);
        }

        public static SpineLayoutTrack Flex(float weight = 1f, float minSize = 0f)
        {
            return new SpineLayoutTrack(0f, Mathf.Max(0.0001f, weight), Mathf.Max(0f, minSize), true);
        }
    }
}

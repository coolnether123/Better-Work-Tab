using UnityEngine;

namespace Spine.UI.Layout
{
    public static class SpineRectLayout
    {
        public static Rect[] Horizontal(Rect rect, float gap, params SpineLayoutTrack[] tracks)
        {
            return Build(rect, gap, horizontal: true, tracks: tracks);
        }

        public static Rect[] Vertical(Rect rect, float gap, params SpineLayoutTrack[] tracks)
        {
            return Build(rect, gap, horizontal: false, tracks: tracks);
        }

        public static Rect WithHeight(Rect rect, float height, out Rect remainder, float gap = 0f)
        {
            height = Mathf.Clamp(height, 0f, rect.height);
            Rect top = new Rect(rect.x, rect.y, rect.width, height);
            float nextY = top.yMax + gap;
            remainder = new Rect(rect.x, nextY, rect.width, Mathf.Max(0f, rect.yMax - nextY));
            return top;
        }

        public static Rect WithWidth(Rect rect, float width, out Rect remainder, float gap = 0f)
        {
            width = Mathf.Clamp(width, 0f, rect.width);
            Rect left = new Rect(rect.x, rect.y, width, rect.height);
            float nextX = left.xMax + gap;
            remainder = new Rect(nextX, rect.y, Mathf.Max(0f, rect.xMax - nextX), rect.height);
            return left;
        }

        public static Rect Pad(Rect rect, float horizontal, float vertical)
        {
            return new Rect(
                rect.x + horizontal,
                rect.y + vertical,
                Mathf.Max(0f, rect.width - horizontal * 2f),
                Mathf.Max(0f, rect.height - vertical * 2f));
        }

        private static Rect[] Build(Rect rect, float gap, bool horizontal, SpineLayoutTrack[] tracks)
        {
            tracks ??= new SpineLayoutTrack[0];
            Rect[] result = new Rect[tracks.Length];
            if (tracks.Length == 0)
            {
                return result;
            }

            gap = Mathf.Max(0f, gap);
            float totalLength = horizontal ? rect.width : rect.height;
            float available = Mathf.Max(0f, totalLength - gap * (tracks.Length - 1));
            float fixedLength = 0f;
            float totalWeight = 0f;

            for (int i = 0; i < tracks.Length; i++)
            {
                if (tracks[i].Flexible)
                {
                    totalWeight += tracks[i].Weight;
                }
                else
                {
                    fixedLength += tracks[i].FixedSize;
                }
            }

            float flexAvailable = Mathf.Max(0f, available - fixedLength);
            float cursor = horizontal ? rect.x : rect.y;

            for (int i = 0; i < tracks.Length; i++)
            {
                SpineLayoutTrack track = tracks[i];
                float length = track.Flexible
                    ? Mathf.Max(track.MinSize, flexAvailable * (track.Weight / Mathf.Max(0.0001f, totalWeight)))
                    : track.FixedSize;

                result[i] = horizontal
                    ? new Rect(cursor, rect.y, length, rect.height)
                    : new Rect(rect.x, cursor, rect.width, length);
                cursor += length + gap;
            }

            return result;
        }
    }
}

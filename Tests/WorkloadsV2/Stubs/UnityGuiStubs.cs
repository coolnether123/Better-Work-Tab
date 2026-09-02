using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public float x;
        public float y;

        public static Vector2 zero => new Vector2(0f, 0f);

        public static Vector2 operator +(Vector2 left, Vector2 right) =>
            new Vector2(left.x + right.x, left.y + right.y);

        public static Vector2 operator /(Vector2 value, float divisor) =>
            new Vector2(value.x / divisor, value.y / divisor);
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public Rect(Vector2 position, Vector2 size)
        {
            x = position.x;
            y = position.y;
            width = size.x;
            height = size.y;
        }

        public float x;
        public float y;
        public float width;
        public float height;

        public static Rect zero => new Rect(0f, 0f, 0f, 0f);
        public Vector2 position
        {
            get => new Vector2(x, y);
            set
            {
                x = value.x;
                y = value.y;
            }
        }

        public Vector2 size
        {
            get => new Vector2(width, height);
            set
            {
                width = value.x;
                height = value.y;
            }
        }

        public float xMin
        {
            get => x;
            set
            {
                width += x - value;
                x = value;
            }
        }

        public float xMax
        {
            get => x + width;
            set => width = value - x;
        }

        public float yMin
        {
            get => y;
            set
            {
                height += y - value;
                y = value;
            }
        }

        public float yMax
        {
            get => y + height;
            set => height = value - y;
        }

        public Vector2 center => new Vector2(x + width * 0.5f, y + height * 0.5f);

        public bool Contains(Vector2 point)
        {
            return point.x >= xMin && point.x < xMax &&
                   point.y >= yMin && point.y < yMax;
        }

        public Rect ContractedBy(float margin)
        {
            return new Rect(x + margin, y + margin, width - margin * 2f, height - margin * 2f);
        }
    }

    public enum TextAnchor
    {
        UpperLeft,
        MiddleLeft,
        MiddleCenter
    }

    public static class Mathf
    {
        public static float Abs(float value) => Math.Abs(value);
        public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
        public static float Lerp(float from, float to, float amount) => from + (to - from) * amount;
        public static float Max(float left, float right) => Math.Max(left, right);
        public static int Max(int left, int right) => Math.Max(left, right);
        public static float Min(float left, float right) => Math.Min(left, right);
        public static float Sin(float value) => (float)Math.Sin(value);
        public static float Round(float value) => (float)Math.Round(value);
    }

    public static class Time
    {
        public static float realtimeSinceStartup { get; set; }
    }

    public struct Color
    {
        public Color(float red, float green, float blue, float alpha)
        {
            r = red;
            g = green;
            b = blue;
            a = alpha;
        }

        public float r;
        public float g;
        public float b;
        public float a;

        public static Color white
        {
            get { return new Color(1f, 1f, 1f, 1f); }
        }
    }

    public static class GUI
    {
        public static Color color { get; set; }
    }
}

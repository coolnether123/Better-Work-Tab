namespace UnityEngine
{
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

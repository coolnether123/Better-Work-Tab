using System.Collections.Generic;
using System.Xml;

namespace Verse
{
    public interface IExposable
    {
        void ExposeData();
    }

    public enum LoadSaveMode
    {
        Inactive = 0,
        Saving = 1,
        LoadingVars = 2,
        PostLoadInit = 3
    }

    public enum LookMode
    {
        Value = 0,
        Deep = 1
    }

    public sealed class ScribeLoaderStub
    {
        public XmlNode curXmlParent;
    }

    public static class Scribe
    {
        public static LoadSaveMode mode = LoadSaveMode.Inactive;
        public static ScribeLoaderStub loader;
    }

    public static class Scribe_Values
    {
        public static void Look(ref string value, string label, string defaultValue = null) { }
        public static void Look(ref int value, string label, int defaultValue = 0) { }
        public static void Look(ref bool value, string label, bool defaultValue = false) { }
    }

    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> values, string label, LookMode lookMode)
        {
        }
    }

    public static class Scribe_Deep
    {
        public static void Look<T>(ref T value, string label) where T : class, IExposable
        {
        }
    }
}

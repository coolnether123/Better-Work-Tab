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

    public sealed class ScribeSaverStub
    {
        public XmlNode curXmlParent;
    }

    public static class Scribe
    {
        public static LoadSaveMode mode = LoadSaveMode.Inactive;
        public static ScribeLoaderStub loader;
        public static ScribeSaverStub saver;
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
            // The deterministic suite only needs a faithful collection member
            // distinction for the schema-2 presentation migration: omitted is
            // different from an explicitly serialized empty collection.
            if (label != "presentationSettingIntents") return;

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                XmlNode parent = Scribe.saver?.curXmlParent;
                if (parent == null || values == null) return;
                XmlDocument document = parent as XmlDocument ?? parent.OwnerDocument;
                if (document == null) return;
                XmlElement member = document.CreateElement(label);
                parent.AppendChild(member);
                for (int i = 0; i < values.Count; i++) member.AppendChild(document.CreateElement("li"));
                return;
            }

            if (Scribe.mode != LoadSaveMode.LoadingVars || Scribe.loader?.curXmlParent == null) return;
            foreach (XmlNode child in Scribe.loader.curXmlParent.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element && child.Name == label)
                {
                    values = new List<T>();
                    return;
                }
            }
        }
    }

    public static class Scribe_Deep
    {
        public static void Look<T>(ref T value, string label) where T : class, IExposable
        {
        }
    }
}

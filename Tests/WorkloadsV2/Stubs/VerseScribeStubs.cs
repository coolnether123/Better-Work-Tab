using System.Collections.Generic;
using System.Xml;

namespace Verse
{
    public readonly struct TaggedString
    {
        private readonly string _value;

        public TaggedString(string value)
        {
            _value = value ?? string.Empty;
        }

        public override string ToString() => _value;
    }

    public static class TestTranslationExtensions
    {
        public static TaggedString Translate(this string key, params object[] args)
        {
            string value;
            switch (key)
            {
                case "BWT_Workload_NoCurrentGame":
                    value = "Load or start a game before using workloads.";
                    break;
                case "BWT_Workload_NameRequired":
                    value = "Enter a workload name.";
                    break;
                case "BWT_Workload_Conflict":
                    value = "The workload changed or conflicts with another saved workload. Refresh the preview and try again.";
                    break;
                case "BWT_Workload_ValidationAtPath":
                    value = "Cannot apply this workload because the saved data at {0} is invalid.";
                    break;
                case "BWT_Workload_ValidationMissingAtPath":
                    value = "Cannot apply this workload because required data is missing at {0}.";
                    break;
                case "BWT_Workload_ValidationUnknownAtPath":
                    value = "Cannot apply this workload because {0} refers to something that is no longer available.";
                    break;
                case "BWT_Workload_ValidationConflictAtPath":
                    value = "Cannot apply this workload because saved entries conflict at {0}.";
                    break;
                case "BWT_Workload_Applied":
                    value = "Workload applied.";
                    break;
                case "BWT_Workload_AppliedPartial":
                    value = "Workload applied, but some entries were skipped.";
                    break;
                case "BWT_Workload_Updated":
                    value = "Workload saved.";
                    break;
                case "BWT_Workload_Forked":
                    value = "New workload saved.";
                    break;
                case "BWT_Workload_ModeUnavailable":
                    value = "That workload mode is unavailable.";
                    break;
                case "BWT_Workload_UnsupportedData":
                    value = "This workload contains data from an older or unsupported format. Apply and save are disabled to avoid losing it.";
                    break;
                case "BWT_Workload_MultiplayerStartFailed":
                    value = "Better Work Tab could not start the multiplayer workload action.";
                    break;
                case "BWT_Workload_CaptureFailed":
                    value = "Better Work Tab could not read all current Work-tab settings.";
                    break;
                case "BWT_Workload_AuthorityBlocked":
                    value = "Another Work-tab system controls the affected priorities, so Better Work Tab cannot change them.";
                    break;
                case "BWT_Workload_MultiplayerRecovery":
                    value = "Multiplayer is still finishing the previous workload action. You cannot edit this preview yet.";
                    break;
                case "BWT_Workload_NotFound":
                    value = "Better Work Tab could not find that workload.";
                    break;
                case "BWT_Workload_OperationFailed":
                    value = "Better Work Tab could not finish that workload action.";
                    break;
                case "BWT_Workload_UnsupportedClear":
                    value = "This preview cannot clear {0} yet. No changes were made.";
                    break;
                case "BWT_Workload_DimensionSchedules":
                    value = "hourly priorities";
                    break;
                default:
                    value = key ?? string.Empty;
                    break;
            }

            return new TaggedString(args == null || args.Length == 0
                ? value
                : string.Format(value, args));
        }
    }

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

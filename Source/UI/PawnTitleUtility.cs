using System.Reflection;
using Verse;

namespace Better_Work_Tab.UI
{
    internal static class PawnTitleUtility
    {
        internal static bool CanEditTitle(Pawn pawn)
        {
            object story = pawn?.story;
            return story != null &&
                (GetWritableTitleProperty(story) != null || GetTitleField(story) != null);
        }

        internal static string GetCurrentTitle(Pawn pawn)
        {
            return pawn?.story?.Title ?? string.Empty;
        }

        internal static string GetDefaultTitle(Pawn pawn)
        {
            object story = pawn?.story;
            if (story == null)
            {
                return string.Empty;
            }

            PropertyInfo titleDefault = story.GetType().GetProperty(
                "TitleDefault",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return titleDefault?.GetValue(story, null) as string ?? GetCurrentTitle(pawn);
        }

        internal static bool TrySetTitle(Pawn pawn, string title)
        {
            object story = pawn?.story;
            if (story == null)
            {
                return false;
            }

            string cleanTitle = (title ?? string.Empty).Trim();
            PropertyInfo titleProperty = GetWritableTitleProperty(story);
            if (titleProperty != null)
            {
                titleProperty.SetValue(story, cleanTitle, null);
                return true;
            }

            FieldInfo titleField = GetTitleField(story);
            if (titleField == null)
            {
                return false;
            }

            titleField.SetValue(story, string.IsNullOrEmpty(cleanTitle) ? null : cleanTitle);
            return true;
        }

        private static PropertyInfo GetWritableTitleProperty(object story)
        {
            PropertyInfo titleProperty = story.GetType().GetProperty(
                "Title",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return titleProperty != null && titleProperty.CanWrite ? titleProperty : null;
        }

        private static FieldInfo GetTitleField(object story)
        {
            FieldInfo titleField = story.GetType().GetField(
                "title",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return titleField != null && titleField.FieldType == typeof(string) ? titleField : null;
        }
    }
}

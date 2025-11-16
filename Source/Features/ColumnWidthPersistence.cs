using System.Collections.Generic;
using RimWorld;

namespace Better_Work_Tab.Features
{
    public interface IColumnWidthStore
    {
        float GetWidth(PawnColumnDef column, float defaultWidth);
        void SetWidth(PawnColumnDef column, float width);
    }

    public sealed class ColumnWidthPersistence : IColumnWidthStore
    {
        public float GetWidth(PawnColumnDef column, float defaultWidth)
        {
            if (column?.defName == null)
            {
                return defaultWidth;
            }

            var settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return defaultWidth;
            }

            var widths = settings.storedColumnWidths;
            if (widths != null && widths.TryGetValue(column.defName, out var stored))
            {
                return stored;
            }

            return defaultWidth;
        }

        public void SetWidth(PawnColumnDef column, float width)
        {
            if (column?.defName == null)
            {
                return;
            }

            var settings = BetterWorkTabMod.Settings;
            if (settings == null)
            {
                return;
            }

            if (settings.storedColumnWidths == null)
            {
                settings.storedColumnWidths = new Dictionary<string, float>();
            }

            settings.storedColumnWidths[column.defName] = width;
            settings.Write();
        }
    }
}

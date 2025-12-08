using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Pure C# helpers for settings manipulation.
    /// </summary>
    public static class SettingsUIHelpers
    {
        /// <summary>
        /// Gets a field value from an object by field name.
        /// </summary>
        public static T GetFieldValue<T>(object obj, string fieldName)
        {
            if (obj == null || string.IsNullOrEmpty(fieldName))
                return default;

            var field = obj.GetType().GetField(fieldName, 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (field == null)
                return default;

            var value = field.GetValue(obj);
            return value is T typed ? typed : default;
        }

        /// <summary>
        /// Sets a field value on an object by field name.
        /// </summary>
        public static bool SetFieldValue<T>(object obj, string fieldName, T value)
        {
            if (obj == null || string.IsNullOrEmpty(fieldName))
                return false;

            var field = obj.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
                return false;

            field.SetValue(obj, value);
            return true;
        }

        /// <summary>
        /// Groups items into rows of a specified size.
        /// </summary>
        public static IEnumerable<List<T>> GroupIntoRows<T>(IEnumerable<T> items, int itemsPerRow)
        {
            var list = items.ToList();
            for (int i = 0; i < list.Count; i += itemsPerRow)
            {
                yield return list.Skip(i).Take(itemsPerRow).ToList();
            }
        }

        /// <summary>
        /// Clamps an integer value to a range.
        /// </summary>
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Clamps a float value to a range.
        /// </summary>
        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}

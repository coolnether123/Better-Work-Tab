using HarmonyLib;
using RimWorld;
using Spine.UI.ColourPicker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;
using static HarmonyLib.Code;

namespace Spine.UI.WidgetExtensions
{
    public static partial class SpineWidgets
    {
        /// <summary>
        /// colorChangeOperation should appear as (newColor, isClosing) => { colorToChange = newColor; }
        /// 
        /// </summary>
        /// <param name="listingStandard">The listing standard to use.</param>
        /// <param name="colorToChange">This is required additionally for the color display.</param>
        /// <param name="colorChangeOperation">How to apply the newly chosen color. Should take the form of (newColor, isClosing) => { colorToChange = newColor; }</param>
        /// <param name="buttonText">What text to show on the button.</param>
        public static void LS_ColorPickButton_Settings(Listing_Standard listingStandard, 
            ModSettings s,
            string nameOfColorToChange,  
            string buttonText,
            bool dependsOn = false)
        {


            var field = AccessTools.DeclaredField(s.GetType(), nameOfColorToChange);
            if (field == null)
            {
                Log.Warning($"Could not find field {nameOfColorToChange} in settings class {s.GetType().Name}.");
                return;
            }

            Color value = (Color)field.GetValue(s);

            Widgets.DrawBoxSolid(listingStandard.GetRect(10), value);
            if (dependsOn) GUI.color = Color.gray;

            if (dependsOn)
            {
                listingStandard.ButtonText(buttonText);
            }
            else
            {
                if (listingStandard.ButtonText(buttonText))
                {
                    Find.WindowStack.Add(new Dialog_ColourPicker(value, (color, b) =>
                    {
                        field.SetValue(s, color);
                    }));
                }
            }
            listingStandard.Gap(10f);
            if(dependsOn) GUI.color = Color.white;
        }

        public static void LS_ChooseFromEnum<T>(Listing_Standard l, string label, ModSettings s, string settingName, bool uneditable = false) where T : Enum
        {

            

            var field = AccessTools.DeclaredField(s.GetType(), settingName);
            T value = (T)field.GetValue(s);


            if (uneditable) GUI.color = Color.gray;
            if (l.ButtonTextLabeled(label, value.ToString()))
            {
                List<FloatMenuOption> enums = new List<FloatMenuOption>();
                        

                foreach (var e in Enum.GetValues(typeof(T)))
                {
                    enums.Add(new FloatMenuOption(e.ToString(), delegate
                    {
                        field.SetValue(s, e);
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(enums));
            }

        }

    }
}

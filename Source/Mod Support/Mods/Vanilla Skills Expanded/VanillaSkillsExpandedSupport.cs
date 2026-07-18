using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;

namespace Better_Work_Tab.ModSupport
{
    /// <summary>
    /// Optional compatibility for Vanilla Skills Expanded's custom PassionDef system.
    /// VSE stores custom passions as extra Passion enum values backed by VSE.Passions.PassionDef.
    /// Reflection keeps BWT independent from VSE while still letting rules target those passions.
    /// </summary>
    public sealed class VanillaSkillsExpandedSupport : ModSupportModuleBase
    {
        private const string VanillaSkillsExpandedPackageId = "vanillaexpanded.skills";

        private static bool initialized;
        private static List<PassionOption> passionOptions = new List<PassionOption>
        {
            new PassionOption((int)Passion.None, "None", "None", 0.35f, true),
            new PassionOption((int)Passion.Minor, "Minor", "Minor", 1f, false),
            new PassionOption((int)Passion.Major, "Major", "Major", 1.5f, false)
        };

        public override string PackageId => VanillaSkillsExpandedPackageId;
        public override string DisplayName => "Vanilla Skills Expanded";

        public override void OnModsDetected()
        {
            Refresh(force: true);
        }

        public static IList<PassionOption> GetPassionOptions()
        {
            EnsureInitialized();
            return passionOptions.AsReadOnly();
        }

        public static int GetMaxPassionValue()
        {
            EnsureInitialized();
            return passionOptions.Count == 0 ? (int)Passion.Major : passionOptions.Max(option => option.Value);
        }

        public static string GetPassionLabel(int passionValue)
        {
            EnsureInitialized();

            PassionOption option = passionOptions.FirstOrDefault(p => p.Value == passionValue);
            if (option.IsValid)
            {
                return option.Label;
            }

            return passionValue switch
            {
                (int)Passion.None => "BWT_None".CanTranslate() ? "BWT_None".Translate().ToString() : "None",
                (int)Passion.Minor => "BWT_Minor".CanTranslate() ? "BWT_Minor".Translate().ToString() : "Minor",
                (int)Passion.Major => "BWT_Major".CanTranslate() ? "BWT_Major".Translate().ToString() : "Major",
                _ => "Passion " + passionValue
            };
        }

        public static bool PassionMatches(Pawn_SkillTracker skills, WorkTypeDef workType, int requiredPassion)
        {
            if (requiredPassion < 0)
            {
                return true;
            }

            if (skills == null || workType == null)
            {
                return false;
            }

            Passion actual = skills.MaxPassionOfRelevantSkillsFor(workType);
            return (int)actual == requiredPassion;
        }

        private static void EnsureInitialized()
        {
            if (!initialized)
            {
                Refresh(force: false);
            }
        }

        private static void Refresh(bool force)
        {
            if (initialized && !force)
            {
                return;
            }

            initialized = true;
            passionOptions = BuildVanillaPassions();

            if (ModListerCompat.GetActiveModWithIdentifier(VanillaSkillsExpandedPackageId) == null)
            {
                return;
            }

            try
            {
                Type passionDefType = AccessTools.TypeByName("VSE.Passions.PassionDef");
                if (passionDefType == null)
                {
                    Log.Warning("[BWT][Vanilla Skills Expanded] PassionDef type not found; using vanilla passion options.");
                    return;
                }

                Type passionManagerType = AccessTools.TypeByName("VSE.Passions.PassionManager");
                if (passionManagerType != null)
                {
                    RuntimeHelpers.RunClassConstructor(passionManagerType.TypeHandle);
                }

                Type closedDatabaseType = typeof(DefDatabase<>).MakeGenericType(passionDefType);
                PropertyInfo allDefsProperty = AccessTools.Property(closedDatabaseType, "AllDefsListForReading");
                IEnumerable defs = allDefsProperty?.GetValue(null, null) as IEnumerable;
                if (defs == null)
                {
                    Log.Warning("[BWT][Vanilla Skills Expanded] Could not read PassionDef database; using vanilla passion options.");
                    return;
                }

                FieldInfo learnRateFactorField = AccessTools.Field(passionDefType, "learnRateFactor");
                FieldInfo isBadField = AccessTools.Field(passionDefType, "isBad");

                var reflected = new List<PassionOption>();
                foreach (object rawDef in defs)
                {
                    if (!(rawDef is Def def))
                    {
                        continue;
                    }

                    float learnRateFactor = ReadField(learnRateFactorField, rawDef, GetVanillaLearnRate((int)def.index));
                    bool isBad = ReadField(isBadField, rawDef, false);
                    reflected.Add(new PassionOption(
                        def.index,
                        def.defName,
                        BuildLabel(def),
                        learnRateFactor,
                        isBad));
                }

                if (reflected.Count == 0)
                {
                    return;
                }

                passionOptions = reflected
                    .OrderBy(option => option.LearnRateFactor)
                    .ThenBy(option => option.Value)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warning("[BWT][Vanilla Skills Expanded] Failed to initialize passion compatibility; using vanilla passion options. " + ex.Message);
                passionOptions = BuildVanillaPassions();
            }
        }

        private static List<PassionOption> BuildVanillaPassions()
        {
            return new List<PassionOption>
            {
                new PassionOption((int)Passion.None, "None", TranslateOrFallback("BWT_None", "None"), 0.35f, true),
                new PassionOption((int)Passion.Minor, "Minor", TranslateOrFallback("BWT_Minor", "Minor"), 1f, false),
                new PassionOption((int)Passion.Major, "Major", TranslateOrFallback("BWT_Major", "Major"), 1.5f, false)
            };
        }

        private static string TranslateOrFallback(string key, string fallback)
        {
            try
            {
                return key.CanTranslate() ? key.Translate().ToString() : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static string BuildLabel(Def def)
        {
            if (!def.label.NullOrEmpty())
            {
                return def.label.CapitalizeFirst();
            }

            return def.defName ?? "Passion";
        }

        private static float GetVanillaLearnRate(int passionValue)
        {
            return passionValue switch
            {
                (int)Passion.None => 0.35f,
                (int)Passion.Minor => 1f,
                (int)Passion.Major => 1.5f,
                _ => passionValue
            };
        }

        private static T ReadField<T>(FieldInfo field, object instance, T fallback)
        {
            if (field == null || instance == null)
            {
                return fallback;
            }

            object value = field.GetValue(instance);
            if (value is T typed)
            {
                return typed;
            }

            return fallback;
        }

        public readonly struct PassionOption
        {
            public PassionOption(int value, string defName, string label, float learnRateFactor, bool isBad)
            {
                Value = value;
                DefName = defName ?? string.Empty;
                Label = label ?? defName ?? value.ToString();
                LearnRateFactor = learnRateFactor;
                IsBad = isBad;
            }

            public int Value { get; }
            public string DefName { get; }
            public string Label { get; }
            public float LearnRateFactor { get; }
            public bool IsBad { get; }
            public bool IsValid => !string.IsNullOrEmpty(Label) || !string.IsNullOrEmpty(DefName);
        }
    }
}

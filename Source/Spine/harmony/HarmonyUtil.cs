using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Spine.Harmony.Infrastructure;

namespace Spine.Harmony
{
    /// <summary>
    /// Guarded Harmony patch helpers used by ModAPI and mods.
    /// Use these wrappers when patch application should respect debug, dangerous, and struct-return safety settings.
    /// </summary>
    internal static class HarmonyUtil
    {
        /// <summary>
        /// Marks a Harmony patch class as debug-only.
        /// The patch is skipped unless debug patches are explicitly enabled.
        /// </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        internal sealed class DebugPatchAttribute : Attribute
        {
            public string Key;
            public DebugPatchAttribute() { }
            public DebugPatchAttribute(string key) { Key = key; }
        }

        /// <summary>
        /// Marks a patch as intentionally high risk.
        /// Dangerous patches require explicit opt-in before <see cref="PatchAll(HarmonyLib.Harmony, Assembly, PatchOptions)"/> applies them.
        /// </summary>
        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
        internal sealed class DangerousAttribute : Attribute
        {
            public string Reason;
            public DangerousAttribute() { }
            public DangerousAttribute(string reason) { Reason = reason; }
        }

        /// <summary>
        /// Safety and ordering options used while applying Harmony patches.
        /// </summary>
        internal sealed class PatchOptions
        {
            public bool AllowDebugPatches;
            public bool AllowDangerousPatches;
            public bool AllowStructReturns;
            public string[] Before;
            public string[] After;
            public int? Priority;
            public Action<object, string> OnResult;
        }

        private static readonly HashSet<string> SensitiveDeny = new HashSet<string>(StringComparer.Ordinal);

        public static void PatchAll(HarmonyLib.Harmony h, Assembly asm, PatchOptions options)
        {
            if (h == null || asm == null) return;
            if (options == null) options = new PatchOptions();

            foreach (var type in SafeTypes(asm))
            {
                PatchType(h, type, options);
            }
        }

        public static IList<MethodBase> PatchType(HarmonyLib.Harmony h, Type type, PatchOptions options)
        {
            if (h == null || type == null) return new MethodBase[0];
            if (options == null) options = new PatchOptions();

            try
            {
                if (!HasHarmonyPatchAttributes(type)) return new MethodBase[0];

                if (!options.AllowDebugPatches && HasDebugAttribute(type))
                {
                    options.OnResult?.Invoke((object)type, "skipped: DebugPatch not enabled");
                    return new MethodBase[0];
                }

                if (!options.AllowDangerousPatches && HasDangerousAttribute(type))
                {
                    options.OnResult?.Invoke((object)type, "skipped: Dangerous not enabled");
                    return new MethodBase[0];
                }

                var targets = GetPatchTargets(type);
                if (targets != null)
                {
                    foreach (var m in targets)
                    {
                        var key = TargetKey(m);
                        if (!options.AllowDangerousPatches && SensitiveDeny.Contains(key) && !HasDangerousAttribute(type))
                        {
                            options.OnResult?.Invoke((object)m, "skipped: sensitive target requires [Dangerous]");
                            return new MethodBase[0];
                        }
                        if (!options.AllowStructReturns && IsStructReturn(m) && !HasDangerousAttribute(type))
                        {
                            options.OnResult?.Invoke((object)m, "skipped: struct-return target not allowed");
                            return new MethodBase[0];
                        }
                    }
                }

                var proc = new PatchClassProcessor(h, type);
                var patched = proc.Patch();

                if (patched != null && patched.Count > 0)
                {
                    foreach (var m in patched)
                        options.OnResult?.Invoke((object)m, "patched");
                    return patched.Cast<MethodBase>().ToList();
                }

                options.OnResult?.Invoke((object)type, "no methods patched");
                return new MethodBase[0];
            }
            catch (Exception ex)
            {
                options.OnResult?.Invoke((object)type, "error: " + ex.Message);
                return new MethodBase[0];
            }
        }

        internal static IList<MethodBase> PatchKnownType(
            HarmonyLib.Harmony h,
            Type type,
            PatchOptions options,
            IList<MethodBase> knownTargets)
        {
            if (h == null || type == null) return new MethodBase[0];
            if (options == null) options = new PatchOptions();

            try
            {
                if (!options.AllowDebugPatches && HasDebugAttribute(type))
                {
                    options.OnResult?.Invoke((object)type, "skipped: DebugPatch not enabled");
                    return new MethodBase[0];
                }

                if (!options.AllowDangerousPatches && HasDangerousAttribute(type))
                {
                    options.OnResult?.Invoke((object)type, "skipped: Dangerous not enabled");
                    return new MethodBase[0];
                }

                if (knownTargets != null)
                    ValidateTargets(type, knownTargets, options);

                var proc = new PatchClassProcessor(h, type);
                var patched = proc.Patch();

                if (patched != null && patched.Count > 0)
                {
                    foreach (var m in patched)
                        options.OnResult?.Invoke((object)m, "patched");
                    return patched.Cast<MethodBase>().ToList();
                }

                options.OnResult?.Invoke((object)type, "no methods patched");
                return new MethodBase[0];
            }
            catch (Exception ex)
            {
                options.OnResult?.Invoke((object)type, "error: " + ex.Message);
                return new MethodBase[0];
            }
        }

        private static void ValidateTargets(Type patchType, IEnumerable<MethodBase> targets, PatchOptions options)
        {
            foreach (var m in targets)
            {
                var key = TargetKey(m);
                if (!options.AllowDangerousPatches && SensitiveDeny.Contains(key) && !HasDangerousAttribute(patchType))
                {
                    options.OnResult?.Invoke((object)m, "skipped: sensitive target requires [Dangerous]");
                    throw new InvalidOperationException("Sensitive target requires [Dangerous].");
                }
                if (!options.AllowStructReturns && IsStructReturn(m) && !HasDangerousAttribute(patchType))
                {
                    options.OnResult?.Invoke((object)m, "skipped: struct-return target not allowed");
                    throw new InvalidOperationException("Struct-return target not allowed.");
                }
            }
        }

        public static bool IsStructReturn(MethodBase m)
        {
            var mi = m as MethodInfo;
            if (mi == null) return false;
            try { return mi.ReturnType != null && mi.ReturnType.IsValueType && mi.ReturnType != typeof(void); }
            catch (Exception ex) { MMLog.WarnOnce("HarmonyUtil.IsStructReturn", "Error checking for struct return: " + ex.Message); return false; }
        }

        private static string TargetKey(MethodBase mb)
        {
            try { return mb.DeclaringType.FullName + "." + mb.Name; }
            catch (Exception ex) { MMLog.WarnOnce("HarmonyUtil.TargetKey", "Error getting target key: " + ex.Message); return "<unknown>"; }
        }

        public static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { return rtle.Types.Where(t => t != null); }
            catch (Exception ex) { MMLog.WarnOnce("HarmonyUtil.SafeTypes", "Error getting types from assembly: " + ex.Message); return Enumerable.Empty<Type>(); }
        }

        public static bool HasHarmonyPatchAttributes(Type t)
        {
            try
            {
                if (t == null)
                    return false;

                if (CustomAttributeData.GetCustomAttributes(t).Any(a => HasHarmonyAttributeName(GetAttributeTypeName(a))))
                    return true;

                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return methods.Any(m => CustomAttributeData.GetCustomAttributes(m).Any(a => HasHarmonyAttributeName(GetAttributeTypeName(a))));
            }
            catch (Exception ex)
            {
                if (!(ex is ReflectionTypeLoadException) && !(ex is TypeLoadException) && !(ex is FileNotFoundException))
                    MMLog.WarnOnce("HarmonyUtil.HasHarmonyPatchAttributes", "Error checking for Harmony attributes: " + ex.Message);
                return false;
            }
        }

        private static bool HasHarmonyAttributeName(string fullName)
        {
            return !string.IsNullOrEmpty(fullName) && fullName.StartsWith("HarmonyLib.Harmony", StringComparison.Ordinal);
        }

        private static string GetAttributeTypeName(CustomAttributeData attribute)
        {
            try
            {
                if (attribute == null)
                    return null;

                // .NET 3.5 does not expose CustomAttributeData.AttributeType.
                // Constructor.DeclaringType is the compatible path for this target.
                if (attribute.Constructor != null && attribute.Constructor.DeclaringType != null)
                    return attribute.Constructor.DeclaringType.FullName;
            }
            catch
            {
            }

            return null;
        }

        public static bool HasDebugAttribute(Type t)
        {
            return HasAttribute<DebugPatchAttribute>(t);
        }

        public static bool HasDangerousAttribute(Type t)
        {
            return HasAttribute<DangerousAttribute>(t);
        }

        private static bool HasAttribute<T>(Type t) where T : Attribute
        {
            try { return t.GetCustomAttributes(typeof(T), false).FirstOrDefault() != null; }
            catch (Exception ex) { MMLog.WarnOnce("HarmonyUtil.HasAttribute", "Error checking for attribute: " + ex.Message); return false; }
        }

        public static IEnumerable<MethodBase> GetPatchTargets(Type patchClass)
        {
            try
            {
                var tm = patchClass.GetMethod("TargetMethods", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (tm != null)
                {
                    var e = tm.Invoke(null, null) as System.Collections.IEnumerable;
                    if (e != null)
                    {
                        var list = new List<MethodBase>();
                        foreach (var it in e) { var mb = it as MethodBase; if (mb != null) list.Add(mb); }
                        if (list.Count > 0) return list;
                    }
                }
            }
            catch (Exception ex) { MMLog.WarnOnce("HarmonyUtil.TryGetTargets.TargetMethods", "Error invoking TargetMethods: " + ex.Message); }

            try
            {
                var tm = patchClass.GetMethod("TargetMethod", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (tm != null)
                {
                    var mb = tm.Invoke(null, null) as MethodBase;
                    if (mb != null) return new[] { mb };
                }
            }
            catch (Exception ex) { MMLog.WarnOnce("HarmonyUtil.TryGetTargets.TargetMethod", "Error invoking TargetMethod: " + ex.Message); }

            try
            {
                var attrs = patchClass.GetCustomAttributes(true);
                var list = new List<MethodBase>();
                foreach (var a in attrs)
                {
                    var at = a.GetType();
                    if (!string.Equals(at.FullName, "HarmonyLib.HarmonyPatch", StringComparison.Ordinal))
                        continue;

                    var typeProp = at.GetProperty("type") ?? (MemberInfo)at.GetField("type");
                    var nameProp = at.GetProperty("methodName") ?? (MemberInfo)at.GetField("methodName");
                    Type targetType = typeProp is PropertyInfo tp
                        ? tp.GetValue(a, null) as Type
                        : (typeProp is FieldInfo tf ? tf.GetValue(a) as Type : null);
                    string methodName = nameProp is PropertyInfo np
                        ? np.GetValue(a, null) as string
                        : (nameProp is FieldInfo nf ? nf.GetValue(a) as string : null);
                    if (targetType == null || string.IsNullOrEmpty(methodName)) continue;

                    try
                    {
                        var mb = targetType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                        if (mb != null) list.Add(mb);
                    }
                    catch (Exception ex) { MMLog.WarnOnce("HarmonyUtil.TryGetTargets.GetMethod", "Error getting method: " + ex.Message); }
                }
                if (list.Count > 0) return list;
            }
            catch (Exception ex) { MMLog.WarnOnce("HarmonyUtil.TryGetTargets.Attributes", "Error reading HarmonyPatch attributes: " + ex.Message); }

            return null;
        }

    }
}

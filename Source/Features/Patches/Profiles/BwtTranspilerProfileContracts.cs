using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Better_Work_Tab.Transpilers.BwtExactProfile;

namespace Better_Work_Tab.Features.Patches.Profiles
{
    internal sealed class BwtBuildIdentity
    {
        internal static readonly BwtBuildIdentity RimWorld16 = new BwtBuildIdentity(
            "Assembly-CSharp",
            "1.6.9676.17238",
            new Guid("13bee51f-e6fa-4214-a4a5-e1e7b84a41ee"));

        internal BwtBuildIdentity(string assemblyName, string assemblyVersion, Guid moduleVersionId)
        {
            AssemblyName = assemblyName;
            AssemblyVersion = assemblyVersion;
            ModuleVersionId = moduleVersionId;
        }

        internal string AssemblyName { get; private set; }
        internal string AssemblyVersion { get; private set; }
        internal Guid ModuleVersionId { get; private set; }

        internal static BwtBuildIdentity From(MethodBase method)
        {
            try
            {
                Assembly assembly = method?.Module?.Assembly;
                if (assembly == null) return null;
                AssemblyName name = assembly.GetName();
                return new BwtBuildIdentity(
                    name.Name,
                    name.Version == null ? null : name.Version.ToString(),
                    method.Module.ModuleVersionId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal bool Equals(BwtBuildIdentity other)
        {
            return other != null &&
                String.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal) &&
                String.Equals(AssemblyVersion, other.AssemblyVersion, StringComparison.Ordinal) &&
                ModuleVersionId == other.ModuleVersionId;
        }

        internal string Describe()
        {
            return AssemblyName + ",Version=" + AssemblyVersion + ",MVID=" + ModuleVersionId;
        }
    }

    internal sealed class BwtTargetIdentity
    {
        private BwtTargetIdentity(
            string declaringTypeName, string methodName, string returnTypeName,
            IReadOnlyList<string> parameterTypeNames, BwtBuildIdentity build)
        {
            DeclaringTypeName = declaringTypeName;
            MethodName = methodName;
            ReturnTypeName = returnTypeName;
            ParameterTypeNames = parameterTypeNames;
            Build = build;
        }

        internal string DeclaringTypeName { get; private set; }
        internal string MethodName { get; private set; }
        internal string ReturnTypeName { get; private set; }
        internal IReadOnlyList<string> ParameterTypeNames { get; private set; }
        internal BwtBuildIdentity Build { get; private set; }

        internal static BwtTargetIdentity ForMethod(MethodBase method, BwtBuildIdentity build)
        {
            if (method == null)
            {
                return new BwtTargetIdentity(
                    "<missing-target>", "<missing-target>", "<missing-target>",
                    new string[0], build);
            }

            var parameters = new List<string>();
            try
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                    parameters.Add(TypeName(parameter.ParameterType));
            }
            catch (Exception)
            {
                parameters.Add("<unreadable>");
            }

            return new BwtTargetIdentity(
                TypeName(method.DeclaringType),
                method.Name,
                TypeName(IlVerifier.ReturnType(method)),
                parameters.AsReadOnly(),
                build);
        }

        internal bool Matches(MethodBase method, BwtBuildIdentity selectedBuild)
        {
            if (method == null || Build == null || !Build.Equals(selectedBuild)) return false;
            if (!String.Equals(DeclaringTypeName, TypeName(method.DeclaringType), StringComparison.Ordinal) ||
                !String.Equals(MethodName, method.Name, StringComparison.Ordinal) ||
                !String.Equals(ReturnTypeName, TypeName(IlVerifier.ReturnType(method)), StringComparison.Ordinal))
                return false;

            try
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != ParameterTypeNames.Count) return false;
                for (int index = 0; index < parameters.Length; index++)
                    if (!String.Equals(ParameterTypeNames[index], TypeName(parameters[index].ParameterType),
                        StringComparison.Ordinal)) return false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal string Describe()
        {
            return DeclaringTypeName + "." + MethodName + " [" + (Build?.Describe() ?? "no-build") + "]";
        }

        private static string TypeName(Type type)
        {
            return type == null ? "<missing-type>" : type.FullName ?? type.Name;
        }
    }

    internal sealed class BwtCallBinding
    {
        internal BwtCallBinding(MethodInfo method)
        {
            Method = method;
        }

        internal MethodInfo Method { get; private set; }
    }

    internal sealed class BwtCallRedirectProfile
    {
        internal BwtCallRedirectProfile(
            string id, BwtTargetIdentity target, BwtCallBinding source,
            BwtCallBinding replacement, MatchMode cardinality)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("A profile ID is required.", "id");
            if (target == null) throw new ArgumentNullException("target");
            if (source == null) throw new ArgumentNullException("source");
            if (replacement == null) throw new ArgumentNullException("replacement");
            IlAnchor.ValidateMatchMode(cardinality);
            Id = id.Trim();
            Target = target;
            Source = source;
            Replacement = replacement;
            Cardinality = cardinality;
        }

        internal string Id { get; private set; }
        internal BwtTargetIdentity Target { get; private set; }
        internal BwtCallBinding Source { get; private set; }
        internal BwtCallBinding Replacement { get; private set; }
        internal MatchMode Cardinality { get; private set; }

        internal string ValidateBindings()
        {
            if (Source.Method == null || Replacement.Method == null)
                return "a named method binding is missing";
            if (Source.Method.IsStatic || !Replacement.Method.IsStatic)
                return "the adapter must replace an instance call with a static call";

            try
            {
                ParameterInfo[] sourceParameters = Source.Method.GetParameters();
                ParameterInfo[] replacementParameters = Replacement.Method.GetParameters();
                if (replacementParameters.Length != sourceParameters.Length + 1 ||
                    replacementParameters[0].ParameterType != Source.Method.DeclaringType)
                    return "the static adapter must receive the original receiver first";
                for (int index = 0; index < sourceParameters.Length; index++)
                    if (sourceParameters[index].ParameterType != replacementParameters[index + 1].ParameterType)
                        return "the static adapter parameters do not match the instance call";
                return null;
            }
            catch (Exception)
            {
                return "binding metadata could not be inspected";
            }
        }
    }

    internal enum BwtPriorityConstantContext
    {
        WrapUnderflow,
        WrapOverflow,
        CachedWrapOverflow,
        BeforeSetPriority,
        SetPriorityUpperBound
    }

    internal enum BwtPriorityProfileShape
    {
        Standard,
        HeaderClicked
    }

    internal sealed class BwtExternalProviderBinding
    {
        internal BwtExternalProviderBinding(
            string declaringTypeName, string methodName, string returnTypeName,
            IReadOnlyList<string> parameterTypeNames)
        {
            if (String.IsNullOrWhiteSpace(declaringTypeName))
                throw new ArgumentException("A provider type is required.", "declaringTypeName");
            if (String.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("A provider method is required.", "methodName");
            DeclaringTypeName = declaringTypeName.Trim();
            MethodName = methodName.Trim();
            ReturnTypeName = returnTypeName;
            ParameterTypeNames = new ReadOnlyCollection<string>(
                new List<string>(parameterTypeNames ?? new string[0]));
        }

        internal string DeclaringTypeName { get; private set; }
        internal string MethodName { get; private set; }
        internal string ReturnTypeName { get; private set; }
        internal IReadOnlyList<string> ParameterTypeNames { get; private set; }

        internal bool Matches(MethodInfo method)
        {
            if (method == null || !method.IsStatic || method.Name != MethodName ||
                TypeName(method.DeclaringType) != DeclaringTypeName ||
                TypeName(method.ReturnType) != ReturnTypeName)
                return false;
            try
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != ParameterTypeNames.Count) return false;
                for (int index = 0; index < parameters.Length; index++)
                    if (TypeName(parameters[index].ParameterType) != ParameterTypeNames[index]) return false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string TypeName(Type type)
        {
            return type == null ? "<missing-type>" : type.FullName ?? type.Name;
        }
    }

    internal sealed class BwtPriorityConstantEdit
    {
        internal BwtPriorityConstantEdit(
            string id, BwtPriorityConstantContext context, MethodInfo contextCall,
            int expectedValue, MethodInfo replacementCall, PatchRequirement requirement,
            bool requiresLocalRelationship = false,
            IReadOnlyList<BwtExternalProviderBinding> externalProviders = null)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("An edit ID is required.", "id");
            if (replacementCall == null) throw new ArgumentNullException("replacementCall");
            IlEditDescription.ValidateRequirement(requirement);
            Id = id.Trim();
            Context = context;
            ContextCall = contextCall;
            ExpectedValue = expectedValue;
            ReplacementCall = replacementCall;
            Requirement = requirement;
            RequiresLocalRelationship = requiresLocalRelationship;
            ExternalProviders = new ReadOnlyCollection<BwtExternalProviderBinding>(
                new List<BwtExternalProviderBinding>(externalProviders ?? new BwtExternalProviderBinding[0]));
        }

        internal string Id { get; private set; }
        internal BwtPriorityConstantContext Context { get; private set; }
        internal MethodInfo ContextCall { get; private set; }
        internal int ExpectedValue { get; private set; }
        internal MethodInfo ReplacementCall { get; private set; }
        internal PatchRequirement Requirement { get; private set; }
        internal bool RequiresLocalRelationship { get; private set; }
        internal IReadOnlyList<BwtExternalProviderBinding> ExternalProviders { get; private set; }

        internal string Validate()
        {
            if (!ReplacementCall.IsStatic || ReplacementCall.GetParameters().Length != 0 ||
                ReplacementCall.ReturnType != typeof(int))
                return "the replacement must be a parameterless static int32 method";
            if (Context == BwtPriorityConstantContext.SetPriorityUpperBound)
                return ContextCall == null && !RequiresLocalRelationship ? null :
                    "the upper-bound edit cannot declare a call or local relationship";
            if (Context == BwtPriorityConstantContext.CachedWrapOverflow)
                return ContextCall == null && RequiresLocalRelationship
                    ? null : "the cached wrap edit must declare only a local relationship";
            if (ContextCall == null) return "the context call binding is missing";
            if (Context == BwtPriorityConstantContext.WrapUnderflow ||
                Context == BwtPriorityConstantContext.WrapOverflow)
                return RequiresLocalRelationship ? null : "the wrap edit needs a local relationship";
            return Context == BwtPriorityConstantContext.BeforeSetPriority
                ? null : "the constant context is unsupported";
        }
    }

    internal sealed class BwtPriorityConstantProfile
    {
        internal BwtPriorityConstantProfile(
            string id, BwtTargetIdentity target, BwtPriorityProfileShape shape,
            IReadOnlyList<BwtPriorityConstantEdit> edits)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("A profile ID is required.", "id");
            if (target == null) throw new ArgumentNullException("target");
            if (shape != BwtPriorityProfileShape.Standard &&
                shape != BwtPriorityProfileShape.HeaderClicked)
                throw new ArgumentOutOfRangeException("shape");
            if (edits == null || edits.Count == 0) throw new ArgumentException("Profile edits are required.", "edits");
            Id = id.Trim();
            Target = target;
            Shape = shape;
            Edits = new ReadOnlyCollection<BwtPriorityConstantEdit>(new List<BwtPriorityConstantEdit>(edits));
        }

        internal string Id { get; private set; }
        internal BwtTargetIdentity Target { get; private set; }
        internal BwtPriorityProfileShape Shape { get; private set; }
        internal IReadOnlyList<BwtPriorityConstantEdit> Edits { get; private set; }

        internal string Validate()
        {
            foreach (BwtPriorityConstantEdit edit in Edits)
            {
                if (edit == null) return "an edit binding is missing";
                string error = edit.Validate();
                if (error != null) return error;
            }
            return null;
        }
    }
}

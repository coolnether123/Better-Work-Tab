using System;
using Spine.Api;
using Better_Work_Tab.Foundation;

namespace Spine.RimWorld.Api
{
    public enum RenderCompatibilityClassification
    {
        OptimizedSafe,
        AdapterRequired,
        LegacyOnly
    }

    public readonly struct RimWorldRenderCompatibilityRequest
    {
        public RimWorldRenderCompatibilityRequest(
            string scopeId,
            string subjectId,
            SemanticVersion rimWorldVersion,
            SpineApiDescriptor spineApi)
        {
            if (string.IsNullOrWhiteSpace(scopeId)) throw new ArgumentException("A compatibility scope ID is required.", nameof(scopeId));
            if (string.IsNullOrWhiteSpace(subjectId)) throw new ArgumentException("A compatibility subject ID is required.", nameof(subjectId));

            ScopeId = scopeId;
            SubjectId = subjectId;
            RimWorldVersion = rimWorldVersion;
            SpineApi = spineApi;
        }

        public string ScopeId { get; }
        public string SubjectId { get; }
        public SemanticVersion RimWorldVersion { get; }
        public SpineApiDescriptor SpineApi { get; }
    }

    public readonly struct RimWorldRenderCompatibilityResult
    {
        public RimWorldRenderCompatibilityResult(RenderCompatibilityClassification classification, string reason)
        {
            Classification = classification;
            Reason = reason ?? string.Empty;
        }

        public RenderCompatibilityClassification Classification { get; }
        public string Reason { get; }
    }

    public interface IRimWorldRenderCompatibilityProvider
    {
        string Id { get; }
        int Priority { get; }
        SpineApiDescriptor ApiDescriptor { get; }
        bool TryClassify(RimWorldRenderCompatibilityRequest request, out RimWorldRenderCompatibilityResult result);
    }

    public readonly struct RimWorldRenderFallbackContext
    {
        public RimWorldRenderFallbackContext(string scopeId, RimWorldRenderCompatibilityResult compatibility, Exception failure = null)
        {
            ScopeId = scopeId ?? string.Empty;
            Compatibility = compatibility;
            Failure = failure;
        }

        public string ScopeId { get; }
        public RimWorldRenderCompatibilityResult Compatibility { get; }
        public Exception Failure { get; }
    }

    public readonly struct RimWorldRenderFallbackDecision
    {
        public RimWorldRenderFallbackDecision(bool useLegacyRenderer, string reason)
        {
            UseLegacyRenderer = useLegacyRenderer;
            Reason = reason ?? string.Empty;
        }

        public bool UseLegacyRenderer { get; }
        public string Reason { get; }
    }

    public interface IRimWorldRenderFallbackPolicy
    {
        RimWorldRenderFallbackDecision Decide(RimWorldRenderFallbackContext context);
    }
}

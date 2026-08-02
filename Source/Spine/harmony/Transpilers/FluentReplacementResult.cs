namespace Spine.Harmony
{
    public enum FluentReplacementResult
    {
        NoMatch,
        PatternReplaced,
        FallbackCallReplaced,
        ReplacementAlreadyPresent,
        AlreadyPatched,
        AmbiguousMatch,
        UnsafeMatch,
        Failed
    }

    public static class FluentReplacementResultExtensions
    {
        public static bool Succeeded(this FluentReplacementResult result)
        {
            return result == FluentReplacementResult.PatternReplaced ||
                result == FluentReplacementResult.FallbackCallReplaced ||
                result == FluentReplacementResult.ReplacementAlreadyPresent ||
                result == FluentReplacementResult.AlreadyPatched;
        }
    }
}

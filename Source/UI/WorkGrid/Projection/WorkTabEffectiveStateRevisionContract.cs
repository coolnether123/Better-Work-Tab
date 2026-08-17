namespace Better_Work_Tab.UI.WorkGrid.Projection
{
    /// <summary>
    /// Common cache-side check for effective-state entries. Consumers can store
    /// the token with a snapshot and reject it when either the provider identity,
    /// source kind, or local revision no longer matches.
    /// </summary>
    public static class WorkTabEffectiveStateRevisionContract
    {
        public static WorkTabEffectiveStateRevision Capture(
            IWorkTabEffectiveStateRevisionSource source)
        {
            return source == null ? default(WorkTabEffectiveStateRevision) : source.RevisionToken;
        }

        public static bool IsCurrent(
            WorkTabEffectiveStateRevision token,
            IWorkTabEffectiveStateRevisionSource source)
        {
            return source != null && token.Equals(source.RevisionToken);
        }

        public static bool RejectStale(
            WorkTabEffectiveStateRevision token,
            IWorkTabEffectiveStateRevisionSource source)
        {
            return !IsCurrent(token, source);
        }
    }
}

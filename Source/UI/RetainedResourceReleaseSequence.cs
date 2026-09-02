using System;

namespace Better_Work_Tab.UI
{
    /// <summary>
    /// Releases independent retained-surface owners without allowing one broken
    /// Unity resource to keep the other owners alive through game teardown.
    /// </summary>
    internal static class RetainedResourceReleaseSequence
    {
        internal static void Release(
            Action releaseRows,
            Action releaseHeaders,
            Action<string, Exception> reportFailure)
        {
            if (releaseRows == null) throw new ArgumentNullException(nameof(releaseRows));
            if (releaseHeaders == null) throw new ArgumentNullException(nameof(releaseHeaders));
            if (reportFailure == null) throw new ArgumentNullException(nameof(reportFailure));

            Exception rowFailure = TryRelease(releaseRows);
            Exception headerFailure = TryRelease(releaseHeaders);

            ReportFailure(reportFailure, "retained work-grid rows", rowFailure);
            ReportFailure(reportFailure, "retained priority headers", headerFailure);
        }

        internal static void ReleaseHeader(
            Action releaseHeaders,
            Action<string, Exception> reportFailure)
        {
            if (releaseHeaders == null) throw new ArgumentNullException(nameof(releaseHeaders));
            if (reportFailure == null) throw new ArgumentNullException(nameof(reportFailure));

            ReportFailure(
                reportFailure,
                "retained priority headers",
                TryRelease(releaseHeaders));
        }

        private static Exception TryRelease(Action release)
        {
            try
            {
                release();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static void ReportFailure(
            Action<string, Exception> reportFailure,
            string resourceGroup,
            Exception failure)
        {
            if (failure == null)
            {
                return;
            }

            try
            {
                reportFailure(resourceGroup, failure);
            }
            catch (Exception)
            {
                // Shutdown logging cannot be allowed to interrupt a remaining release.
            }
        }
    }
}

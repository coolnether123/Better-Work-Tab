using System;

namespace Spine.Api
{
    public enum RenderDiagnosticSeverity
    {
        Information,
        Warning,
        Error
    }

    public readonly struct RenderDiagnostic
    {
        public RenderDiagnostic(
            RenderDiagnosticSeverity severity,
            string sourceId,
            string message,
            Exception exception = null)
        {
            Severity = severity;
            SourceId = sourceId ?? string.Empty;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        public RenderDiagnosticSeverity Severity { get; }
        public string SourceId { get; }
        public string Message { get; }
        public Exception Exception { get; }
    }

    public interface IRenderDiagnosticsSink
    {
        bool Enabled { get; }
        void Record(RenderDiagnostic diagnostic);
    }
}

namespace Spine.Diagnostics
{
    using Spine.Api;

    public sealed class NullRenderDiagnosticsSink : IRenderDiagnosticsSink
    {
        public static readonly NullRenderDiagnosticsSink Instance = new NullRenderDiagnosticsSink();

        private NullRenderDiagnosticsSink()
        {
        }

        public bool Enabled => false;

        public void Record(RenderDiagnostic diagnostic)
        {
        }
    }
}

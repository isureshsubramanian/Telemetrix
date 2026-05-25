namespace Telemetrix.Models;

/// <summary>Mirror of <see cref="System.Diagnostics.ActivityKind"/> with stable integer values.</summary>
public enum SpanKind
{
    /// <summary>Internal operation within an application (the default).</summary>
    Internal = 0,

    /// <summary>Handling of an inbound request (for example an HTTP endpoint).</summary>
    Server = 1,

    /// <summary>An outbound request to a remote dependency.</summary>
    Client = 2,

    /// <summary>A message published to a broker or queue.</summary>
    Producer = 3,

    /// <summary>A message consumed from a broker or queue.</summary>
    Consumer = 4,
}

/// <summary>Identifies how a <see cref="SpanRecord"/> was captured.</summary>
public enum SpanSource
{
    /// <summary>Captured from an OpenTelemetry <see cref="System.Diagnostics.Activity"/>.</summary>
    Activity = 0,

    /// <summary>Synthesised from a database command captured via <c>DiagnosticListener</c>.</summary>
    Sql = 1,
}

/// <summary>Mirror of <see cref="System.Diagnostics.ActivityStatusCode"/>.</summary>
public enum SpanStatus
{
    /// <summary>No explicit status was recorded.</summary>
    Unset = 0,

    /// <summary>The operation completed successfully.</summary>
    Ok = 1,

    /// <summary>The operation failed.</summary>
    Error = 2,
}

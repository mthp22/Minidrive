namespace Minidrive.Domain.Enums;

/// <summary>
/// Explicit states of a multipart upload session. Transitions are validated so that
/// concurrent or duplicated client requests cannot corrupt the workflow.
/// </summary>
public enum UploadStatus
{
    /// <summary>Session row created locally; the provider multipart upload may not exist yet.</summary>
    Initiating = 0,

    /// <summary>Provider multipart upload created; parts may be uploaded.</summary>
    Active = 1,

    /// <summary>A completion request is in flight and must not be processed twice.</summary>
    Completing = 2,

    /// <summary>Multipart upload completed and the file node finalized.</summary>
    Completed = 3,

    /// <summary>An abort request is in flight.</summary>
    Aborting = 4,

    /// <summary>Multipart upload aborted; the pending node and stored parts are discarded.</summary>
    Aborted = 5,

    /// <summary>Unrecoverable failure while initiating, completing or aborting.</summary>
    Failed = 6,

    /// <summary>Session passed its expiry without completing and was cleaned up.</summary>
    Expired = 7,
}

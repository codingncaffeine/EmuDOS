namespace EmuDOS.Core.Engine;

/// <summary>Lifecycle state of a running <see cref="IDosSession"/>.</summary>
public enum EngineState
{
    /// <summary>Created but not yet started.</summary>
    Idle,

    Running,

    Paused,

    /// <summary>Stopped normally; the session is finished and should be disposed.</summary>
    Stopped,

    /// <summary>Stopped due to an error.</summary>
    Faulted,
}

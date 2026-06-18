using EmuDOS.Core.Model;

namespace EmuDOS.Core.Engine;

/// <summary>
/// An available DOS engine (e.g. the dosbox_pure libretro core). EmuDOS is engine-agnostic:
/// everything above this interface speaks <see cref="GameProfile"/> / <see cref="GameInstance"/>,
/// and only an engine implementation knows how to actually run them.
/// </summary>
public interface IDosEngine
{
    /// <summary>Stable identifier, e.g. <c>"dosbox_pure"</c>.</summary>
    string Id { get; }

    string DisplayName { get; }

    EngineCapabilities Capabilities { get; }

    /// <summary>
    /// Create a session for <paramref name="instance"/>, rendering and playing into
    /// <paramref name="host"/>. The session starts <see cref="EngineState.Idle"/>; call
    /// <see cref="IDosSession.Start"/> to run it.
    /// </summary>
    IDosSession CreateSession(GameInstance instance, IEngineHost host);
}

using UnityEngine;

namespace ArcheryTrickShot.CharacterTesting
{
    /// <summary>
    /// Backward-compatible name from the v2.x validation scene.
    /// New tests use HumanoidArcherGameplayAdapter directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterGameplayTestAdapter : HumanoidArcherGameplayAdapter
    {
    }
}

using UnityEngine;

namespace ArcheryTrickShot.CharacterTesting
{
    /// <summary>
    /// Deprecated compatibility component.
    ///
    /// Older EmberGameplayTest scenes serialized this component. It is now a
    /// deliberate no-op so a stale test scene can never replace BowController's
    /// production Archer3DRuntimeProfile through reflection again.
    /// Character choice belongs exclusively to ArcherCharacterRoster.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    public sealed class ProductionArcherProfileSwapTest : MonoBehaviour
    {
        public void Configure(
            BowController controller,
            Archer3DRuntimeProfile profile,
            string displayName)
        {
            // Intentionally empty. Retained only so old editor/test code and
            // serialized scenes remain loadable while being migrated.
        }

        private void Awake()
        {
            Debug.LogWarning(
                "[CharacterRigTest] Deprecated profile-swap adapter ignored. " +
                "Use ArcherCharacterRoster / production Level01 instead.",
                this);

            enabled = false;
        }
    }
}

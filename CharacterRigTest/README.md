# CharacterRigTest v2.5

This is an isolated Ember test using the existing production Archer3D runtime pipeline.

v2.4 attempted to tear down/rebuild the production archer after startup from a coroutine.
That could remain at `Replacing production character profile...` while the original character
stayed active.

v2.5 moves the profile injection to startup ordering:

1. Production `BowController` Awake runs first (`-200`).
2. Test profile bridge Awake runs next (`-150`).
3. The original visual is synchronously removed.
4. `Archer3DRuntimeFactory.Ensure` creates Ember using the cloned production profile.
5. Production callbacks are reconnected.
6. Normal LevelManager/gameplay Start proceeds with Ember already active.

No procedural Ember IK or alternate projectile logic is used.

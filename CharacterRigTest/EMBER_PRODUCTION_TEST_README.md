# Ember production test

The old `EmberGameplayTest` runtime profile-swap experiment is retired.

Use:

`Tools > Archery Trick Shot > Character Rig Test > Create Ember Gameplay Test`

The menu now selects Ember through `ArcherCharacterRoster`, repairs the PBR material using the same production shader contract as Khaem/Nerissa, opens `Assets/ArcheryTrickShot/Scenes/Level01.unity`, and removes the obsolete Ember test scene/profile/prefab.

`CharacterRigTest` remains useful only for validating a new Humanoid rig before onboarding it to the production roster.

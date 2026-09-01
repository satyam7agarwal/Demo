# Khaem Runtime Integration

This package makes Khaem the active runtime archer through
`Resources/Archer3D/ArcherCharacterRoster.asset`. Khaem's reusable tuning remains
in `Resources/Archer3D/DefaultArcher3D.asset` under stable ID `khaem`.

## What is reused

- Khaem humanoid character, material and imported avatar
- Kevin Iglesias Idle, Load, Hold and Release Humanoid animations
- Kevin's authored `HumanArcher_Bow` mesh, bow bones, tips, nock and LineRenderer
- Existing Archery Trick Shot `BowController`, `ArrowController`, mirror reflection,
  level data, input, UI and audio

Kevin's demo controller is never allowed to launch the gameplay arrow. It is used
only as an optional source of original bow references when the Kevin character is
selected. With Khaem, those same references are resolved directly from the bow
prefab.

## First test

1. Replace the project's `Assets` folder while Unity is closed.
2. Open the project and wait for Unity to finish importing and compiling.
3. Confirm there are no red Console errors.
4. Open `Assets/ArcheryTrickShot/Scenes/KhaemRigTest.unity` for the isolated test.
5. In Play Mode use `1`, `2`, `3`, `4` for Idle, Load, Hold and Release.
6. Open `Level01.unity` and test horizontal, upward and downward shots before an
   Android build.

No Inspector assignment is required for the gameplay scene. Khaem, the authored
bow and runtime alignment are resolved from the roster and character profile.

## Safe rollback

The previous Kevin runtime settings are preserved under stable ID `kevin` in
`Resources/Archer3D/KevinArcher3D.asset`.

To make Kevin the default, open
`Tools > Archery Trick Shot > Setup Smooth 3D Archer`, select the Kevin profile,
enable `Set As Default`, and run `Create / Update Smooth 3D Archer`.

## Adding future characters

1. Import the model and set its rig to Humanoid.
2. Open `Tools > Archery Trick Shot > Setup Smooth 3D Archer`.
3. Enter a stable ID/name and assign the character prefab.
4. Assign either its authored bow or the shared bow.
5. Visually verify the one-time bow grip/orientation calibration.
6. Click `Create / Update Smooth 3D Archer`.

The tool creates and registers the profile automatically. No gameplay scene,
level data, BowController, ArrowController, mirror, scoring or UI edits are
needed for additional characters. A different skeleton may still require a
one-time visual grip offset because Humanoid hand axes and proportions vary;
that correction is stored in the profile and is not repeated per scene/level.

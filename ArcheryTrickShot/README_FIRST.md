# Archery Trick Shot — Clean Mobile Test Build

This folder is the cleaned `Assets/ArcheryTrickShot` package for the current runtime architecture.

## Current gameplay architecture

The old captured 2D archer pipeline has been removed. The gameplay archer is now the real Kevin Iglesias `HumanF_Archer` humanoid running at runtime:

- Kevin Idle / Load / Hold / Release animation clips
- continuous direct-bone arm aiming
- nonlinear Spine/Chest/Head posture at stronger aim angles
- stable bow-to-hand binding
- Kevin's original bow limbs, tips, nock point and original bowstring `LineRenderer`
- the separate 2D `ArrowController` remains the real physics projectile

Do not re-add the old directional sprite/Archer2D pipeline.

## Result stars fix

Result stars are no longer TMP Unicode characters.

The UI now builds three procedural `StarGraphic` objects in code, so `LiberationSans SDF` does not need U+2605/U+2606. This removes the warning:

`The character with Unicode value \u2605 was not found in [LiberationSans SDF]...`

No star PNG or font-atlas modification is required.

## Install

1. Commit/back up the current Unity project.
2. Replace only:
   `Assets/ArcheryTrickShot`
   with this cleaned folder.
3. Keep the existing third-party:
   `Assets/Kevin Iglesias`
   folder in the Unity project.
4. Open:
   `Assets/ArcheryTrickShot/Scenes/Level01.unity`
5. Wait for Unity import/compile to finish.
6. Confirm there are zero red Console errors.
7. Press Play.

The current archer setup expects the Kevin Iglesias package because `KevinBowRuntimeController` deliberately reuses the original serialized bow/string references from `HumanArcherController`.

## Mobile test

The package already includes:

- landscape-left/right only for Android/iOS
- portrait disabled
- `Application.targetFrameRate` from `GameConfig` (default 60)
- `Scale With Screen Size` runtime UI
- safe-area fitting for notches/cutouts
- touch and mouse input through the Input System

For the first Android device test, run Level 1 and Level 2 and verify:

- character body/arms/bow follow aim smoothly
- original Kevin string stays attached to both real bow tips and nock
- held/flying arrow transition is visually aligned
- trajectory follows touch continuously
- hit/miss/result overlays work
- 1/2/3 earned stars render as graphics with no TMP Unicode warning
- pause/resume/retry/next-level work
- both landscape orientations are usable

## Current data ownership

- `Resources/Levels/Level_*.asset`: per-level layout, shots and objects
- `Resources/GameConfig.asset`: global gameplay/UI/mobile tuning
- `Resources/Archer3D/ArcherCharacterRoster.asset`: default/available character IDs
- `Resources/Archer3D/DefaultArcher3D.asset`: Khaem character profile
- `Resources/Archer3D/KevinArcher3D.asset`: Kevin rollback profile
- `LevelManager`: lifecycle/progression
- `BowController`: input + aim + handoff to projectile
- `Archer3DVisualController`: continuous character pose
- `KevinBowRuntimeController`: original Kevin bow/string internals
- `ArrowController`: real projectile physics
- `GameUIBuilder` / `GameUIController`: code-generated UI

## Editor menu

Only the current archer rebuild tool remains:

`Tools -> Archery Trick Shot -> Setup Smooth 3D Archer`

You do not need to run it just to test the included Level01 build. Use it when
adding or recalibrating a Humanoid character. The tool creates one reusable
profile, registers it in `ArcherCharacterRoster`, and can make it the default.
It does not require scene, level, BowController or projectile changes.

## Cleanup performed

Removed obsolete development assets that are no longer part of the runtime architecture:

- captured 2D archer PNG frames, including directional captures
- Archer2D controller/animations
- directional capture/build profiles and editor tools
- old 2D archer runtime profile/factory/controller
- Android sprite optimizer for the obsolete 2D archer frames
- old Archer test/sample scenes
- unused BowLauncher sprite
- placeholder-only README/audio folders

The source `HumanF_ArcherCaptureProfile` and `ArcherCaptureProfile.cs` are intentionally retained because the current 3D setup editor uses them to rebuild the 3D runtime assets.

# Scalable Character System

## Runtime flow

`BowController` keeps its existing gameplay responsibilities and asks
`Archer3DRuntimeProfile.LoadDefault()` for presentation data. That lookup now
resolves the saved/default stable character ID through
`ArcherCharacterRoster`, then passes one profile to the existing runtime
factory.

Profiles contain only character-specific presentation data:

- character prefab and player-facing name
- Humanoid bow/draw hand mapping
- automatic world-height scaling and root orientation
- authored/shared bow prefab
- bow binding mode and one-time grip/orientation/depth calibration
- held-arrow and animation-state contract

Core arrow physics, aiming, mirrors, scoring, level data and UI remain shared.

## Future character onboarding

Use `Tools > Archery Trick Shot > Setup Smooth 3D Archer`. Supply a stable ID,
display name, Humanoid prefab and optional bow. The tool creates or updates one
profile, detects internal bow/held-arrow paths when available, registers the
profile once, and optionally makes it the roster default.

This avoids per-scene and per-level setup. The only manual work that cannot be
reliably eliminated is a one-time visual check for character-specific wrist
axes, body proportions and grip placement. Those values are saved in the
profile and reused everywhere.

## Character selection API

Future character-select UI can persist a choice without changing gameplay:

```csharp
ArcherCharacterRoster roster =
    ArcherCharacterRoster.LoadDefault();

roster.SelectCharacter("kevin");
```

The selected profile is used the next time the gameplay archer is created. If
a saved ID is missing, the roster safely falls back to `DefaultCharacterId`.

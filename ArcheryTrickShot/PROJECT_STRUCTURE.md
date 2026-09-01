# Current project structure

```text
Assets/
├── ArcheryTrickShot/
│   ├── Animations/
│   │   └── Archer3D/
│   │       ├── Archer3D.controller
│   │       ├── Idle.anim
│   │       ├── Load.anim
│   │       ├── Hold.anim
│   │       └── Release.anim
│   ├── Art/
│   │   └── Sprites/
│   │       └── Gameplay/
│   │           ├── Arrow.png
│   │           ├── GameplayBackground.png
│   │           └── Target.png
│   ├── Resources/
│   │   ├── Archer3D/
│   │   │   ├── ArcherCharacterRoster.asset
│   │   │   ├── DefaultArcher3D.asset   # Khaem profile
│   │   │   ├── KevinArcher3D.asset     # rollback profile
│   │   │   └── Characters/             # future generated profiles
│   │   ├── GameConfig.asset
│   │   ├── Levels/
│   │   │   ├── Level_001.asset
│   │   │   └── Level_002.asset
│   │   └── Prefabs/
│   │       └── Gameplay/
│   │           ├── Arrow.prefab
│   │           ├── Bow.prefab
│   │           ├── Target.prefab
│   │           ├── Wall.prefab
│   │           └── Mirror.prefab
│   ├── Scenes/
│   │   └── Level01.unity
│   ├── HumanF_ArcherCaptureProfile.asset
│   └── Scripts/
│       ├── ArcherCaptureTool/
│       │   └── ArcherCaptureProfile.cs
│       ├── Core/
│       │   ├── GameConfig.cs
│       │   ├── LevelData.cs
│       │   └── LevelManager.cs
│       ├── Gameplay/
│       │   ├── AimTrajectoryRenderer.cs
│       │   ├── ArcherCharacterRoster.cs
│       │   ├── Archer3DRuntimeFactory.cs
│       │   ├── Archer3DRuntimeProfile.cs
│       │   ├── Archer3DVisualController.cs
│       │   ├── KevinBowRuntimeController.cs
│       │   ├── ArrowController.cs
│       │   ├── BowController.cs
│       │   ├── Target.cs
│       │   ├── Wall.cs
│       │   └── Mirror.cs
│       ├── Presentation/
│       │   ├── BackgroundScaler.cs
│       │   ├── GameAudioController.cs
│       │   ├── GameFeelController.cs
│       │   ├── GameUIBuilder.cs
│       │   ├── GameUIController.cs
│       │   ├── GameUIView.cs
│       │   ├── StarGraphic.cs
│       │   └── SafeAreaFitter.cs
│       ├── Platform/
│       │   └── MobilePlatformBootstrap.cs
│       └── Editor/
│           ├── Archer3DRuntimeSetupWindow.cs
│           └── MobileBuildPreprocessor.cs
│
├── Kevin Iglesias/       # required third-party source assets; keep existing folder
├── Settings/
└── TextMesh Pro/
```

## Runtime ownership

`LevelData` owns level-specific data. `GameConfig` owns global gameplay/UI/mobile
tuning. `ArcherCharacterRoster` chooses a profile by stable character ID, and
each `Archer3DRuntimeProfile` owns one character's prefab, size, hand mapping
and bow calibration. Runtime code owns spawning, input, continuous body posing,
bow/string integration, projectile physics, UI state, safe-area handling and
progression.

The result-star UI is fully procedural and does not depend on a TMP star glyph or a star texture asset.

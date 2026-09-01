# Premium gameplay visual pass — Levels 1–10

- GameplayBackground.png now uses the low-contrast Ancient Ruins background.
- Mirror.prefab uses Art/Environment/Mirror_Premium.png. The reflection collider remains a thin straight trigger and Mirror.cs physics is unchanged.
- Wall.prefab uses Art/Environment/Wall_Premium.png with a rectangular BoxCollider2D.
- Levels 1–7 keep their proven geometric routes but use uniform premium-art scale and corrected wall rotations.
- Level 6 explicitly faces the target right because its intended final ricochet approaches from the right.
- Levels 8–10 add angled double ricochet, reverse-face approach, and a triple-ricochet trial.
- Target art remains Wood for these levels because the current scoring markers/collision calibration were authored against Target_Wood.
- ArrowSpeed restored from the temporary test value 2 to production value 12.

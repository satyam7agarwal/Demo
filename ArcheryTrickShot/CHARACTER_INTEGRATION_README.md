# ArcheryTrickShot Scalable Character Integration

## Runtime policy

- Default playable character: **Khaem**.
- Rollback character: **Kevin** (legacy authored-hand/bow path preserved).
- Gameplay authority stays in the existing v18 systems (`BowController`, `ArrowController`, levels, mirrors, targets, scoring).
- Character code is presentation/adaptation only.

## Normal future character source: Hyper / Mixamo Humanoid

1. Import the model/prefab and configure its Avatar as **Humanoid**.
2. Select the imported GameObject asset in the Project window.
3. Run:
   `Tools > Archery Trick Shot > Characters > Create Hyper-Mixamo Archer From Selected`
4. The tool creates/registers a reusable profile that shares the existing archery Animator and bow.
5. Test the character in `Level01`.

The runtime adapter automatically resolves standard Humanoid hands and Mixamo-style `Index1/Index2` finger chains. The bow stays in the camera-facing 2D plane instead of inheriting retargeted wrist rotation. The string/held-arrow nock follows a finger-derived draw socket rather than the wrist pivot.

## Rare fallback

If a non-standard Humanoid has no mapped/named finger chain, the system falls back to the draw-hand pivot and logs one warning. `BowGripSocketLocalCorrection` and `DrawNockSocketLocalCorrection` exist only for unusual rigs; normal Hyper/Mixamo characters should keep them at zero.

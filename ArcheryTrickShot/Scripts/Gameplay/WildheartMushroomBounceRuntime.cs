using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Reusable spring-mushroom integration for authored LevelData presentation objects.
///
/// A PresentationSprite named MushroomBounce_{id}_Visual and a trigger
/// PresentationCollider named MushroomBounce_{id}_Surface opt into a bumper.
/// Rotation of the thin collider determines its surface normal (+local X),
/// exactly as for the existing Mirror ricochet system. Both ArrowController
/// and AimTrajectoryRenderer therefore agree on the real reflected trajectory.
/// No level-number-specific logic, physics overrides or Inspector steps.
/// </summary>
[DefaultExecutionOrder(1750)]
[DisallowMultipleComponent]
public sealed class WildheartMushroomBounceRuntime : MonoBehaviour
{
    private const string Prefix = "MushroomBounce_";
    private const string SurfaceSuffix = "_Surface";
    private const string VisualSuffix = "_Visual";
    private const string BoingResource = "Audio/SFX/Wildheart_MushroomBoing";

    private Transform observedLevelObjects;
    private static readonly FieldInfo CurrentLevelField =
        typeof(LevelManager).GetField("currentLevel", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo PreviewEnabledField =
        typeof(LevelManager).GetField("fullTrajectoryPreviewEnabled", BindingFlags.Instance | BindingFlags.NonPublic);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager manager = UnityEngine.Object.FindFirstObjectByType<LevelManager>();
        if (manager != null && manager.GetComponent<WildheartMushroomBounceRuntime>() == null)
            manager.gameObject.AddComponent<WildheartMushroomBounceRuntime>();
    }

    private void LateUpdate()
    {
        Transform objects = transform.Find("LevelObjects");
        if (objects == null)
        {
            observedLevelObjects = null;
            return;
        }

        if (objects == observedLevelObjects)
            return;

        observedLevelObjects = objects;
        var visuals = new Dictionary<string, SpriteRenderer>(StringComparer.Ordinal);
        foreach (SpriteRenderer renderer in objects.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer == null || renderer.transform.parent != objects)
                continue;
            visuals[renderer.gameObject.name] = renderer;
        }

        int installed = 0;
        AudioClip boingClip = Resources.Load<AudioClip>(BoingResource);

        foreach (Collider2D collider in objects.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider == null || collider.transform.parent != objects)
                continue;

            string name = collider.gameObject.name;
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) ||
                !name.EndsWith(SurfaceSuffix, StringComparison.Ordinal))
                continue;

            string visualName = name.Substring(0, name.Length - SurfaceSuffix.Length) + VisualSuffix;
            if (!visuals.TryGetValue(visualName, out SpriteRenderer visual) || visual == null || visual.sprite == null)
            {
                Debug.LogWarning("[Wildheart Mushroom] Missing authored sprite " + visualName, collider);
                continue;
            }

            // A solid obstacle would stop the arrow before the ricochet callback.
            // Mirror expects the same trigger plane used by trajectory BoxCast.
            collider.isTrigger = true;

            if (collider.GetComponent<Mirror>() == null)
                collider.gameObject.AddComponent<Mirror>();

            WildheartMushroomBounceSurface spring =
                collider.GetComponent<WildheartMushroomBounceSurface>();
            if (spring == null)
                spring = collider.gameObject.AddComponent<WildheartMushroomBounceSurface>();
            spring.Configure(visual.transform, boingClip);
            installed++;
        }

        if (installed > 0)
        {
            // The level opts in by having authored bounce colliders, NOT by
            // naming a particular character. This keeps character mechanics
            // separate from levels and works with Khaem now and Mochi later.
            // The existing full-path renderer is valid only for straight
            // projectiles; do not substitute it for Nerissa's gravity guide.
            LevelManager manager = GetComponent<LevelManager>();
            LevelData level = CurrentLevelField?.GetValue(manager) as LevelData;
            ArcherGameplayTraitProfile trait =
                ArcherGameplayTraitResolver.ResolveSelected();
            bool straightProjectile = trait == null || !trait.UsesGravityArc;
            if (level != null && straightProjectile)
            {
                PreviewEnabledField?.SetValue(manager, true);
                BowController bow = UnityEngine.Object.FindFirstObjectByType<BowController>();
                GameUIController ui = UnityEngine.Object.FindFirstObjectByType<GameUIController>();
                bow?.SetFullTrajectoryPreviewEnabled(true);
                ui?.SetFullTrajectoryPreviewEnabled(true);
            }
            else if (level != null && !straightProjectile)
            {
                Debug.LogWarning(
                    "[Wildheart Mushroom] Full straight-path prediction is " +
                    "disabled for this gravity archer. Select Khaem for Level 22.",
                    manager);
            }
            Physics2D.SyncTransforms();
        }
    }
}


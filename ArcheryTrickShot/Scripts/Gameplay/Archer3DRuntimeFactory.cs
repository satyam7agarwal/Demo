using System;
using UnityEngine;

public static class Archer3DRuntimeFactory
{
    private const string RuntimeChildName = "Archer3DVisual";
    private const string LegacySpriteChildName = "ArcherVisual";

    public static Archer3DVisualController Ensure(
        Transform gameplayArcherRoot,
        Archer3DRuntimeProfile profile)
    {
        if (gameplayArcherRoot == null || profile == null)
            return null;

        DisableLegacySpriteArcher(gameplayArcherRoot);

        Transform existing =
            gameplayArcherRoot.Find(RuntimeChildName);

        if (existing != null)
        {
            Archer3DVisualController existingController =
                existing.GetComponentInChildren<
                    Archer3DVisualController>(true);

            if (existingController != null &&
                existingController.Profile != null &&
                existingController.Profile != profile)
            {
                // Disable the old visual immediately before deferred Destroy().
                // This prevents a one-frame Khaem+Nerissa overlap on slower Android frames.
                GameObject oldVisual = existing.gameObject;
                oldVisual.SetActive(false);
                Renderer[] oldRenderers = oldVisual.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < oldRenderers.Length; rendererIndex++)
                {
                    if (oldRenderers[rendererIndex] != null)
                        oldRenderers[rendererIndex].enabled = false;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(oldVisual);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(oldVisual);
                }

                existing = null;
            }
        }

        GameObject characterRoot;

        if (existing != null)
        {
            characterRoot = existing.gameObject;
        }
        else
        {
            if (profile.ArcherPrefab == null)
            {
                Debug.LogError(
                    "Archer3DRuntimeProfile has no ArcherPrefab.");
                return null;
            }

            characterRoot =
                UnityEngine.Object.Instantiate(
                    profile.ArcherPrefab,
                    gameplayArcherRoot);

            characterRoot.name = RuntimeChildName;
        }

        characterRoot.transform.localPosition =
            profile.LocalOffset;

        characterRoot.transform.localRotation =
            Quaternion.Euler(profile.LocalEulerAngles);

        characterRoot.transform.localScale =
            profile.LocalScale;

        // Materials are presentation data, not part of the rigged source prefab.
        // Apply the profile override immediately after instantiation so the
        // original Mixamo/Hyper FBX remains the stable animation/rig source.
        // The shared runtime bow is created later by Archer3DVisualController,
        // therefore this cannot accidentally overwrite the bow material.
        ApplyCharacterMaterialOverride(
            characterRoot,
            profile);

        ApplyAutomaticHeight(
            characterRoot.transform,
            profile);

        DisableOnlyConfiguredDemoBehaviours(
            characterRoot,
            profile);

        Animator animator =
            characterRoot.GetComponentInChildren<Animator>(true);

        if (animator == null)
        {
            Debug.LogError(
                "Human archer prefab has no Animator.",
                characterRoot);
            return null;
        }

        animator.enabled = false;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController =
            profile.AnimatorController;

        Archer3DVisualController controller =
            animator.GetComponent<Archer3DVisualController>();

        if (controller == null)
        {
            controller =
                animator.gameObject
                    .AddComponent<Archer3DVisualController>();
        }

        controller.Configure(
            profile,
            characterRoot.transform);

        animator.enabled = true;

        if (profile.AnimatorController != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }

        CalibrateNockToGameplayRoot(
            gameplayArcherRoot,
            characterRoot.transform,
            animator,
            controller,
            profile);

        return controller;
    }

    private static void ApplyCharacterMaterialOverride(
        GameObject characterRoot,
        Archer3DRuntimeProfile profile)
    {
        if (characterRoot == null ||
            profile == null ||
            profile.CharacterMaterialOverride == null)
        {
            return;
        }

        Renderer[] renderers =
            characterRoot.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];

            if (renderer == null ||
                renderer is LineRenderer ||
                renderer is TrailRenderer)
            {
                continue;
            }

            Material[] slots = renderer.sharedMaterials;

            if (slots == null || slots.Length == 0)
            {
                renderer.sharedMaterial =
                    profile.CharacterMaterialOverride;
                continue;
            }

            if (!profile.ApplyCharacterMaterialToAllSlots)
            {
                slots[0] = profile.CharacterMaterialOverride;
                renderer.sharedMaterials = slots;
                continue;
            }

            for (int slot = 0; slot < slots.Length; slot++)
            {
                slots[slot] =
                    profile.CharacterMaterialOverride;
            }

            renderer.sharedMaterials = slots;
        }
    }

    private static void ApplyAutomaticHeight(
        Transform characterRoot,
        Archer3DRuntimeProfile profile)
    {
        if (characterRoot == null ||
            profile == null ||
            !profile.AutoScaleToDesiredHeight ||
            profile.DesiredWorldHeight <= 0.01f)
        {
            return;
        }

        Renderer[] renderers =
            characterRoot
                .GetComponentsInChildren<Renderer>(true);

        bool hasBounds = false;
        Bounds bodyBounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null ||
                !renderer.enabled ||
                !renderer.gameObject.activeInHierarchy ||
                renderer is LineRenderer ||
                renderer is TrailRenderer)
            {
                continue;
            }

            string lowerName =
                renderer.name.ToLowerInvariant();

            if (lowerName.Contains("bow") ||
                lowerName.Contains("arrow") ||
                lowerName.Contains("quiver") ||
                lowerName.Contains("trail"))
            {
                continue;
            }

            if (!hasBounds)
            {
                bodyBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bodyBounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds || bodyBounds.size.y <= 0.0001f)
        {
            Debug.LogWarning(
                "[Smooth Archer] Automatic height could not resolve body bounds; " +
                "using the profile's Local Scale.",
                characterRoot);
            return;
        }

        float multiplier =
            Mathf.Clamp(
                profile.DesiredWorldHeight /
                bodyBounds.size.y,
                0.05f,
                20f);

        characterRoot.localScale =
            Vector3.Scale(
                characterRoot.localScale,
                new Vector3(
                    multiplier,
                    multiplier,
                    multiplier));
    }

    private static void DisableLegacySpriteArcher(
        Transform gameplayRoot)
    {
        Transform legacy =
            gameplayRoot.Find(LegacySpriteChildName);

        if (legacy != null)
            legacy.gameObject.SetActive(false);
    }

    private static void DisableOnlyConfiguredDemoBehaviours(
        GameObject characterRoot,
        Archer3DRuntimeProfile profile)
    {
        if (profile.DisableBehaviourTypeNames == null ||
            profile.DisableBehaviourTypeNames.Length == 0)
        {
            return;
        }

        foreach (MonoBehaviour behaviour
                 in characterRoot
                    .GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            string typeName =
                behaviour.GetType().Name;

            foreach (string disabledType
                     in profile.DisableBehaviourTypeNames)
            {
                if (string.IsNullOrWhiteSpace(disabledType))
                    continue;

                if (!typeName.Equals(
                        disabledType.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                behaviour.enabled = false;
                break;
            }
        }
    }

    private static void CalibrateNockToGameplayRoot(
        Transform gameplayRoot,
        Transform characterRoot,
        Animator animator,
        Archer3DVisualController controller,
        Archer3DRuntimeProfile profile)
    {
        if (animator == null ||
            controller == null ||
            profile.AnimatorController == null ||
            string.IsNullOrWhiteSpace(profile.HoldStateName))
        {
            return;
        }

        // Preserve the existing LevelData.ArcherPosition semantics:
        // the gameplay Bow root remains the arrow/nock anchor.
        // We temporarily sample straight Hold, measure the draw hand, then shift
        // the whole 3D character so that the nock lands exactly on the gameplay root.
        animator.Play(profile.HoldStateName, 0, 0.5f);
        animator.Update(0f);

        Vector3 nockWorld =
            controller.NockWorldPosition;

        Vector3 desiredWorld =
            gameplayRoot.position;

        Vector3 delta =
            desiredWorld - nockWorld;

        characterRoot.position +=
            new Vector3(
                delta.x,
                delta.y,
                0f);

        animator.Play(profile.IdleStateName, 0, 0f);
        animator.Update(0f);

        controller.SetReady();
    }
}

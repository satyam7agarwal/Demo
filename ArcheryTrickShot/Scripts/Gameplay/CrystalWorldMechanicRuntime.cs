using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Data-driven runtime bridge for Crystal Caverns mechanics.
///
/// The base level manager still owns ordinary targets, walls, arrows and scoring.
/// This component adds World 2 mechanics declared in LevelData without scene
/// wiring or per-level conditionals.
/// </summary>
[DisallowMultipleComponent]
public sealed class CrystalWorldMechanicRuntime : MonoBehaviour
{
    private LevelManager levelManager;
    private Transform lastLevelObjects;
    private LevelData lastLevel;
    private readonly Dictionary<string, List<CrystalGateRuntime>>
        gatesByGroup =
            new Dictionary<string, List<CrystalGateRuntime>>();

    private static FieldInfo currentLevelField;
    private static Material sharedLineMaterial;
    private static Sprite crystalWallHorizontal;
    private static Sprite crystalWallVertical;
    private static Sprite crystalGateLeaf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager manager =
            UnityEngine.Object.FindFirstObjectByType<LevelManager>();

        if (manager == null)
            return;

        if (manager.GetComponent<CrystalWorldMechanicRuntime>() == null)
        {
            manager.gameObject.AddComponent<CrystalWorldMechanicRuntime>();
        }

        if (manager.GetComponent<CrystalPlayableFrontierRuntime>() == null)
        {
            manager.gameObject.AddComponent<CrystalPlayableFrontierRuntime>();
        }
    }

    private void Awake()
    {
        levelManager =
            GetComponent<LevelManager>();

        currentLevelField ??=
            typeof(LevelManager).GetField(
                "currentLevel",
                BindingFlags.Instance |
                BindingFlags.NonPublic);

        EnsureSharedLineMaterial();
    }

    private void LateUpdate()
    {
        if (levelManager == null)
            return;

        Transform levelObjects =
            levelManager.transform.Find(
                "LevelObjects");

        if (levelObjects == null)
        {
            lastLevelObjects = null;
            lastLevel = null;
            gatesByGroup.Clear();
            return;
        }

        if (levelObjects == lastLevelObjects)
            return;

        LevelData level =
            ResolveCurrentLevel();

        if (level == null)
            return;

        lastLevelObjects = levelObjects;
        lastLevel = level;

        // Only Crystal Caverns data opts into this presentation/mechanic pass.
        // Ordinary worlds are left completely untouched.
        bool usesCrystalWorld =
            level.LevelNumber >= 11 &&
            level.LevelNumber <= 20;

        if (!usesCrystalWorld)
            return;

        SpawnForLevel(
            level,
            levelObjects);
    }

    private LevelData ResolveCurrentLevel()
    {
        if (currentLevelField != null &&
            levelManager != null)
        {
            LevelData active =
                currentLevelField.GetValue(
                    levelManager) as LevelData;

            if (active != null)
                return active;
        }

        int fallbackLevel =
            ATSPlayerProgress.LastPlayedLevel;

        LevelData[] all =
            Resources.LoadAll<LevelData>(
                "Levels");

        for (int i = 0;
             i < all.Length;
             i++)
        {
            if (all[i] != null &&
                all[i].LevelNumber == fallbackLevel)
            {
                return all[i];
            }
        }

        return null;
    }

    private void SpawnForLevel(
        LevelData level,
        Transform parent)
    {
        gatesByGroup.Clear();

        StyleOrdinaryCrystalWalls(parent);
        SpawnCrystalGates(level.CrystalGates, parent);
        SpawnResonanceCrystals(level.ResonanceCrystals, parent);
        SpawnPrismCrystals(level.PrismCrystals, parent);
    }

    private void StyleOrdinaryCrystalWalls(
        Transform parent)
    {
        if (parent == null)
            return;

        for (int i = 0;
             i < parent.childCount;
             i++)
        {
            Transform child =
                parent.GetChild(i);

            if (child == null ||
                !child.name.StartsWith(
                    "Wall",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ApplyCrystalWallStyle(
                child.gameObject);
        }
    }

    private void SpawnCrystalGates(
        LevelData.CrystalGateData[] data,
        Transform parent)
    {
        if (data == null)
            return;

        if (crystalGateLeaf == null)
        {
            crystalGateLeaf =
                Resources.Load<Sprite>(
                    "Art/Gameplay/World2/CrystalGateLeaf");
        }

        if (crystalGateLeaf == null)
        {
            Debug.LogWarning(
                "CrystalWorldMechanicRuntime: CrystalGateLeaf sprite is missing.");
            return;
        }

        for (int i = 0;
             i < data.Length;
             i++)
        {
            LevelData.CrystalGateData item =
                data[i];

            if (item == null)
                continue;

            string group =
                Normalize(
                    item.UnlockGroupId);

            GameObject root =
                new GameObject(
                    "CrystalGate_" + group,
                    typeof(CrystalGateRuntime));

            root.transform.SetParent(
                parent,
                false);

            root.transform.position =
                new Vector3(
                    item.Position.x,
                    item.Position.y,
                    -0.02f);

            root.transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    item.Rotation);

            // The gate is a dedicated authored Crystal Caverns asset rather
            // than a recoloured World-1 wall. Two physical leaves retract in
            // opposite directions while preserving the proven collision shape.
            const float nativeGateWidth = 3f;
            const float nativeGateHeight = 1.34f;

            float quarterHeight =
                nativeGateHeight *
                Mathf.Abs(item.Scale.y) *
                0.25f;

            GameObject top =
                CreateCrystalGateLeaf(
                    root.transform,
                    "CrystalGateTop",
                    new Vector3(
                        0f,
                        quarterHeight,
                        0f),
                    new Vector3(
                        item.Scale.x,
                        item.Scale.y * 0.5f,
                        1f),
                    crystalGateLeaf,
                    nativeGateWidth,
                    nativeGateHeight);

            GameObject bottom =
                CreateCrystalGateLeaf(
                    root.transform,
                    "CrystalGateBottom",
                    new Vector3(
                        0f,
                        -quarterHeight,
                        0f),
                    new Vector3(
                        item.Scale.x,
                        item.Scale.y * 0.5f,
                        1f),
                    crystalGateLeaf,
                    nativeGateWidth,
                    nativeGateHeight);

            CrystalGateRuntime runtime =
                root.GetComponent<CrystalGateRuntime>();

            runtime.Configure(
                top.transform,
                bottom.transform,
                item.OpenOffset,
                item.OpenDuration);

            if (!gatesByGroup.TryGetValue(
                    group,
                    out List<CrystalGateRuntime> list))
            {
                list =
                    new List<CrystalGateRuntime>();

                gatesByGroup[group] =
                    list;
            }

            list.Add(runtime);
        }
    }

    private static GameObject CreateCrystalGateLeaf(
        Transform parent,
        string objectName,
        Vector3 localPosition,
        Vector3 localScale,
        Sprite sprite,
        float colliderWidth,
        float colliderHeight)
    {
        GameObject leaf =
            new GameObject(
                objectName,
                typeof(SpriteRenderer),
                typeof(BoxCollider2D));

        leaf.transform.SetParent(
            parent,
            false);

        leaf.transform.localPosition =
            localPosition;

        leaf.transform.localRotation =
            Quaternion.identity;

        leaf.transform.localScale =
            localScale;

        SpriteRenderer renderer =
            leaf.GetComponent<SpriteRenderer>();

        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = 8;

        BoxCollider2D collider =
            leaf.GetComponent<BoxCollider2D>();

        collider.size =
            new Vector2(
                colliderWidth,
                colliderHeight);

        collider.offset =
            Vector2.zero;

        collider.isTrigger =
            false;

        GameObject glowObject =
            new GameObject(
                "CrystalGateGlow",
                typeof(SpriteRenderer));

        glowObject.transform.SetParent(
            leaf.transform,
            false);

        SpriteRenderer glow =
            glowObject.GetComponent<SpriteRenderer>();

        glow.sprite = sprite;
        glow.sortingLayerID =
            renderer.sortingLayerID;

        glow.sortingOrder =
            renderer.sortingOrder + 1;

        glow.color =
            new Color(
                0.20f,
                0.92f,
                1f,
                0.11f);

        glow.transform.localPosition =
            Vector3.zero;

        glow.transform.localRotation =
            Quaternion.identity;

        glow.transform.localScale =
            Vector3.one * 1.035f;

        return leaf;
    }

    private void SpawnResonanceCrystals(
        LevelData.ResonanceCrystalData[] data,
        Transform parent)
    {
        if (data == null)
            return;

        Sprite sprite =
            Resources.Load<Sprite>(
                "Art/Gameplay/World2/ResonanceCrystal");

        for (int i = 0;
             i < data.Length;
             i++)
        {
            LevelData.ResonanceCrystalData item =
                data[i];

            if (item == null)
                continue;

            string group =
                Normalize(
                    item.UnlockGroupId);

            GameObject go =
                new GameObject(
                    "ResonanceCrystal_" + group,
                    typeof(SpriteRenderer),
                    typeof(CircleCollider2D),
                    typeof(ResonanceCrystalRuntime));

            go.transform.SetParent(
                parent,
                false);

            go.transform.position =
                new Vector3(
                    item.Position.x,
                    item.Position.y,
                    -0.15f);

            go.transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    item.Rotation);

            go.transform.localScale =
                new Vector3(
                    item.Scale.x,
                    item.Scale.y,
                    1f);

            SpriteRenderer renderer =
                go.GetComponent<SpriteRenderer>();

            renderer.sprite = sprite;
            renderer.sortingOrder = 10;

            CircleCollider2D trigger =
                go.GetComponent<CircleCollider2D>();

            trigger.isTrigger = true;

            float resonanceScale =
                Mathf.Max(
                    0.01f,
                    Mathf.Max(
                        Mathf.Abs(item.Scale.x),
                        Mathf.Abs(item.Scale.y)));

            trigger.radius =
                Mathf.Max(
                    0.25f,
                    item.TriggerRadius) /
                resonanceScale;

            ResonanceCrystalRuntime crystal =
                go.GetComponent<ResonanceCrystalRuntime>();

            crystal.Configure(
                this,
                group,
                renderer);

            CreateIdleRuneLinks(
                item.Position,
                group,
                parent);
        }
    }

    private void SpawnPrismCrystals(
        LevelData.PrismCrystalData[] data,
        Transform parent)
    {
        if (data == null)
            return;

        Sprite sprite =
            Resources.Load<Sprite>(
                "Art/Gameplay/World2/PrismCrystal");

        for (int i = 0;
             i < data.Length;
             i++)
        {
            LevelData.PrismCrystalData item =
                data[i];

            if (item == null)
                continue;

            GameObject go =
                new GameObject(
                    "PrismCrystal",
                    typeof(SpriteRenderer),
                    typeof(CircleCollider2D),
                    typeof(PrismCrystalRuntime));

            go.transform.SetParent(
                parent,
                false);

            go.transform.position =
                new Vector3(
                    item.Position.x,
                    item.Position.y,
                    -0.15f);

            go.transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    item.Rotation);

            go.transform.localScale =
                new Vector3(
                    item.Scale.x,
                    item.Scale.y,
                    1f);

            SpriteRenderer renderer =
                go.GetComponent<SpriteRenderer>();

            renderer.sprite = sprite;
            renderer.sortingOrder = 10;

            CircleCollider2D trigger =
                go.GetComponent<CircleCollider2D>();

            trigger.isTrigger = true;

            float prismScale =
                Mathf.Max(
                    0.01f,
                    Mathf.Max(
                        Mathf.Abs(item.Scale.x),
                        Mathf.Abs(item.Scale.y)));

            trigger.radius =
                Mathf.Max(
                    0.25f,
                    item.TriggerRadius) /
                prismScale;

            PrismCrystalRuntime prism =
                go.GetComponent<PrismCrystalRuntime>();

            prism.Configure(
                item.ExitAngle,
                item.SpeedMultiplier,
                renderer);
        }
    }

    public void ActivateGroup(
        string unlockGroupId,
        Vector2 sourcePosition)
    {
        string group =
            Normalize(
                unlockGroupId);

        if (!gatesByGroup.TryGetValue(
                group,
                out List<CrystalGateRuntime> gates))
        {
            return;
        }

        for (int i = 0;
             i < gates.Count;
             i++)
        {
            CrystalGateRuntime gate =
                gates[i];

            if (gate == null)
                continue;

            StartCoroutine(
                PlayEnergyPulse(
                    sourcePosition,
                    gate.ClosedWorldPosition));

            gate.Open(0.20f);
        }
    }

    private void CreateIdleRuneLinks(
        Vector2 sourcePosition,
        string group,
        Transform parent)
    {
        if (!gatesByGroup.TryGetValue(
                group,
                out List<CrystalGateRuntime> gates))
        {
            return;
        }

        for (int i = 0;
             i < gates.Count;
             i++)
        {
            CrystalGateRuntime gate =
                gates[i];

            if (gate == null)
                continue;

            GameObject linkObject =
                new GameObject(
                    "CrystalRuneLink_" + group);

            linkObject.transform.SetParent(
                parent,
                false);

            LineRenderer link =
                linkObject.AddComponent<LineRenderer>();

            link.sharedMaterial =
                sharedLineMaterial;

            link.useWorldSpace = true;
            link.positionCount = 2;
            link.startWidth = 0.022f;
            link.endWidth = 0.022f;
            link.numCapVertices = 2;
            link.sortingOrder = 4;

            Color faint =
                new Color(
                    0.24f,
                    0.86f,
                    1f,
                    0.22f);

            link.startColor = faint;
            link.endColor = faint;

            link.SetPosition(
                0,
                new Vector3(
                    sourcePosition.x,
                    sourcePosition.y,
                    -0.11f));

            Vector2 target =
                gate.ClosedWorldPosition;

            link.SetPosition(
                1,
                new Vector3(
                    target.x,
                    target.y,
                    -0.11f));
        }
    }

    private IEnumerator PlayEnergyPulse(
        Vector2 source,
        Vector2 destination)
    {
        GameObject pulseObject =
            new GameObject(
                "CrystalEnergyPulse");

        if (lastLevelObjects != null)
        {
            pulseObject.transform.SetParent(
                lastLevelObjects,
                false);
        }

        LineRenderer pulse =
            pulseObject.AddComponent<LineRenderer>();

        pulse.sharedMaterial =
            sharedLineMaterial;

        pulse.useWorldSpace = true;
        pulse.positionCount = 2;
        pulse.startWidth = 0.12f;
        pulse.endWidth = 0.035f;
        pulse.numCapVertices = 4;
        pulse.sortingOrder = 13;
        pulse.startColor =
            new Color(
                0.88f,
                1f,
                1f,
                1f);
        pulse.endColor =
            new Color(
                0.16f,
                0.88f,
                1f,
                0.12f);

        const float duration = 0.28f;
        const float segmentLength = 0.18f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / duration);

            float tailT =
                Mathf.Clamp01(
                    t - segmentLength);

            Vector2 tail =
                Vector2.Lerp(
                    source,
                    destination,
                    tailT);

            Vector2 head =
                Vector2.Lerp(
                    source,
                    destination,
                    t);

            pulse.SetPosition(
                0,
                new Vector3(
                    tail.x,
                    tail.y,
                    -0.12f));

            pulse.SetPosition(
                1,
                new Vector3(
                    head.x,
                    head.y,
                    -0.12f));

            yield return null;
        }

        Destroy(pulseObject);
    }

    private static void ApplyCrystalWallStyle(
        GameObject wall)
    {
        if (wall == null)
            return;

        if (crystalWallHorizontal == null)
        {
            crystalWallHorizontal =
                Resources.Load<Sprite>(
                    "Art/Gameplay/World2/CrystalWall_Horizontal");
        }

        if (crystalWallVertical == null)
        {
            crystalWallVertical =
                Resources.Load<Sprite>(
                    "Art/Gameplay/World2/CrystalWall_Vertical");
        }

        float z =
            NormalizeAngle(
                wall.transform.eulerAngles.z);

        bool vertical =
            Mathf.Abs(
                Mathf.Abs(z) - 90f) <=
            20f;

        Sprite selected =
            vertical
                ? crystalWallVertical
                : crystalWallHorizontal;

        if (selected == null)
            return;

        Transform visualTransform =
            wall.transform.Find(
                "WallVisual");

        SpriteRenderer renderer =
            visualTransform != null
                ? visualTransform.GetComponent<SpriteRenderer>()
                : null;

        if (renderer == null)
        {
            SpriteRenderer[] all =
                wall.GetComponentsInChildren<SpriteRenderer>(
                    true);

            for (int i = 0;
                 i < all.Length;
                 i++)
            {
                if (all[i] == null ||
                    all[i].transform == wall.transform)
                {
                    continue;
                }

                renderer = all[i];
                break;
            }
        }

        if (renderer == null)
            return;

        renderer.sprite =
            selected;

        renderer.color =
            Color.white;

        renderer.sortingOrder =
            8;

        // Match Wall.ApplyPresentation(): vertical artwork stays visually
        // upright while the physical collider keeps the authored 90° rotation.
        renderer.transform.localRotation =
            vertical
                ? Quaternion.Euler(
                    0f,
                    0f,
                    -z)
                : Quaternion.identity;

        renderer.transform.localPosition =
            Vector3.zero;

        renderer.transform.localScale =
            Vector3.one;

        // V1/V2 created runtime outline/glow children. Disable them so the
        // authored sprite itself owns the World-2 visual language.
        Transform oldFrame =
            wall.transform.Find(
                "CrystalRuneFrame");

        if (oldFrame != null)
            oldFrame.gameObject.SetActive(false);

        Transform oldGlow =
            wall.transform.Find(
                "CrystalGateGlow");

        if (oldGlow != null)
            oldGlow.gameObject.SetActive(false);
    }

    private static float NormalizeAngle(
        float degrees)
    {
        degrees %= 360f;

        if (degrees > 180f)
            degrees -= 360f;

        return degrees;
    }

    private static void EnsureSharedLineMaterial()
    {
        if (sharedLineMaterial != null)
            return;

        Shader shader =
            Shader.Find("Sprites/Default");

        if (shader == null)
            return;

        sharedLineMaterial =
            new Material(shader)
            {
                name = "RuntimeCrystalWorldLineMaterial",
                hideFlags = HideFlags.DontSave
            };
    }

    private static string Normalize(
        string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "default"
            : value.Trim();
    }
}

using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Keeps authored edge presentation art attached to the physical viewport edge
/// on wide phones/tablets without changing gameplay-space composition.
///
/// Rules live in Resources/UI/ResponsivePresentationRules.json.
/// The original authored gameplay-facing edge stays fixed; only the outside
/// edge is extended. Matched PresentationColliders remain children of the
/// visual, so they automatically follow the responsive transform.
///
/// This intentionally does NOT react to final-shot camera zoom/pan. It only
/// recalculates when a new level is spawned or the physical display aspect
/// changes.
/// </summary>
[DefaultExecutionOrder(1380)]
[DisallowMultipleComponent]
public sealed class ViewportEdgePresentationRuntime : MonoBehaviour
{
    private const string RulesResourcePath =
        "UI/ResponsivePresentationRules";

    [Serializable]
    private sealed class RuleSet
    {
        public Rule[] rules =
            Array.Empty<Rule>();
    }

    [Serializable]
    private sealed class Rule
    {
        public string resourcePath =
            string.Empty;

        // 0=None, 1=Left, 2=Right, 3=Both
        public int fill = 0;

        [Min(0f)]
        public float bleed = 0.4f;
    }

    private LevelManager levelManager;
    private Transform lastLevelObjects;
    private RuleSet ruleSet =
        new RuleSet();

    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private float lastAspect = -1f;

    private float levelCameraCenterX;
    private float baseOrthographicSize = 6.2f;

    private static FieldInfo currentLevelField;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager manager =
            UnityEngine.Object.FindFirstObjectByType<LevelManager>();

        if (manager == null)
            return;

        if (manager.GetComponent<
                ViewportEdgePresentationRuntime>() == null)
        {
            manager.gameObject.AddComponent<
                ViewportEdgePresentationRuntime>();
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

        GameConfig config =
            GameConfig.Load();

        if (config != null)
        {
            baseOrthographicSize =
                Mathf.Max(
                    1f,
                    config.BaseOrthographicSize);
        }

        LoadRules();
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
            return;
        }

        Camera camera =
            Camera.main;

        if (camera == null ||
            !camera.orthographic)
        {
            return;
        }

        bool newLevel =
            levelObjects !=
            lastLevelObjects;

        bool displayChanged =
            Screen.width !=
                lastScreenWidth ||
            Screen.height !=
                lastScreenHeight ||
            !Mathf.Approximately(
                camera.aspect,
                lastAspect);

        if (!newLevel &&
            !displayChanged)
        {
            return;
        }

        if (newLevel)
        {
            lastLevelObjects =
                levelObjects;

            // Capture the normal level camera centre once. Responsive scenery
            // must not chase the camera during cinematic target focus.
            levelCameraCenterX =
                camera.transform.position.x;
        }

        lastScreenWidth =
            Screen.width;

        lastScreenHeight =
            Screen.height;

        lastAspect =
            camera.aspect;

        LevelData level =
            ResolveCurrentLevel();

        if (level == null)
            return;

        ApplyRules(
            level,
            levelObjects,
            camera.aspect);
    }

    private void LoadRules()
    {
        TextAsset json =
            Resources.Load<TextAsset>(
                RulesResourcePath);

        if (json == null ||
            string.IsNullOrWhiteSpace(
                json.text))
        {
            Debug.LogWarning(
                "[Responsive Presentation] Missing Resources/" +
                RulesResourcePath +
                ".json");

            ruleSet =
                new RuleSet();

            return;
        }

        try
        {
            RuleSet parsed =
                JsonUtility.FromJson<RuleSet>(
                    json.text);

            ruleSet =
                parsed ??
                new RuleSet();
        }
        catch (Exception ex)
        {
            ruleSet =
                new RuleSet();

            Debug.LogWarning(
                "[Responsive Presentation] Could not parse rules: " +
                ex.Message);
        }
    }

    private LevelData ResolveCurrentLevel()
    {
        if (levelManager == null ||
            currentLevelField == null)
        {
            return null;
        }

        return currentLevelField.GetValue(
            levelManager) as LevelData;
    }

    private void ApplyRules(
        LevelData level,
        Transform levelObjects,
        float aspect)
    {
        if (level == null ||
            levelObjects == null ||
            level.PresentationSprites == null ||
            ruleSet == null ||
            ruleSet.rules == null)
        {
            return;
        }

        float halfWidth =
            baseOrthographicSize *
            Mathf.Max(
                0.1f,
                aspect);

        float cameraLeft =
            levelCameraCenterX -
            halfWidth;

        float cameraRight =
            levelCameraCenterX +
            halfWidth;

        for (int i = 0;
             i < level.PresentationSprites.Length;
             i++)
        {
            LevelData.PresentationSpriteData item =
                level.PresentationSprites[i];

            if (item == null ||
                string.IsNullOrWhiteSpace(
                    item.Name) ||
                string.IsNullOrWhiteSpace(
                    item.ResourcePath))
            {
                continue;
            }

            Rule rule =
                FindRule(
                    item.ResourcePath);

            if (rule == null ||
                rule.fill <= 0)
            {
                continue;
            }

            Transform visual =
                FindDirectChild(
                    levelObjects,
                    item.Name.Trim());

            if (visual == null)
                continue;

            SpriteRenderer renderer =
                visual.GetComponent<
                    SpriteRenderer>();

            Sprite sprite =
                renderer != null
                    ? renderer.sprite
                    : null;

            if (sprite == null ||
                sprite.bounds.size.x <=
                0.001f)
            {
                continue;
            }

            ApplyRule(
                visual,
                sprite,
                item,
                rule,
                cameraLeft,
                cameraRight);
        }
    }

    private static void ApplyRule(
        Transform visual,
        Sprite sprite,
        LevelData.PresentationSpriteData item,
        Rule rule,
        float cameraLeft,
        float cameraRight)
    {
        // Rebuild the exact authored transform first so resizing never
        // compounds when a device/window aspect changes.
        Vector3 baseScale =
            new Vector3(
                item.Scale.x,
                item.Scale.y,
                1f);

        if (item.WorldWidth >
                0.001f &&
            sprite.bounds.size.x >
                0.001f)
        {
            float fitScale =
                item.WorldWidth /
                sprite.bounds.size.x;

            baseScale.x *=
                fitScale;

            baseScale.y *=
                fitScale;
        }

        visual.position =
            new Vector3(
                item.Position.x,
                item.Position.y,
                item.Depth);

        visual.rotation =
            Quaternion.Euler(
                0f,
                0f,
                item.Rotation);

        visual.localScale =
            baseScale;

        float spriteWidth =
            sprite.bounds.size.x;

        float currentWidth =
            spriteWidth *
            Mathf.Abs(
                baseScale.x);

        if (currentWidth <=
            0.001f)
        {
            return;
        }

        float currentLeft =
            item.Position.x -
            currentWidth *
            0.5f;

        float currentRight =
            item.Position.x +
            currentWidth *
            0.5f;

        float desiredLeft =
            currentLeft;

        float desiredRight =
            currentRight;

        float bleed =
            Mathf.Max(
                0f,
                rule.bleed);

        // LEFT / BOTH:
        // Preserve the gameplay-facing right edge and only extend outward.
        if (rule.fill == 1 ||
            rule.fill == 3)
        {
            desiredLeft =
                Mathf.Min(
                    currentLeft,
                    cameraLeft -
                    bleed);
        }

        // RIGHT / BOTH:
        // Preserve the gameplay-facing left edge and only extend outward.
        if (rule.fill == 2 ||
            rule.fill == 3)
        {
            desiredRight =
                Mathf.Max(
                    currentRight,
                    cameraRight +
                    bleed);
        }

        float desiredWidth =
            desiredRight -
            desiredLeft;

        if (desiredWidth <=
            currentWidth +
            0.001f)
        {
            return;
        }

        Vector3 responsiveScale =
            baseScale;

        responsiveScale.x =
            Mathf.Sign(
                baseScale.x) *
            desiredWidth /
            spriteWidth;

        visual.localScale =
            responsiveScale;

        Vector3 responsivePosition =
            visual.position;

        responsivePosition.x =
            (desiredLeft +
             desiredRight) *
            0.5f;

        visual.position =
            responsivePosition;
    }

    private Rule FindRule(
        string resourcePath)
    {
        if (ruleSet == null ||
            ruleSet.rules == null ||
            string.IsNullOrWhiteSpace(
                resourcePath))
        {
            return null;
        }

        string normalized =
            resourcePath.Trim();

        for (int i = 0;
             i < ruleSet.rules.Length;
             i++)
        {
            Rule rule =
                ruleSet.rules[i];

            if (rule == null ||
                string.IsNullOrWhiteSpace(
                    rule.resourcePath))
            {
                continue;
            }

            if (string.Equals(
                    normalized,
                    rule.resourcePath.Trim(),
                    StringComparison.Ordinal))
            {
                return rule;
            }
        }

        return null;
    }

    private static Transform FindDirectChild(
        Transform parent,
        string name)
    {
        if (parent == null ||
            string.IsNullOrWhiteSpace(
                name))
        {
            return null;
        }

        for (int i = 0;
             i < parent.childCount;
             i++)
        {
            Transform child =
                parent.GetChild(i);

            if (child != null &&
                child.name == name)
            {
                return child;
            }
        }

        return null;
    }
}

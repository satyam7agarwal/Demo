using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Generic data-driven living-water encounter system.
///
/// Serpents stay below the water, telegraph with a ripple/glow, rise into the
/// playable path, expose a trigger-only weak point, then retreat. A successful
/// weak-point hit activates any CrystalGate with the same unlock group.
///
/// No level-number checks live here.
/// </summary>
[DefaultExecutionOrder(1560)]
[DisallowMultipleComponent]
public sealed class AbyssalSerpentSystemRuntime : MonoBehaviour
{
    private const string WeakPointIndicatorResourcePath =
        "Art/Gameplay/World2/Effects/WeakPoint_CrystalMarker";

    private LevelManager levelManager;
    private Transform lastLevelObjects;
    private static FieldInfo currentLevelField;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager manager =
            UnityEngine.Object.FindFirstObjectByType<LevelManager>();

        if (manager == null)
            return;

        if (manager.GetComponent<AbyssalSerpentSystemRuntime>() == null)
        {
            manager.gameObject.AddComponent<
                AbyssalSerpentSystemRuntime>();
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

        if (levelObjects == lastLevelObjects)
            return;

        lastLevelObjects =
            levelObjects;

        LevelData level =
            ResolveCurrentLevel();

        if (level == null)
            return;

        SpawnSerpents(
            level.AbyssalSerpents,
            levelObjects);
    }

    private LevelData ResolveCurrentLevel()
    {
        if (currentLevelField == null ||
            levelManager == null)
        {
            return null;
        }

        return currentLevelField.GetValue(
            levelManager) as LevelData;
    }

    private void SpawnSerpents(
        LevelData.AbyssalSerpentData[] data,
        Transform parent)
    {
        if (data == null ||
            parent == null)
        {
            return;
        }

        CrystalWorldMechanicRuntime crystalRuntime =
            levelManager != null
                ? levelManager.GetComponent<
                    CrystalWorldMechanicRuntime>()
                : null;

        Sprite indicatorSprite =
            Resources.Load<Sprite>(
                WeakPointIndicatorResourcePath);

        for (int i = 0;
             i < data.Length;
             i++)
        {
            LevelData.AbyssalSerpentData item =
                data[i];

            if (item == null ||
                string.IsNullOrWhiteSpace(
                    item.ResourcePath))
            {
                continue;
            }

            Sprite serpentSprite =
                Resources.Load<Sprite>(
                    item.ResourcePath.Trim());

            if (serpentSprite == null)
            {
                Debug.LogWarning(
                    "[AbyssalSerpent] Missing sprite at Resources/" +
                    item.ResourcePath,
                    parent);
                continue;
            }

            GameObject root =
                new GameObject(
                    "AbyssalSerpent_" + i,
                    typeof(SpriteRenderer),
                    typeof(AbyssalSerpentRuntime));

            root.transform.SetParent(
                parent,
                false);

            root.transform.position =
                new Vector3(
                    item.Position.x +
                        item.HiddenOffset.x,
                    item.Position.y +
                        item.HiddenOffset.y,
                    -0.04f);

            root.transform.rotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    item.Rotation);

            Vector3 scale =
                new Vector3(
                    item.Scale.x,
                    item.Scale.y,
                    1f);

            if (item.WorldWidth > 0.001f &&
                serpentSprite.bounds.size.x > 0.001f)
            {
                float fitScale =
                    item.WorldWidth /
                    serpentSprite.bounds.size.x;

                scale.x *= fitScale;
                scale.y *= fitScale;
            }

            root.transform.localScale =
                scale;

            SpriteRenderer renderer =
                root.GetComponent<SpriteRenderer>();

            renderer.sprite =
                serpentSprite;
            renderer.sortingOrder =
                item.SortingOrder;
            renderer.color =
                new Color(
                    1f,
                    1f,
                    1f,
                    0f);

            GameObject weakPointObject =
                new GameObject(
                    "SerpentWeakPoint",
                    typeof(CircleCollider2D),
                    typeof(AbyssalSerpentWeakPointRuntime));

            weakPointObject.transform.SetParent(
                root.transform,
                false);

            Vector3 lossy =
                root.transform.lossyScale;

            float safeX =
                Mathf.Max(
                    0.0001f,
                    Mathf.Abs(lossy.x));

            float safeY =
                Mathf.Max(
                    0.0001f,
                    Mathf.Abs(lossy.y));

            float safeUniform =
                Mathf.Max(
                    safeX,
                    safeY);

            weakPointObject.transform.localPosition =
                new Vector3(
                    item.WeakPointOffset.x /
                        safeX,
                    item.WeakPointOffset.y /
                        safeY,
                    0f);

            CircleCollider2D weakPoint =
                weakPointObject.GetComponent<
                    CircleCollider2D>();

            weakPoint.isTrigger =
                true;
            weakPoint.radius =
                item.WeakPointRadius /
                safeUniform;
            weakPoint.enabled =
                false;

            SpriteRenderer indicatorRenderer =
                null;

            if (indicatorSprite != null)
            {
                GameObject indicatorObject =
                    new GameObject(
                        "WeakPointIndicator",
                        typeof(SpriteRenderer));

                indicatorObject.transform.SetParent(
                    weakPointObject.transform,
                    false);

                indicatorRenderer =
                    indicatorObject.GetComponent<
                        SpriteRenderer>();

                indicatorRenderer.sprite =
                    indicatorSprite;
                indicatorRenderer.sortingOrder =
                    item.SortingOrder + 2;
                indicatorRenderer.color =
                    new Color(
                        1f,
                        1f,
                        1f,
                        0f);

                float desiredIndicatorWorldWidth =
                    Mathf.Max(
                        item.WeakPointRadius * 3.1f,
                        1.2f);

                float indicatorScale =
                    desiredIndicatorWorldWidth /
                    Mathf.Max(
                        0.0001f,
                        indicatorSprite.bounds.size.x) /
                    safeUniform;

                indicatorObject.transform.localScale =
                    new Vector3(
                        indicatorScale,
                        indicatorScale,
                        1f);
            }

            AbyssalSerpentRuntime runtime =
                root.GetComponent<
                    AbyssalSerpentRuntime>();

            AbyssalSerpentWeakPointRuntime weakPointRuntime =
                weakPointObject.GetComponent<
                    AbyssalSerpentWeakPointRuntime>();

            weakPointRuntime.Configure(
                runtime);

            runtime.Configure(
                renderer,
                weakPoint,
                indicatorRenderer,
                item,
                crystalRuntime,
                parent);
        }
    }
}

[DisallowMultipleComponent]
public sealed class AbyssalSerpentRuntime : MonoBehaviour
{
    private SpriteRenderer renderer;
    private CircleCollider2D weakPoint;
    private SpriteRenderer weakPointIndicator;
    private LevelData.AbyssalSerpentData data;
    private CrystalWorldMechanicRuntime crystalRuntime;
    private Transform effectsParent;

    private Vector3 emergedPosition;
    private Vector3 hiddenPosition;
    private Vector3 authoredScale;
    private Color authoredColor;
    private Vector3 indicatorBaseLocalScale;

    private Coroutine routine;
    private bool consumed;

    private LineRenderer rippleOuter;
    private LineRenderer rippleInner;
    private Material rippleMaterial;

    public void Configure(
        SpriteRenderer configuredRenderer,
        CircleCollider2D configuredWeakPoint,
        SpriteRenderer configuredIndicator,
        LevelData.AbyssalSerpentData configuredData,
        CrystalWorldMechanicRuntime configuredCrystalRuntime,
        Transform configuredEffectsParent)
    {
        renderer =
            configuredRenderer;

        weakPoint =
            configuredWeakPoint;

        weakPointIndicator =
            configuredIndicator;

        data =
            configuredData;

        crystalRuntime =
            configuredCrystalRuntime;

        effectsParent =
            configuredEffectsParent;

        emergedPosition =
            new Vector3(
                data.Position.x,
                data.Position.y,
                transform.position.z);

        hiddenPosition =
            emergedPosition +
            new Vector3(
                data.HiddenOffset.x,
                data.HiddenOffset.y,
                0f);

        authoredScale =
            transform.localScale;

        authoredColor =
            Color.white;

        indicatorBaseLocalScale =
            weakPointIndicator != null
                ? weakPointIndicator.transform.localScale
                : Vector3.one;

        CreateRipples();

        if (routine != null)
            StopCoroutine(routine);

        routine =
            StartCoroutine(
                Cycle());
    }

    public void TryHitWeakPoint(
        ArrowController arrow)
    {
        if (consumed ||
            arrow == null ||
            !arrow.HasFired ||
            arrow.IsStopped ||
            weakPoint == null ||
            !weakPoint.enabled)
        {
            return;
        }

        consumed = true;

        weakPoint.enabled =
            false;

        if (data.ConsumeArrowOnWeakPoint)
            arrow.Stop();

        crystalRuntime?.ActivateGroup(
            data.UnlockGroupId,
            transform.position +
            new Vector3(
                data.WeakPointOffset.x,
                data.WeakPointOffset.y,
                0f));

        if (routine != null)
            StopCoroutine(routine);

        routine =
            StartCoroutine(
                HitRetreat());
    }

    private IEnumerator Cycle()
    {
        SetHidden();

        if (data.StartDelay > 0f)
        {
            yield return
                new WaitForSeconds(
                    data.StartDelay);
        }

        while (!consumed)
        {
            yield return
                WarningRoutine(
                    Mathf.Max(
                        0.05f,
                        data.WarningDuration));

            yield return
                RiseRoutine(
                    Mathf.Max(
                        0.05f,
                        data.RiseDuration));

            if (consumed)
                yield break;

            yield return
                EmergedRoutine(
                    Mathf.Max(
                        0.10f,
                        data.EmergedDuration));

            if (consumed)
                yield break;

            yield return
                RetreatRoutine(
                    Mathf.Max(
                        0.05f,
                        data.RetreatDuration));

            if (consumed)
                yield break;

            SetHidden();

            yield return
                new WaitForSeconds(
                    Mathf.Max(
                        0.10f,
                        data.HiddenDuration));
        }
    }

    private IEnumerator WarningRoutine(
        float duration)
    {
        transform.position =
            hiddenPosition;

        if (weakPoint != null)
            weakPoint.enabled = false;

        SetIndicator(
            false,
            0f,
            1f);

        SetRippleEnabled(true);

        float elapsed = 0f;

        while (elapsed < duration &&
               !consumed)
        {
            elapsed +=
                Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            float pulse =
                0.5f +
                0.5f *
                Mathf.Sin(
                    t *
                    Mathf.PI *
                    5f);

            UpdateRipple(
                rippleOuter,
                0.72f +
                    t * 0.95f,
                0.42f *
                    (1f - t));

            UpdateRipple(
                rippleInner,
                0.40f +
                    t * 0.68f,
                0.60f *
                    pulse);

            if (renderer != null)
            {
                renderer.enabled =
                    true;

                renderer.color =
                    new Color(
                        0.18f,
                        0.82f,
                        1f,
                        0.04f +
                        pulse * 0.06f);
            }

            yield return null;
        }

        SetRippleEnabled(false);

        if (renderer != null)
        {
            renderer.color =
                new Color(
                    1f,
                    1f,
                    1f,
                    0f);
        }
    }

    private IEnumerator RiseRoutine(
        float duration)
    {
        if (renderer != null)
            renderer.enabled = true;

        if (weakPoint != null)
            weakPoint.enabled = false;

        float elapsed = 0f;
        bool weakPointEnabled = false;

        while (elapsed < duration &&
               !consumed)
        {
            elapsed +=
                Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            float eased =
                1f -
                Mathf.Pow(
                    1f - t,
                    3f);

            transform.position =
                Vector3.LerpUnclamped(
                    hiddenPosition,
                    emergedPosition,
                    eased);

            float punch =
                1f +
                Mathf.Sin(
                    t *
                    Mathf.PI) *
                0.035f;

            transform.localScale =
                authoredScale *
                punch;

            if (renderer != null)
            {
                Color color =
                    authoredColor;

                color.a =
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(
                            t * 1.25f));

                renderer.color =
                    color;
            }

            if (t > 0.30f)
            {
                float indicatorT =
                    Mathf.InverseLerp(
                        0.30f,
                        1f,
                        t);

                float pulse =
                    1f +
                    Mathf.Sin(
                        t *
                        Mathf.PI *
                        6f) *
                    0.10f;

                SetIndicator(
                    true,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        indicatorT),
                    pulse);
            }
            else
            {
                SetIndicator(
                    false,
                    0f,
                    1f);
            }

            if (!weakPointEnabled &&
                t >= 0.70f)
            {
                weakPointEnabled = true;

                if (weakPoint != null)
                    weakPoint.enabled = true;
            }

            yield return null;
        }

        transform.position =
            emergedPosition;

        transform.localScale =
            authoredScale;

        if (!consumed)
        {
            if (renderer != null)
                renderer.color =
                    authoredColor;

            if (weakPoint != null)
                weakPoint.enabled = true;

            SetIndicator(
                true,
                1f,
                1f);
        }
    }

    private IEnumerator EmergedRoutine(
        float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration &&
               !consumed)
        {
            elapsed +=
                Time.deltaTime;

            float pulse =
                1f +
                Mathf.Sin(
                    Time.time *
                    8f) *
                0.10f;

            float alpha =
                0.86f +
                0.14f *
                (0.5f +
                 0.5f *
                 Mathf.Sin(
                     Time.time *
                     8f));

            SetIndicator(
                true,
                alpha,
                pulse);

            yield return null;
        }
    }

    private IEnumerator RetreatRoutine(
        float duration)
    {
        if (weakPoint != null)
            weakPoint.enabled = false;

        Vector3 start =
            transform.position;

        float elapsed = 0f;

        while (elapsed < duration &&
               !consumed)
        {
            elapsed +=
                Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            float eased =
                t * t;

            transform.position =
                Vector3.LerpUnclamped(
                    start,
                    hiddenPosition,
                    eased);

            if (renderer != null)
            {
                Color color =
                    authoredColor;

                color.a =
                    1f -
                    Mathf.Clamp01(
                        (t - 0.25f) /
                        0.75f);

                renderer.color =
                    color;
            }

            float indicatorAlpha =
                1f -
                Mathf.Clamp01(
                    t / 0.85f);

            SetIndicator(
                indicatorAlpha > 0.001f,
                indicatorAlpha,
                1f + t * 0.12f);

            yield return null;
        }

        SetHidden();
    }

    private IEnumerator HitRetreat()
    {
        SetRippleEnabled(false);

        Vector3 start =
            transform.position;

        Vector3 end =
            hiddenPosition +
            new Vector3(
                0.25f,
                -0.45f,
                0f);

        float elapsed = 0f;
        const float duration = 0.42f;

        while (elapsed < duration)
        {
            elapsed +=
                Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            float eased =
                t * t;

            transform.position =
                Vector3.LerpUnclamped(
                    start,
                    end,
                    eased);

            transform.localRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Lerp(
                        data.Rotation,
                        data.Rotation - 8f,
                        t));

            if (renderer != null)
            {
                Color color =
                    authoredColor;

                color.a =
                    1f - t;

                renderer.color =
                    color;
            }

            float flashAlpha =
                1f - t;

            float flashScale =
                1f +
                t * 0.45f;

            SetIndicator(
                flashAlpha > 0.001f,
                flashAlpha,
                flashScale);

            yield return null;
        }

        SetHidden();
        routine = null;
    }

    private void SetHidden()
    {
        transform.position =
            hiddenPosition;

        transform.localScale =
            authoredScale;

        transform.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                data != null
                    ? data.Rotation
                    : 0f);

        if (weakPoint != null)
            weakPoint.enabled = false;

        if (renderer != null)
        {
            renderer.enabled = false;
            renderer.color =
                new Color(
                    1f,
                    1f,
                    1f,
                    0f);
        }

        SetIndicator(
            false,
            0f,
            1f);

        SetRippleEnabled(false);
    }

    private void SetIndicator(
        bool enabled,
        float alpha,
        float scaleMultiplier)
    {
        if (weakPointIndicator == null)
            return;

        weakPointIndicator.enabled =
            enabled;

        if (!enabled)
            return;

        weakPointIndicator.color =
            new Color(
                1f,
                1f,
                1f,
                Mathf.Clamp01(alpha));

        weakPointIndicator.transform.localScale =
            indicatorBaseLocalScale *
            Mathf.Max(
                0.4f,
                scaleMultiplier);
    }

    private void CreateRipples()
    {
        Shader shader =
            Shader.Find(
                "Sprites/Default");

        if (shader == null)
            return;

        rippleMaterial =
            new Material(shader)
            {
                name =
                    "RuntimeAbyssalSerpentRipple",
                hideFlags =
                    HideFlags.DontSave
            };

        rippleOuter =
            CreateRipple(
                "SerpentWarningRippleOuter");

        rippleInner =
            CreateRipple(
                "SerpentWarningRippleInner");

        SetRippleEnabled(false);
    }

    private LineRenderer CreateRipple(
        string objectName)
    {
        GameObject go =
            new GameObject(
                objectName,
                typeof(LineRenderer));

        go.transform.SetParent(
            effectsParent,
            false);

        LineRenderer line =
            go.GetComponent<LineRenderer>();

        line.sharedMaterial =
            rippleMaterial;
        line.useWorldSpace =
            true;
        line.loop =
            true;
        line.positionCount =
            40;
        line.startWidth =
            0.045f;
        line.endWidth =
            0.045f;
        line.numCornerVertices =
            2;
        line.sortingOrder =
            7;

        return line;
    }

    private void UpdateRipple(
        LineRenderer line,
        float radius,
        float alpha)
    {
        if (line == null ||
            data == null)
        {
            return;
        }

        Color color =
            new Color(
                0.30f,
                0.90f,
                1f,
                Mathf.Clamp01(alpha));

        line.startColor =
            color;
        line.endColor =
            color;

        Vector2 center =
            new Vector2(
                data.Position.x,
                data.WaterSurfaceY);

        int count =
            line.positionCount;

        for (int i = 0;
             i < count;
             i++)
        {
            float angle =
                i /
                (float)count *
                Mathf.PI *
                2f;

            float x =
                Mathf.Cos(angle) *
                radius;

            float y =
                Mathf.Sin(angle) *
                radius *
                0.18f;

            line.SetPosition(
                i,
                new Vector3(
                    center.x + x,
                    center.y + y,
                    -0.06f));
        }
    }

    private void SetRippleEnabled(
        bool enabled)
    {
        if (rippleOuter != null)
            rippleOuter.enabled =
                enabled;

        if (rippleInner != null)
            rippleInner.enabled =
                enabled;
    }

    private void OnDestroy()
    {
        if (rippleMaterial != null)
        {
            Destroy(
                rippleMaterial);

            rippleMaterial =
                null;
        }
    }
}

[DisallowMultipleComponent]
public sealed class AbyssalSerpentWeakPointRuntime : MonoBehaviour
{
    private AbyssalSerpentRuntime owner;

    public void Configure(
        AbyssalSerpentRuntime configuredOwner)
    {
        owner =
            configuredOwner;
    }

    private void OnTriggerEnter2D(
        Collider2D other)
    {
        ArrowController arrow =
            other.GetComponentInParent<
                ArrowController>();

        if (arrow == null)
            return;

        owner?.TryHitWeakPoint(
            arrow);
    }
}

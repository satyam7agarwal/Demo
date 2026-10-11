using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Generic data-driven disappearing obstacle system.
///
/// An obstacle remains physically fixed in place. It warns, dissolves,
/// completely disappears (including collision), gives the player a fair open
/// shooting window, telegraphs its return, then materializes again.
///
/// There are no per-level conditionals in this runtime.
/// </summary>
[DefaultExecutionOrder(1550)]
[DisallowMultipleComponent]
public sealed class VanishingObstacleSystemRuntime : MonoBehaviour
{
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

        if (manager.GetComponent<VanishingObstacleSystemRuntime>() == null)
        {
            manager.gameObject.AddComponent<
                VanishingObstacleSystemRuntime>();
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

        SpawnObstacles(
            level.VanishingObstacles,
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

    private static void SpawnObstacles(
        LevelData.VanishingObstacleData[] data,
        Transform parent)
    {
        if (data == null ||
            parent == null)
        {
            return;
        }

        for (int i = 0;
             i < data.Length;
             i++)
        {
            LevelData.VanishingObstacleData item =
                data[i];

            if (item == null ||
                string.IsNullOrWhiteSpace(
                    item.ResourcePath))
            {
                continue;
            }

            Sprite sprite =
                Resources.Load<Sprite>(
                    item.ResourcePath.Trim());

            if (sprite == null)
            {
                Debug.LogWarning(
                    "[VanishingObstacle] Missing sprite at Resources/" +
                    item.ResourcePath,
                    parent);
                continue;
            }

            GameObject go =
                new GameObject(
                    "VanishingObstacle_" + i,
                    typeof(SpriteRenderer),
                    typeof(BoxCollider2D),
                    typeof(VanishingObstacleRuntime));

            go.transform.SetParent(
                parent,
                false);

            go.transform.position =
                new Vector3(
                    item.Position.x,
                    item.Position.y,
                    -0.03f);

            go.transform.rotation =
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
                sprite.bounds.size.x > 0.001f)
            {
                float fitScale =
                    item.WorldWidth /
                    sprite.bounds.size.x;

                scale.x *= fitScale;
                scale.y *= fitScale;
            }

            go.transform.localScale =
                scale;

            SpriteRenderer renderer =
                go.GetComponent<SpriteRenderer>();

            renderer.sprite =
                sprite;

            renderer.color =
                Color.white;

            renderer.sortingOrder =
                item.SortingOrder;

            BoxCollider2D collider =
                go.GetComponent<BoxCollider2D>();

            Vector3 lossy =
                go.transform.lossyScale;

            float safeX =
                Mathf.Max(
                    0.0001f,
                    Mathf.Abs(lossy.x));

            float safeY =
                Mathf.Max(
                    0.0001f,
                    Mathf.Abs(lossy.y));

            collider.size =
                new Vector2(
                    item.HitboxWorldSize.x / safeX,
                    item.HitboxWorldSize.y / safeY);

            collider.offset =
                new Vector2(
                    item.HitboxOffset.x / safeX,
                    item.HitboxOffset.y / safeY);

            collider.isTrigger =
                false;

            VanishingObstacleRuntime runtime =
                go.GetComponent<VanishingObstacleRuntime>();

            runtime.Configure(
                renderer,
                collider,
                item);
        }
    }
}

/// <summary>
/// Runtime state machine for one disappearing obstacle.
/// </summary>
[DisallowMultipleComponent]
public sealed class VanishingObstacleRuntime : MonoBehaviour
{
    private SpriteRenderer renderer;
    private Collider2D solidCollider;
    private LevelData.VanishingObstacleData data;

    private Vector3 authoredScale;
    private Color authoredColor;

    private Coroutine routine;

    public void Configure(
        SpriteRenderer configuredRenderer,
        Collider2D configuredCollider,
        LevelData.VanishingObstacleData configuredData)
    {
        renderer =
            configuredRenderer;

        solidCollider =
            configuredCollider;

        data =
            configuredData;

        authoredScale =
            transform.localScale;

        authoredColor =
            renderer != null
                ? renderer.color
                : Color.white;

        if (routine != null)
            StopCoroutine(routine);

        routine =
            StartCoroutine(
                Cycle());
    }

    private IEnumerator Cycle()
    {
        if (data == null)
            yield break;

        if (data.StartDelay > 0f)
        {
            yield return
                new WaitForSeconds(
                    data.StartDelay);
        }

        while (true)
        {
            SetSolidVisible();

            yield return
                new WaitForSeconds(
                    Mathf.Max(
                        0.05f,
                        data.VisibleDuration));

            yield return
                WarningRoutine(
                    Mathf.Max(
                        0.05f,
                        data.WarningDuration));

            yield return
                DisappearRoutine(
                    Mathf.Max(
                        0.05f,
                        data.DisappearDuration));

            SetHidden();

            yield return
                new WaitForSeconds(
                    Mathf.Max(
                        0.10f,
                        data.HiddenDuration));

            if (data.ReappearWarningDuration > 0.001f)
            {
                yield return
                    ReappearWarningRoutine(
                        data.ReappearWarningDuration);
            }

            yield return
                ReappearRoutine(
                    Mathf.Max(
                        0.05f,
                        data.ReappearDuration));
        }
    }

    private IEnumerator WarningRoutine(
        float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
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
                    6f);

            if (renderer != null)
            {
                renderer.color =
                    Color.Lerp(
                        authoredColor,
                        new Color(
                            0.72f,
                            0.42f,
                            1f,
                            1f),
                        pulse * 0.48f);
            }

            float scalePulse =
                1f +
                pulse * 0.025f;

            transform.localScale =
                authoredScale *
                scalePulse;

            yield return null;
        }

        transform.localScale =
            authoredScale;
    }

    private IEnumerator DisappearRoutine(
        float duration)
    {
        float elapsed = 0f;
        bool collisionDisabled = false;

        while (elapsed < duration)
        {
            elapsed +=
                Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    duration);

            if (!collisionDisabled &&
                t >= 0.82f)
            {
                collisionDisabled = true;

                if (solidCollider != null)
                    solidCollider.enabled = false;
            }

            if (renderer != null)
            {
                Color color =
                    Color.Lerp(
                        new Color(
                            0.78f,
                            0.52f,
                            1f,
                            1f),
                        new Color(
                            0.18f,
                            0.92f,
                            1f,
                            0f),
                        t);

                renderer.color =
                    color;
            }

            float shrink =
                Mathf.Lerp(
                    1f,
                    0.90f,
                    t);

            transform.localScale =
                authoredScale *
                shrink;

            yield return null;
        }
    }

    private IEnumerator ReappearWarningRoutine(
        float duration)
    {
        if (renderer != null)
            renderer.enabled = true;

        if (solidCollider != null)
            solidCollider.enabled = false;

        transform.localScale =
            authoredScale *
            0.92f;

        float elapsed = 0f;

        while (elapsed < duration)
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

            if (renderer != null)
            {
                renderer.color =
                    new Color(
                        0.35f,
                        0.88f,
                        1f,
                        0.08f +
                        pulse * 0.18f);
            }

            yield return null;
        }
    }

    private IEnumerator ReappearRoutine(
        float duration)
    {
        if (renderer != null)
            renderer.enabled = true;

        if (solidCollider != null)
            solidCollider.enabled = false;

        float elapsed = 0f;

        while (elapsed < duration)
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

            if (renderer != null)
            {
                Color color =
                    Color.Lerp(
                        new Color(
                            0.26f,
                            0.92f,
                            1f,
                            0.12f),
                        authoredColor,
                        eased);

                renderer.color =
                    color;
            }

            transform.localScale =
                authoredScale *
                Mathf.Lerp(
                    0.92f,
                    1f,
                    eased);

            yield return null;
        }

        SetSolidVisible();
    }

    private void SetSolidVisible()
    {
        transform.localScale =
            authoredScale;

        if (renderer != null)
        {
            renderer.enabled =
                true;

            renderer.color =
                authoredColor;
        }

        if (solidCollider != null)
        {
            solidCollider.enabled =
                true;
        }
    }

    private void SetHidden()
    {
        if (solidCollider != null)
            solidCollider.enabled = false;

        if (renderer != null)
        {
            renderer.color =
                new Color(
                    authoredColor.r,
                    authoredColor.g,
                    authoredColor.b,
                    0f);

            renderer.enabled =
                false;
        }

        transform.localScale =
            authoredScale;
    }

    private void OnDisable()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
    }
}

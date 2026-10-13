using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Applies LevelData.CrystalGateData.RootScale to the complete runtime gate
/// assembly after CrystalWorldMechanicRuntime has spawned it.
///
/// RootScale is intentionally separate from PlacementData.Scale:
/// - Scale keeps the proven internal leaf/collider proportions.
/// - RootScale behaves exactly like manually changing the CrystalGate root
///   Transform scale in the Unity Inspector.
/// </summary>
[DefaultExecutionOrder(1750)]
[DisallowMultipleComponent]
public sealed class CrystalGateRootScaleRuntime : MonoBehaviour
{
    private LevelManager levelManager;
    private Transform lastLevelObjects;
    private int bindAttemptsRemaining;

    private static FieldInfo currentLevelField;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager manager =
            UnityEngine.Object.FindFirstObjectByType<
                LevelManager>();

        if (manager == null)
            return;

        if (manager.GetComponent<
                CrystalGateRootScaleRuntime>() == null)
        {
            manager.gameObject.AddComponent<
                CrystalGateRootScaleRuntime>();
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
            bindAttemptsRemaining = 0;
            return;
        }

        if (levelObjects != lastLevelObjects)
        {
            lastLevelObjects =
                levelObjects;

            // CrystalWorldMechanicRuntime also creates its objects after the
            // LevelObjects root appears. Retry briefly so component execution
            // order can never make scaling dependent on a single frame.
            bindAttemptsRemaining =
                16;
        }

        if (bindAttemptsRemaining <= 0)
            return;

        LevelData level =
            ResolveCurrentLevel();

        if (level == null ||
            level.CrystalGates == null)
        {
            bindAttemptsRemaining--;
            return;
        }

        int expected = 0;
        int applied = 0;

        for (int i = 0;
             i < level.CrystalGates.Length;
             i++)
        {
            LevelData.CrystalGateData data =
                level.CrystalGates[i];

            if (data == null)
                continue;

            expected++;

            string group =
                Normalize(
                    data.UnlockGroupId);

            Transform gateRoot =
                levelObjects.Find(
                    "CrystalGate_" +
                    group);

            if (gateRoot == null)
                continue;

            Vector2 resolved =
                ResolveRootScale(
                    data.RootScale);

            gateRoot.localScale =
                new Vector3(
                    resolved.x,
                    resolved.y,
                    1f);

            applied++;
        }

        bindAttemptsRemaining--;

        if (expected == 0 ||
            applied >= expected)
        {
            bindAttemptsRemaining = 0;
        }
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

    private static Vector2 ResolveRootScale(
        Vector2 authored)
    {
        // Existing LevelData assets predate RootScale. Even if Unity loads a
        // missing serialized field as (0,0), preserve legacy behaviour.
        float x =
            Mathf.Abs(authored.x) >
            0.0001f
                ? authored.x
                : 1f;

        float y =
            Mathf.Abs(authored.y) >
            0.0001f
                ? authored.y
                : 1f;

        return new Vector2(
            x,
            y);
    }

    private static string Normalize(
        string value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "default"
            : value.Trim();
    }
}

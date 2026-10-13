using System;
using UnityEngine;

/// <summary>
/// Generic data-driven shield installer for moving flying creatures.
/// A creature opts into shielding through its existing UnlockGroupId.
/// Matching rules live in Resources/UI/ShieldedFlyingCreatureRules.json.
/// There are no per-level conditionals.
/// </summary>
[DefaultExecutionOrder(1610)]
[DisallowMultipleComponent]
public sealed class FlyingCreatureShieldSystemRuntime : MonoBehaviour
{
    private const string RulesResourcePath =
        "UI/ShieldedFlyingCreatureRules";

    [Serializable]
    private sealed class RuleSet
    {
        public Rule[] rules =
            Array.Empty<Rule>();
    }

    [Serializable]
    private sealed class Rule
    {
        public string unlockGroupPrefix =
            "shielded_";

        [Min(0.25f)]
        public float radius =
            1.25f;

        public int sortingOrder =
            14;

        [Min(0.1f)]
        public float pulseSpeed =
            2.35f;

        public float rotationSpeed =
            7f;

        public string visualResourcePath =
            "Art/Gameplay/World2/Effects/SanctumFlyingShield";

        [Min(0.25f)]
        public float visualWorldWidth =
            3.15f;
    }

    private LevelManager levelManager;
    private Transform lastLevelObjects;
    private RuleSet ruleSet =
        new RuleSet();

    private int bindAttemptsRemaining;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        LevelManager manager =
            UnityEngine.Object.FindFirstObjectByType<LevelManager>();

        if (manager == null)
            return;

        if (manager.GetComponent<
                FlyingCreatureShieldSystemRuntime>() == null)
        {
            manager.gameObject.AddComponent<
                FlyingCreatureShieldSystemRuntime>();
        }
    }

    private void Awake()
    {
        levelManager =
            GetComponent<LevelManager>();

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
            bindAttemptsRemaining = 0;
            return;
        }

        if (levelObjects != lastLevelObjects)
        {
            lastLevelObjects =
                levelObjects;

            bindAttemptsRemaining =
                12;
        }

        if (bindAttemptsRemaining <= 0)
            return;

        int attached =
            AttachMatchingShields(
                levelObjects);

        bindAttemptsRemaining--;

        if (attached > 0)
            bindAttemptsRemaining = 0;
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
                "[Flying Shield] Missing Resources/" +
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
                "[Flying Shield] Could not parse rules: " +
                ex.Message);
        }
    }

    private int AttachMatchingShields(
        Transform levelObjects)
    {
        if (levelObjects == null ||
            ruleSet == null ||
            ruleSet.rules == null)
        {
            return 0;
        }

        FlyingBirdTarget[] birds =
            levelObjects.GetComponentsInChildren<
                FlyingBirdTarget>(true);

        int attached = 0;

        for (int i = 0;
             i < birds.Length;
             i++)
        {
            FlyingBirdTarget bird =
                birds[i];

            if (bird == null ||
                bird.GetComponent<
                    ShieldedFlyingCreatureRuntime>() != null)
            {
                continue;
            }

            Rule rule =
                FindRule(
                    bird.UnlockGroupId);

            if (rule == null)
                continue;

            ShieldedFlyingCreatureRuntime shield =
                bird.gameObject.AddComponent<
                    ShieldedFlyingCreatureRuntime>();

            shield.Configure(
                rule.radius,
                rule.sortingOrder,
                rule.pulseSpeed,
                rule.rotationSpeed,
                rule.visualResourcePath,
                rule.visualWorldWidth);

            attached++;
        }

        return attached;
    }

    private Rule FindRule(
        string unlockGroupId)
    {
        if (string.IsNullOrWhiteSpace(
                unlockGroupId) ||
            ruleSet == null ||
            ruleSet.rules == null)
        {
            return null;
        }

        string group =
            unlockGroupId.Trim();

        for (int i = 0;
             i < ruleSet.rules.Length;
             i++)
        {
            Rule rule =
                ruleSet.rules[i];

            if (rule == null ||
                string.IsNullOrWhiteSpace(
                    rule.unlockGroupPrefix))
            {
                continue;
            }

            if (group.StartsWith(
                    rule.unlockGroupPrefix.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }
}

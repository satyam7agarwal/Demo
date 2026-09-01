using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Temporary test helper for KhaemRigTest only.
/// 1 = Idle, 2 = Load, 3 = Hold, 4 = Release, Space = Next.
/// </summary>
public sealed class KhaemAnimationTestDriver : MonoBehaviour
{
    private Animator animator;
    private KhaemBowAttachment bowAttachment;
    private int currentIndex;
    private string currentLabel = "Idle";

    private static readonly string[][] StateCandidates =
    {
        new[] { "HumanF@BowIdle01", "BowIdle01" },
        new[] { "HumanF@BowShot01-Load", "HumanF@BowShot01 - Load", "HumanF@BowShot01_Load",
                "BowShot01-Load", "BowShot01 - Load", "BowShot01_Load" },
        new[] { "HumanF@BowShot01-Hold", "HumanF@BowShot01 - Hold", "HumanF@BowShot01_Hold",
                "BowShot01-Hold", "BowShot01 - Hold", "BowShot01_Hold" },
        new[] { "HumanF@BowShot01-Release", "HumanF@BowShot01 - Release", "HumanF@BowShot01_Release",
                "BowShot01-Release", "BowShot01 - Release", "BowShot01_Release" }
    };

    private static readonly string[] Labels = { "Idle", "Load", "Hold", "Release" };

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Debug.LogError("[KhaemAnimationTestDriver] No Animator found.", this);
            enabled = false;
            return;
        }

        animator.applyRootMotion = false;

        bowAttachment =
            GetComponentInParent<
                KhaemBowAttachment>();
    }

    private void Start() => PlayIndex(0);

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) PlayIndex(0);
        if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) PlayIndex(1);
        if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) PlayIndex(2);
        if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) PlayIndex(3);
        if (keyboard.spaceKey.wasPressedThisFrame) PlayIndex((currentIndex + 1) % StateCandidates.Length);
#else
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) PlayIndex(0);
        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) PlayIndex(1);
        if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) PlayIndex(2);
        if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) PlayIndex(3);
        if (Input.GetKeyDown(KeyCode.Space)) PlayIndex((currentIndex + 1) % StateCandidates.Length);
#endif
    }

    private void PlayIndex(int index)
    {
        if (animator == null) return;

        index = Mathf.Clamp(index, 0, StateCandidates.Length - 1);

        foreach (string stateName in StateCandidates[index])
        {
            int hash = Animator.StringToHash($"Base Layer.{stateName}");
            if (!animator.HasState(0, hash)) continue;

            currentIndex = index;
            currentLabel = Labels[index];
            animator.Play(hash, 0, 0f);
            animator.Update(0f);

            bowAttachment?.SetTestAnimationState(
                index);

            Debug.Log($"[Khaem Test] Playing {currentLabel}: {stateName}", this);
            return;
        }

        Debug.LogWarning(
            $"[Khaem Test] Could not find {Labels[index]} state in Base Layer.",
            this);
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(12, 12, 330, 88), "Khaem Animation Test");
        GUI.Label(new Rect(24, 38, 300, 22), $"Current: {currentLabel}");
        GUI.Label(new Rect(24, 60, 300, 22), "1 Idle   2 Load   3 Hold   4 Release   Space Next");
    }
} 

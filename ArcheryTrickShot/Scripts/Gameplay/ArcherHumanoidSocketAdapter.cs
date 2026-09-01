using UnityEngine;

/// <summary>
/// Builds stable runtime archery sockets from a standard Unity Humanoid.
///
/// This is the normal path for Hyper/Mixamo-style characters:
/// - BowGripSocket is derived from the bow-hand palm/finger roots, not the wrist pivot.
/// - DrawNockSocket is derived from the draw-hand index/middle finger roots, not the wrist pivot.
/// - Sockets are refreshed after Animator/procedural posing every frame.
///
/// The adapter intentionally contains no gameplay rules. It only translates a
/// Humanoid skeleton into the common archery presentation contract.
/// </summary>
[DisallowMultipleComponent]
public sealed class ArcherHumanoidSocketAdapter : MonoBehaviour
{
    private const string BowGripSocketName = "ATS_BowGripSocket";
    private const string DrawNockSocketName = "ATS_DrawNockSocket";

    private Animator animator;
    private Archer3DRuntimeProfile profile;

    private Transform bowHand;
    private Transform drawHand;

    private Transform bowIndexProximal;
    private Transform bowMiddleProximal;
    private Transform bowRingProximal;
    private Transform bowLittleProximal;

    private Transform drawIndexProximal;
    private Transform drawMiddleProximal;
    private Transform drawIndexIntermediate;
    private Transform drawMiddleIntermediate;
    private Transform drawIndexDistal;
    private Transform drawMiddleDistal;
    private Transform drawIndexTip;
    private Transform drawMiddleTip;

    public Transform BowGripSocket { get; private set; }
    public Transform DrawNockSocket { get; private set; }

    public bool HasBowFingerData { get; private set; }
    public bool HasDrawFingerData { get; private set; }

    public void Configure(
        Animator runtimeAnimator,
        Archer3DRuntimeProfile runtimeProfile,
        Transform runtimeBowHand,
        Transform runtimeDrawHand)
    {
        animator = runtimeAnimator;
        profile = runtimeProfile;
        bowHand = runtimeBowHand;
        drawHand = runtimeDrawHand;

        BowGripSocket = EnsureSocket(BowGripSocketName);
        DrawNockSocket = EnsureSocket(DrawNockSocketName);

        ResolveFingerReferences();
        RefreshPose();

        Debug.Log(
            "[Archer Sockets] Humanoid auto-sockets ready. " +
            "bowFingerData=" + HasBowFingerData +
            ", drawFingerData=" + HasDrawFingerData +
            ", bowHand=" + (bowHand != null ? bowHand.name : "NULL") +
            ", drawHand=" + (drawHand != null ? drawHand.name : "NULL"),
            this);

        if (!HasDrawFingerData)
        {
            Debug.LogWarning(
                "[Archer Sockets] No mapped/named index or middle finger bones were found. " +
                "Falling back to the Humanoid draw-hand pivot. Standard Mixamo/Hyper rigs " +
                "normally expose an Index1 chain and do not use this fallback.",
                this);
        }
    }

    public void RefreshPose()
    {
        if (profile == null)
            return;

        if (BowGripSocket != null && bowHand != null)
        {
            Vector3 grip = ResolveBowGripWorldPosition();
            grip += bowHand.TransformVector(profile.BowGripSocketLocalCorrection);
            BowGripSocket.position = grip;
            BowGripSocket.rotation = bowHand.rotation;
        }

        if (DrawNockSocket != null && drawHand != null)
        {
            Vector3 nock = ResolveDrawNockWorldPosition();
            nock += drawHand.TransformVector(profile.DrawNockSocketLocalCorrection);
            DrawNockSocket.position = nock;
            DrawNockSocket.rotation = drawHand.rotation;
        }
    }

    private Transform EnsureSocket(string socketName)
    {
        Transform existing = transform.Find(socketName);

        if (existing != null)
            return existing;

        GameObject socketObject = new GameObject(socketName);
        Transform socket = socketObject.transform;
        socket.SetParent(transform, false);
        return socket;
    }

    private void ResolveFingerReferences()
    {
        bool bowIsLeft = IsLeftHand(bowHand);
        bool drawIsLeft = IsLeftHand(drawHand);

        bowIndexProximal = ResolveFinger(
            bowHand,
            bowIsLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal,
            bowIsLeft ? "lefthandindex1" : "righthandindex1");

        bowMiddleProximal = ResolveFinger(
            bowHand,
            bowIsLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal,
            bowIsLeft ? "lefthandmiddle1" : "righthandmiddle1");

        bowRingProximal = ResolveFinger(
            bowHand,
            bowIsLeft ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal,
            bowIsLeft ? "lefthandring1" : "righthandring1");

        bowLittleProximal = ResolveFinger(
            bowHand,
            bowIsLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal,
            bowIsLeft ? "lefthandpinky1" : "righthandpinky1",
            bowIsLeft ? "lefthandlittle1" : "righthandlittle1");

        drawIndexProximal = ResolveFinger(
            drawHand,
            drawIsLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal,
            drawIsLeft ? "lefthandindex1" : "righthandindex1");

        drawMiddleProximal = ResolveFinger(
            drawHand,
            drawIsLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal,
            drawIsLeft ? "lefthandmiddle1" : "righthandmiddle1");

        drawIndexIntermediate = ResolveFinger(
            drawHand,
            drawIsLeft ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate,
            drawIsLeft ? "lefthandindex2" : "righthandindex2");

        drawMiddleIntermediate = ResolveFinger(
            drawHand,
            drawIsLeft ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate,
            drawIsLeft ? "lefthandmiddle2" : "righthandmiddle2");

        drawIndexDistal = ResolveFinger(
            drawHand,
            drawIsLeft ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal,
            drawIsLeft ? "lefthandindex3" : "righthandindex3");

        drawMiddleDistal = ResolveFinger(
            drawHand,
            drawIsLeft ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal,
            drawIsLeft ? "lefthandmiddle3" : "righthandmiddle3");

        // Mixamo/Hyper exports commonly include an extra end transform (Index4/Middle4)
        // after Unity's mapped Distal bone. Prefer that endpoint when present so the
        // socket is based on the visible finger span, not only the tiny first phalanx.
        drawIndexTip = ResolveFingerTip(
            drawIndexDistal,
            drawIsLeft ? "lefthandindex4" : "righthandindex4");

        drawMiddleTip = ResolveFingerTip(
            drawMiddleDistal,
            drawIsLeft ? "lefthandmiddle4" : "righthandmiddle4");

        HasBowFingerData =
            bowIndexProximal != null ||
            bowMiddleProximal != null ||
            bowRingProximal != null ||
            bowLittleProximal != null;

        HasDrawFingerData =
            drawIndexProximal != null ||
            drawMiddleProximal != null;
    }

    private Vector3 ResolveBowGripWorldPosition()
    {
        Vector3 sum = Vector3.zero;
        int count = 0;

        AddPosition(ref sum, ref count, bowIndexProximal);
        AddPosition(ref sum, ref count, bowMiddleProximal);
        AddPosition(ref sum, ref count, bowRingProximal);
        AddPosition(ref sum, ref count, bowLittleProximal);

        if (count == 0)
        {
            return bowHand.position;
        }

        Vector3 fingerRootCenter = sum / count;

        return Vector3.LerpUnclamped(
            bowHand.position,
            fingerRootCenter,
            profile.AutoBowGripPalmReach);
    }

    private Vector3 ResolveDrawNockWorldPosition()
    {
        Vector3 proximalSum = Vector3.zero;
        int proximalCount = 0;

        AddPosition(ref proximalSum, ref proximalCount, drawIndexProximal);
        AddPosition(ref proximalSum, ref proximalCount, drawMiddleProximal);

        if (proximalCount == 0)
        {
            return drawHand.position;
        }

        Vector3 fingerBase = proximalSum / proximalCount;

        Vector3 tipSum = Vector3.zero;
        int tipCount = 0;

        // Prefer the real exported end transforms, then Unity Distal bones. If a
        // rig does not expose those, fall back to Intermediate. This makes the
        // normalization span the WHOLE visible finger rather than only Index1->2.
        AddPosition(ref tipSum, ref tipCount, drawIndexTip ?? drawIndexDistal ?? drawIndexIntermediate);
        AddPosition(ref tipSum, ref tipCount, drawMiddleTip ?? drawMiddleDistal ?? drawMiddleIntermediate);

        if (tipCount == 0)
        {
            return fingerBase;
        }

        Vector3 fingerTipCenter = tipSum / tipCount;

        // Normalized full-finger reach. Around 0.68 lands near the finger pads /
        // first hook area on standard Mixamo hands, which is where an archery
        // string visually belongs. Because this is proportional to each rig's
        // own finger length, it scales across differently-sized Humanoids.
        float fingerAdvance = Mathf.Clamp(
            profile.AutoDrawNockFingerAdvance,
            0f,
            1f);

        return Vector3.LerpUnclamped(
            fingerBase,
            fingerTipCenter,
            fingerAdvance);
    }


    private Transform ResolveFingerTip(
        Transform distal,
        string normalizedNameHint)
    {
        if (distal == null)
            return null;

        Transform[] descendants =
            distal.GetComponentsInChildren<Transform>(true);

        foreach (Transform candidate in descendants)
        {
            if (candidate == null || candidate == distal)
                continue;

            string normalized =
                NormalizeBoneName(candidate.name);

            if (!string.IsNullOrWhiteSpace(normalizedNameHint) &&
                normalized.Contains(normalizedNameHint))
            {
                return candidate;
            }
        }

        // Some Humanoid exports omit the explicit *4 name but still provide a
        // single end transform under Distal. It is safe to use that endpoint.
        if (distal.childCount == 1)
            return distal.GetChild(0);

        return distal;
    }

    private Transform ResolveFinger(
        Transform hand,
        HumanBodyBones mappedBone,
        params string[] normalizedNameHints)
    {
        if (animator != null)
        {
            Transform mapped = animator.GetBoneTransform(mappedBone);
            if (mapped != null)
                return mapped;
        }

        if (hand == null)
            return null;

        Transform[] descendants = hand.GetComponentsInChildren<Transform>(true);

        foreach (Transform candidate in descendants)
        {
            if (candidate == null || candidate == hand)
                continue;

            string normalized = NormalizeBoneName(candidate.name);

            foreach (string hint in normalizedNameHints)
            {
                if (!string.IsNullOrWhiteSpace(hint) && normalized.Contains(hint))
                    return candidate;
            }
        }

        return null;
    }

    private bool IsLeftHand(Transform hand)
    {
        if (animator == null || hand == null)
            return true;

        Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);

        if (hand == left)
            return true;

        if (hand == right)
            return false;

        string normalized = NormalizeBoneName(hand.name);
        return normalized.Contains("left");
    }

    private static string NormalizeBoneName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static void AddPosition(
        ref Vector3 sum,
        ref int count,
        Transform transformToAdd)
    {
        if (transformToAdd == null)
            return;

        sum += transformToAdd.position;
        count++;
    }
}

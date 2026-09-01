using UnityEngine;
using KevinIglesias;

/// <summary>
/// Runtime adapter around Kevin Iglesias' original bow.
///
/// The original Kevin prefab remains the source of truth for:
/// - limb01 / tip01
/// - limb02 / tip02
/// - B-nockPoint
/// - AnchorPoint
/// - original LineRenderer + material
/// - bowReleaseCurve
///
/// The demo HumanArcherController stays disabled.
///
/// Mobile-polish behaviour:
/// - the real string nock is constrained to the FINAL runtime draw-hand pose
///   while drawing, so hand/string stay visually connected at every aim angle;
/// - bow-limb bend is derived from the actual nock displacement, so it stays
///   synchronized with the visible pull;
/// - release returns the real nock/limbs using Kevin's original release curve.
/// </summary>
[DefaultExecutionOrder(1100)]
[DisallowMultipleComponent]
public sealed class KevinBowRuntimeController : MonoBehaviour
{
    private const float KevinLoadedLimbBendDegrees = 15f;

    // Short blend avoids a one-frame snap when Idle transitions into Load.
    private const float DrawHandAttachSeconds = 0.10f;

    // Matches the short Kevin bow release behaviour while the actual gameplay
    // arrow is launched immediately by BowController.
    private const float KevinReleaseDurationSeconds = 0.15f;

    private HumanArcherController source;

    private LineRenderer bowstringLine;

    private Transform limb01;
    private Transform limb02;
    private Transform tip01;
    private Transform tip02;
    private Transform nockPoint;
    private Transform anchorPoint;

    private Transform drawHand;
    private Vector3 drawHandNockOffsetLocal;

    private Vector3 restNockLocalPosition;
    private Vector3 restLimb01Euler;
    private Vector3 restLimb02Euler;
    private Vector3 loadedLimb01Euler;
    private Vector3 loadedLimb02Euler;

    private Vector3 releaseStartNockWorldPosition;
    private Vector3 releaseStartLimb01Euler;
    private Vector3 releaseStartLimb02Euler;

    private AnimationCurve releaseCurve;

    private float requestedDrawAmount;
    private float drawHandAttach;
    private float releaseElapsed;

    private State state;

    private enum State
    {
        Ready,
        Drawing,
        Releasing
    }

    public bool IsReady { get; private set; }

    public Vector3 NockWorldPosition =>
        nockPoint != null
            ? nockPoint.position
            : transform.position;

    public void Configure(
        Transform characterRoot,
        Transform runtimeBowRoot,
        Transform runtimeDrawHand,
        Vector3 nockOffsetInDrawHandLocal,
        Archer3DRuntimeProfile profile)
    {
        IsReady = false;

        if (characterRoot == null)
        {
            Debug.LogError(
                "[Kevin Bow] Character root is missing.",
                this);
            return;
        }

        source =
            characterRoot
                .GetComponentInChildren<HumanArcherController>(
                    true);

        if (source != null)
        {
            // Never run Kevin's demo gameplay controller in our game.
            source.enabled = false;

            bowstringLine = source.bowstringLine;
            limb01 = source.limb01;
            limb02 = source.limb02;
            tip01 = source.tip01;
            tip02 = source.tip02;
            nockPoint = source.nockPoint;
            anchorPoint = source.bowstringAnchorPoint;
            releaseCurve = source.bowReleaseCurve;
        }
        else
        {
            // Retargeted Humanoids (Khaem / Mixamo / Hyper-style characters)
            // still use the shared authored HumanArcher_Bow prefab, but the
            // visual controller may hand us either the prefab root or one of
            // its descendants depending on how the character was assembled.
            //
            // Resolve from both the supplied bow root and the full character
            // hierarchy so character skeleton differences can never break the
            // shared bow internals.
            ResolveOriginalBowRig(
                characterRoot,
                runtimeBowRoot);

            releaseCurve =
                profile != null &&
                profile.BowReleaseCurve != null &&
                profile.BowReleaseCurve.length > 0
                    ? profile.BowReleaseCurve
                    : CreateFallbackReleaseCurve();
        }

        if (!ValidateReferences())
            return;

        drawHand = runtimeDrawHand;
        drawHandNockOffsetLocal =
            nockOffsetInDrawHandLocal;

        restNockLocalPosition =
            nockPoint.localPosition;

        restLimb01Euler =
            limb01.localEulerAngles;

        restLimb02Euler =
            limb02.localEulerAngles;

        loadedLimb01Euler =
            new Vector3(
                restLimb01Euler.x,
                restLimb01Euler.y,
                restLimb01Euler.z -
                KevinLoadedLimbBendDegrees);

        loadedLimb02Euler =
            new Vector3(
                restLimb02Euler.x,
                restLimb02Euler.y,
                restLimb02Euler.z -
                KevinLoadedLimbBendDegrees);

        bowstringLine.positionCount = 3;
        bowstringLine.useWorldSpace = true;
        bowstringLine.enabled = true;

        bowstringLine.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;

        bowstringLine.receiveShadows = false;

        RemoveOldGeneratedRuntimeString(
            characterRoot);

        requestedDrawAmount = 0f;
        drawHandAttach = 0f;
        releaseElapsed = 0f;
        state = State.Ready;

        ApplyReadyPose();
        UpdateOriginalBowstring();

        IsReady = true;

        Debug.Log(
            "[Kevin Bow] Original bow active with live draw-hand nock " +
            "constraint. line=" + bowstringLine.name +
            ", tip01=" + tip01.name +
            ", nock=" + nockPoint.name +
            ", tip02=" + tip02.name +
            ", drawHand=" +
            (drawHand != null
                ? drawHand.name
                : "NULL") +
            ".",
            this);
    }

    private void ResolveOriginalBowRig(
        Transform characterRoot,
        Transform runtimeBowRoot)
    {
        // First try the supplied runtime bow hierarchy. This is the normal and
        // cheapest path.
        bowstringLine =
            runtimeBowRoot != null
                ? runtimeBowRoot.GetComponentInChildren<LineRenderer>(true)
                : null;

        limb01 = FindDescendant(runtimeBowRoot, "B-bowLimb01");
        limb02 = FindDescendant(runtimeBowRoot, "B-bowLimb02");
        tip01 = FindDescendant(runtimeBowRoot, "B-bowTip01");
        tip02 = FindDescendant(runtimeBowRoot, "B-bowTip02");
        nockPoint = FindDescendant(runtimeBowRoot, "B-nockPoint");
        anchorPoint = FindDescendant(runtimeBowRoot, "AnchorPoint");

        if (HasAllRequiredReferences())
            return;

        // Some retargeted-character setups expose a nested mesh/transform as
        // the visual bow rather than the prefab root. Walk upward before
        // falling back to a character-wide lookup.
        Transform ancestor = runtimeBowRoot != null
            ? runtimeBowRoot.parent
            : null;

        while (ancestor != null &&
               ancestor != characterRoot.parent)
        {
            if (bowstringLine == null)
            {
                bowstringLine =
                    ancestor.GetComponentInChildren<LineRenderer>(true);
            }

            limb01 ??= FindDescendant(ancestor, "B-bowLimb01");
            limb02 ??= FindDescendant(ancestor, "B-bowLimb02");
            tip01 ??= FindDescendant(ancestor, "B-bowTip01");
            tip02 ??= FindDescendant(ancestor, "B-bowTip02");
            nockPoint ??= FindDescendant(ancestor, "B-nockPoint");
            anchorPoint ??= FindDescendant(ancestor, "AnchorPoint");

            if (HasAllRequiredReferences())
                return;

            if (ancestor == characterRoot)
                break;

            ancestor = ancestor.parent;
        }

        // Final robust fallback: the character presentation hierarchy owns
        // exactly one shared runtime bow. Search it by the authored bow names.
        // This keeps Khaem and future standard Humanoids independent of their
        // own wrist/finger hierarchy and prefab nesting.
        if (characterRoot != null)
        {
            if (bowstringLine == null)
            {
                LineRenderer[] lines =
                    characterRoot.GetComponentsInChildren<LineRenderer>(true);

                foreach (LineRenderer line in lines)
                {
                    if (line == null)
                        continue;

                    Transform lineRoot = line.transform;

                    if (FindDescendant(lineRoot, "B-nockPoint") != null ||
                        FindDescendant(lineRoot, "B-bowLimb01") != null)
                    {
                        bowstringLine = line;
                        break;
                    }
                }

                if (bowstringLine == null && lines.Length == 1)
                {
                    bowstringLine = lines[0];
                }
            }

            limb01 ??= FindDescendant(characterRoot, "B-bowLimb01");
            limb02 ??= FindDescendant(characterRoot, "B-bowLimb02");
            tip01 ??= FindDescendant(characterRoot, "B-bowTip01");
            tip02 ??= FindDescendant(characterRoot, "B-bowTip02");
            nockPoint ??= FindDescendant(characterRoot, "B-nockPoint");
            anchorPoint ??= FindDescendant(characterRoot, "AnchorPoint");
        }
    }

    private bool HasAllRequiredReferences()
    {
        return
            bowstringLine != null &&
            limb01 != null &&
            limb02 != null &&
            tip01 != null &&
            tip02 != null &&
            nockPoint != null &&
            anchorPoint != null;
    }

    private static Transform FindDescendant(
        Transform root,
        string exactName)
    {
        if (root == null ||
            string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        Transform[] descendants =
            root.GetComponentsInChildren<Transform>(true);

        foreach (Transform candidate in descendants)
        {
            if (candidate != null &&
                candidate.name.Equals(
                    exactName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static AnimationCurve
        CreateFallbackReleaseCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.34f, 1.16f),
            new Keyframe(0.71f, 0.87f),
            new Keyframe(1f, 1f));
    }

    /// <summary>
    /// Called after Archer3DVisualController has corrected which physical hand
    /// is holding the bow. This keeps the nock attached to the real draw hand,
    /// not to a profile guess.
    /// </summary>
    public void SetDrawHand(
        Transform runtimeDrawHand,
        Vector3 nockOffsetInDrawHandLocal)
    {
        drawHand = runtimeDrawHand;
        drawHandNockOffsetLocal =
            nockOffsetInDrawHandLocal;
    }

    public void SetReady()
    {
        if (!IsReady)
            return;

        state = State.Ready;
        requestedDrawAmount = 0f;
        drawHandAttach = 0f;
        releaseElapsed = 0f;

        ApplyReadyPose();
        UpdateOriginalBowstring();
    }

    public void BeginDraw()
    {
        if (!IsReady)
            return;

        state = State.Drawing;
        requestedDrawAmount = 0f;
        drawHandAttach = 0f;
        releaseElapsed = 0f;

        bowstringLine.enabled = true;
    }

    public void SetDrawAmount(
        float normalizedDrawAmount)
    {
        if (!IsReady)
            return;

        requestedDrawAmount =
            Mathf.Clamp01(
                normalizedDrawAmount);

        if (state == State.Ready)
            state = State.Drawing;
    }

    public void Release()
    {
        if (!IsReady)
            return;

        // Capture the exact visible pose from the last draw frame. This makes
        // the string snap back from where the user's hand actually was.
        releaseStartNockWorldPosition =
            nockPoint.position;

        releaseStartLimb01Euler =
            limb01.localEulerAngles;

        releaseStartLimb02Euler =
            limb02.localEulerAngles;

        state = State.Releasing;
        releaseElapsed = 0f;
    }

    public void CancelDraw()
    {
        if (!IsReady)
            return;

        state = State.Ready;
        requestedDrawAmount = 0f;
        drawHandAttach = 0f;
        releaseElapsed = 0f;

        ApplyReadyPose();
        UpdateOriginalBowstring();
    }

    /// <summary>
    /// Called explicitly by Archer3DVisualController after Animator + full-body
    /// procedural aim + stable bow binding have all finished for the frame.
    /// Therefore drawHand, bow tips and bow root are all in their FINAL visible
    /// positions before the string is updated.
    /// </summary>
    public void ApplyAfterArcherPose(
        float deltaTime)
    {
        if (!IsReady)
            return;

        float dt =
            Mathf.Max(
                0.0001f,
                deltaTime);

        switch (state)
        {
            case State.Drawing:
                ApplyDrawingPose(dt);
                break;

            case State.Releasing:
                ApplyReleasePose(dt);
                break;

            default:
                ApplyReadyPose();
                break;
        }

        UpdateOriginalBowstring();
    }

    private void ApplyDrawingPose(
        float dt)
    {
        Vector3 restWorld =
            GetRestNockWorldPosition();

        Vector3 handTarget =
            drawHand != null
                ? drawHand.TransformPoint(
                    drawHandNockOffsetLocal)
                : anchorPoint.position;

        // Blend the constraint in over a very short interval. The actual
        // movement after that comes from Kevin's authored arm animation plus
        // our procedural full-body aim, so no second smoothing layer is added.
        drawHandAttach =
            Mathf.MoveTowards(
                drawHandAttach,
                1f,
                dt /
                DrawHandAttachSeconds);

        float attach =
            Mathf.SmoothStep(
                0f,
                1f,
                drawHandAttach);

        nockPoint.position =
            Vector3.Lerp(
                restWorld,
                handTarget,
                attach);

        // Bow flex comes from the REAL visible nock displacement. This keeps
        // limb bend synchronized with the hand/string instead of using an
        // unrelated pointer-distance animation.
        float authoredFullDrawDistance =
            Mathf.Max(
                0.001f,
                Vector3.Distance(
                    restWorld,
                    anchorPoint.position));

        float currentPullDistance =
            Vector3.Distance(
                restWorld,
                nockPoint.position);

        float geometryDraw =
            Mathf.Clamp01(
                currentPullDistance /
                authoredFullDrawDistance);

        // requestedDrawAmount remains a soft cap only during the first few
        // frames so a press does not instantly over-flex the limbs.
        float pointerGate =
            Mathf.Lerp(
                0.35f,
                1f,
                requestedDrawAmount);

        float limbDraw =
            Mathf.Clamp01(
                geometryDraw *
                pointerGate);

        ApplyLimbPose(
            limbDraw);
    }

    private void ApplyReleasePose(
        float dt)
    {
        releaseElapsed += dt;

        float releaseT =
            Mathf.Clamp01(
                releaseElapsed /
                KevinReleaseDurationSeconds);

        float evaluated =
            releaseCurve != null &&
            releaseCurve.length > 0
                ? releaseCurve.Evaluate(
                    releaseT)
                : releaseT;

        Vector3 restWorld =
            GetRestNockWorldPosition();

        nockPoint.position =
            Vector3.LerpUnclamped(
                releaseStartNockWorldPosition,
                restWorld,
                evaluated);

        limb01.localEulerAngles =
            LerpEulerUnclamped(
                releaseStartLimb01Euler,
                restLimb01Euler,
                evaluated);

        limb02.localEulerAngles =
            LerpEulerUnclamped(
                releaseStartLimb02Euler,
                restLimb02Euler,
                evaluated);

        if (releaseT >= 1f)
        {
            state = State.Ready;
            requestedDrawAmount = 0f;
            drawHandAttach = 0f;
            releaseElapsed = 0f;

            ApplyReadyPose();
        }
    }

    private void ApplyReadyPose()
    {
        if (!IsReferenceSet())
            return;

        limb01.localEulerAngles =
            restLimb01Euler;

        limb02.localEulerAngles =
            restLimb02Euler;

        nockPoint.localPosition =
            restNockLocalPosition;
    }

    private void ApplyLimbPose(
        float drawAmount)
    {
        limb01.localEulerAngles =
            Vector3.Lerp(
                restLimb01Euler,
                loadedLimb01Euler,
                drawAmount);

        limb02.localEulerAngles =
            Vector3.Lerp(
                restLimb02Euler,
                loadedLimb02Euler,
                drawAmount);
    }

    private Vector3 GetRestNockWorldPosition()
    {
        if (nockPoint == null)
            return transform.position;

        return nockPoint.parent != null
            ? nockPoint.parent.TransformPoint(
                restNockLocalPosition)
            : nockPoint.position;
    }

    private static Vector3 LerpEulerUnclamped(
        Vector3 from,
        Vector3 to,
        float t)
    {
        return new Vector3(
            from.x +
            Mathf.DeltaAngle(
                from.x,
                to.x) * t,
            from.y +
            Mathf.DeltaAngle(
                from.y,
                to.y) * t,
            from.z +
            Mathf.DeltaAngle(
                from.z,
                to.z) * t);
    }

    private void UpdateOriginalBowstring()
    {
        if (bowstringLine == null ||
            tip01 == null ||
            tip02 == null ||
            nockPoint == null)
        {
            return;
        }

        bowstringLine.SetPosition(
            0,
            tip01.position);

        bowstringLine.SetPosition(
            1,
            nockPoint.position);

        bowstringLine.SetPosition(
            2,
            tip02.position);
    }

    private bool ValidateReferences()
    {
        if (bowstringLine != null &&
            limb01 != null &&
            limb02 != null &&
            tip01 != null &&
            tip02 != null &&
            nockPoint != null &&
            anchorPoint != null)
        {
            return true;
        }

        Debug.LogError(
            "[Archer Bow] Shared bow rig could not be resolved. Missing: " +
            BuildMissingReferenceList() +
            ". This is a bow-prefab/setup issue, not a character hand/socket issue.",
            this);

        return false;
    }

    private string BuildMissingReferenceList()
    {
        System.Collections.Generic.List<string> missing =
            new System.Collections.Generic.List<string>();

        if (bowstringLine == null) missing.Add("LineRenderer");
        if (limb01 == null) missing.Add("B-bowLimb01");
        if (limb02 == null) missing.Add("B-bowLimb02");
        if (tip01 == null) missing.Add("B-bowTip01");
        if (tip02 == null) missing.Add("B-bowTip02");
        if (nockPoint == null) missing.Add("B-nockPoint");
        if (anchorPoint == null) missing.Add("AnchorPoint");

        return missing.Count > 0
            ? string.Join(", ", missing)
            : "unknown reference";
    }

    private bool IsReferenceSet()
    {
        return
            limb01 != null &&
            limb02 != null &&
            nockPoint != null &&
            anchorPoint != null;
    }

    private static void RemoveOldGeneratedRuntimeString(
        Transform characterRoot)
    {
        if (characterRoot == null)
            return;

        Transform[] transforms =
            characterRoot
                .GetComponentsInChildren<Transform>(
                    true);

        foreach (Transform candidate
                 in transforms)
        {
            if (candidate == null)
                continue;

            if (candidate.name !=
                "RuntimeBowString")
            {
                continue;
            }

            candidate.gameObject.SetActive(false);
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mobile-safe character preview renderer.
///
/// Important rules:
/// - exactly one preview character exists at a time;
/// - every character switch starts from a freshly-cleared RenderTexture;
/// - the preview camera is rendered explicitly instead of depending on Unity's
///   camera scheduling (which can differ on Android);
/// - the showcase motion uses unscaled time and does not depend on gameplay.
/// </summary>
public sealed class ATSCharacterPreview : MonoBehaviour
{
    private const int PreviewLayer = 30;
    private const float PreviewRenderFps = 30f;
    private const bool ForceUiRefreshEachPreviewFrame = true;

    // Preview quality is based on the actual UI preview area. Phones are capped
    // at 768 px on the long edge; large/high-resolution screens can use 1024.
    // This keeps the character sharp on tablets without rendering a full-screen RT.
    private const int PreviewMinimumLongEdge = 512;
    private const int PreviewPhoneMaxLongEdge = 768;
    private const int PreviewLargeScreenMaxLongEdge = 1024;
    private const int LargeScreenMinPixelDimension = 1200;
    private const int PreviewSizeStep = 32;
    private static int nextPreviewSlot;

    private RawImage target;
    private Camera previewCamera;
    private RenderTexture texture;
    private GameObject previewInstance;
    private Transform previewRoot;
    private Transform characterRoot;
    private Animator previewAnimator;
    private Archer3DRuntimeProfile currentProfile;

    private Vector3 showcaseBasePosition;
    private Quaternion showcaseBaseRotation;
    private Vector3 showcaseBaseScale;
    private float showcaseHeight = 1f;
    private float showcasePhase;
    private bool previewVisible;
    private int previewSlot = -1;
    private float nextRenderTime;
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private Vector2Int currentTextureSize;

    // V4.3 fixed Android ghosting by clearing the preview buffer explicitly on
    // every rendered preview frame. Keep that behavior, but clear to transparent
    // so the authored ruins/menu artwork remains visible behind the character.
    private static readonly Color PreviewBackground = new Color(0f, 0f, 0f, 0f);

    public void Initialize(RawImage targetImage)
    {
        target = targetImage;
        ConfigureTargetImage();
        EnsureCamera();
        EnsureRenderTextureForTarget(true);
    }

    public void SetTarget(RawImage targetImage)
    {
        if (target != null && target != targetImage)
        {
            target.texture = null;
            target.enabled = false;
        }

        target = targetImage;
        ConfigureTargetImage();
        EnsureRenderTextureForTarget(true);

        if (target != null)
        {
            target.texture = texture;
            target.enabled = previewVisible && previewInstance != null;
        }
    }

    public void Show(Archer3DRuntimeProfile profile)
    {
        EnsureCamera();

        if (profile == null || profile.ArcherPrefab == null)
        {
            ClearCharacter();
            ClearRenderTexture();
            SetVisible(false);
            return;
        }

        // Reuse the same hero when navigating between Main and Character Select.
        if (previewInstance != null && currentProfile == profile)
        {
            SetVisible(true);
            EnsureAnimatorRunning();
            RenderPreviewNow();
            return;
        }

        // Hide/retire the previous model first, then use a freshly-cleared RT.
        // Recreate the RT on an actual character switch so no driver can sample
        // stale contents from the previous hero.
        ClearCharacter();
        EnsureRenderTextureForTarget(true);

        currentProfile = profile;
        previewInstance = Instantiate(profile.ArcherPrefab, characterRoot, false);
        previewInstance.name = "Preview_" + profile.CharacterId;
        previewInstance.transform.localPosition = Vector3.zero;
        previewInstance.transform.localRotation = Quaternion.Euler(profile.LocalEulerAngles);
        previewInstance.transform.localScale = profile.LocalScale;

        SetLayerRecursive(previewInstance.transform, PreviewLayer);
        DisableRuntimeComponents(previewInstance);
        ApplyMaterial(profile, previewInstance);

        previewAnimator = previewInstance.GetComponentInChildren<Animator>(true);
        ConfigureAnimator(profile);

        ScaleToProfileHeight(profile);
        CaptureShowcasePose();
        FitCamera();

        previewVisible = true;
        previewInstance.SetActive(true);

        if (target != null)
        {
            target.texture = texture;
            target.enabled = true;
        }

        // Camera stays disabled: LateUpdate renders it explicitly after the
        // Animator/showcase pose has been evaluated.
        previewCamera.enabled = false;
        RenderPreviewNow();
    }

    public void SetVisible(bool visible)
    {
        previewVisible = visible;

        if (previewInstance != null)
            previewInstance.SetActive(visible);

        if (previewCamera != null)
            previewCamera.enabled = false;

        if (target != null)
            target.enabled = visible && previewInstance != null;

        if (visible)
        {
            EnsureAnimatorRunning();
            nextRenderTime = 0f;
            RenderPreviewNow();
        }
    }

    private void LateUpdate()
    {
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
            EnsureRenderTextureForTarget(false);

        if (!previewVisible || previewInstance == null || !previewInstance.activeInHierarchy)
            return;

        // Deliberately visible but restrained showroom motion. This is separate
        // from the gameplay Animator, so the mobile menu never looks frozen.
        float t = Time.unscaledTime + showcasePhase;
        float vertical = Mathf.Sin(t * 1.35f) * showcaseHeight * 0.010f;
        float yaw = Mathf.Sin(t * 0.62f) * 5.0f;
        float roll = Mathf.Sin(t * 0.93f + 0.8f) * 0.55f;
        float breathe = 1f + Mathf.Sin(t * 1.55f) * 0.0025f;

        previewInstance.transform.localPosition =
            showcaseBasePosition + Vector3.up * vertical;
        previewInstance.transform.localRotation =
            showcaseBaseRotation * Quaternion.Euler(0f, yaw, roll);
        previewInstance.transform.localScale = showcaseBaseScale * breathe;

        // Render at a stable 30 fps in menus. This is enough for showcase motion
        // and is cheaper than a second continuously-enabled camera on Android.
        if (Time.unscaledTime >= nextRenderTime)
        {
            nextRenderTime = Time.unscaledTime + (1f / PreviewRenderFps);
            RenderPreviewNow();
        }
    }

    private void EnsureCamera()
    {
        if (previewRoot == null)
        {
            if (previewSlot < 0)
                previewSlot = nextPreviewSlot++;

            GameObject root = new GameObject("CharacterPreviewRoot");
            root.transform.SetParent(transform, false);

            float x = 4000f + (previewSlot % 128) * 160f;
            root.transform.position = new Vector3(x, 4000f, 4000f);
            previewRoot = root.transform;

            GameObject characters = new GameObject("CharacterRoot");
            characters.transform.SetParent(previewRoot, false);
            characterRoot = characters.transform;
        }
        else if (characterRoot == null)
        {
            Transform found = previewRoot.Find("CharacterRoot");
            if (found != null)
                characterRoot = found;
            else
            {
                GameObject characters = new GameObject("CharacterRoot");
                characters.transform.SetParent(previewRoot, false);
                characterRoot = characters.transform;
            }
        }

        if (previewCamera == null)
        {
            GameObject cameraObject = new GameObject("CharacterPreviewCamera", typeof(Camera));
            cameraObject.transform.SetParent(previewRoot, false);
            previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = PreviewBackground;
            previewCamera.orthographic = true;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 50f;
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = false;
            previewCamera.enabled = false;
        }
    }

    private void EnsureRenderTextureForTarget(bool forceRecreate)
    {
        Vector2Int desiredSize = CalculateRecommendedTextureSize();
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        if (!forceRecreate && texture != null && texture.IsCreated() && currentTextureSize == desiredSize)
            return;

        CreateFreshRenderTexture(desiredSize.x, desiredSize.y);

        if (previewInstance != null)
            FitCamera();
    }

    private Vector2Int CalculateRecommendedTextureSize()
    {
        float targetPixelWidth = 0f;
        float targetPixelHeight = 0f;
        float aspect = 0.8f;

        if (target != null && target.rectTransform != null)
        {
            Rect rect = target.rectTransform.rect;
            Canvas canvas = target.canvas;
            float canvasScale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;

            targetPixelWidth = Mathf.Abs(rect.width) * canvasScale;
            targetPixelHeight = Mathf.Abs(rect.height) * canvasScale;

            if (targetPixelWidth > 1f && targetPixelHeight > 1f)
                aspect = targetPixelWidth / targetPixelHeight;
        }

        int minimumScreenDimension = Mathf.Min(Screen.width, Screen.height);
        int maxLongEdge = minimumScreenDimension >= LargeScreenMinPixelDimension
            ? PreviewLargeScreenMaxLongEdge
            : PreviewPhoneMaxLongEdge;

        float requestedLongEdge = Mathf.Max(targetPixelWidth, targetPixelHeight);
        if (requestedLongEdge < 1f)
            requestedLongEdge = maxLongEdge;

        // A small oversample avoids soft scaling while still respecting the device cap.
        int longEdge = RoundToStep(
            Mathf.RoundToInt(Mathf.Clamp(requestedLongEdge * 1.08f, PreviewMinimumLongEdge, maxLongEdge)),
            PreviewSizeStep);

        int width;
        int height;
        if (aspect >= 1f)
        {
            width = longEdge;
            height = RoundToStep(Mathf.RoundToInt(longEdge / Mathf.Max(0.1f, aspect)), PreviewSizeStep);
        }
        else
        {
            height = longEdge;
            width = RoundToStep(Mathf.RoundToInt(longEdge * Mathf.Max(0.1f, aspect)), PreviewSizeStep);
        }

        width = Mathf.Clamp(width, 256, maxLongEdge);
        height = Mathf.Clamp(height, 256, maxLongEdge);
        return new Vector2Int(width, height);
    }

    private static int RoundToStep(int value, int step)
    {
        return Mathf.Max(step, Mathf.CeilToInt(value / (float)step) * step);
    }

    private void CreateFreshRenderTexture(int width, int height)
    {
        RenderTexture old = texture;

        texture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
        {
            name = $"ATS_CharacterPreviewRT_{width}x{height}",
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.Create();
        currentTextureSize = new Vector2Int(width, height);

        if (previewCamera != null)
            previewCamera.targetTexture = texture;
        if (target != null)
        {
            target.texture = texture;
            target.canvasRenderer.SetTexture(texture);
        }

        ClearRenderTexture();

        if (old != null)
        {
            old.Release();
            Destroy(old);
        }
    }

    private void ConfigureTargetImage()
    {
        if (target == null)
            return;

        target.color = Color.white;
        target.raycastTarget = false;
    }

    private void ClearRenderTexture()
    {
        if (texture == null)
            return;

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        GL.Clear(true, true, PreviewBackground);
        RenderTexture.active = previous;
    }

    private void RenderPreviewNow()
    {
        if (!previewVisible || previewCamera == null || texture == null || previewInstance == null)
            return;

        // Keep the V4.3 mobile-safe behavior: explicitly discard and clear every
        // rendered preview frame. The clear color is transparent, so only the
        // character is composited over the authored menu background.
        texture.DiscardContents();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        GL.Clear(true, true, PreviewBackground);
        RenderTexture.active = previous;

        previewCamera.targetTexture = texture;
        previewCamera.backgroundColor = PreviewBackground;
        previewCamera.Render();

        // RawImage normally picks up a RenderTexture update automatically. A few
        // mobile UI/driver paths can keep the previous sampled surface in the
        // CanvasRenderer. Re-submit the texture explicitly after each preview
        // render. This runs only while Main/Character Select is visible.
        if (ForceUiRefreshEachPreviewFrame && target != null && target.enabled)
        {
            target.canvasRenderer.SetTexture(texture);
            target.SetMaterialDirty();
            target.SetVerticesDirty();
        }
    }

    private void ConfigureAnimator(Archer3DRuntimeProfile profile)
    {
        if (previewAnimator == null)
            return;

        previewAnimator.enabled = true;
        if (profile.AnimatorController != null)
            previewAnimator.runtimeAnimatorController = profile.AnimatorController;
        previewAnimator.applyRootMotion = false;
        previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        previewAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        previewAnimator.speed = 1f;

        previewAnimator.Rebind();
        int idleHash = Animator.StringToHash("Idle");
        if (previewAnimator.HasState(0, idleHash))
            previewAnimator.Play(idleHash, 0, Random.Range(0f, 0.75f));
        previewAnimator.Update(0f);
    }

    private void EnsureAnimatorRunning()
    {
        if (previewAnimator == null)
            return;

        if (!previewAnimator.enabled)
            previewAnimator.enabled = true;
        previewAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        previewAnimator.speed = 1f;
    }

    private void ScaleToProfileHeight(Archer3DRuntimeProfile profile)
    {
        if (!profile.AutoScaleToDesiredHeight || profile.DesiredWorldHeight <= 0.1f)
            return;

        if (!TryGetBounds(previewInstance, out Bounds bounds) || bounds.size.y <= 0.001f)
            return;

        float scale = profile.DesiredWorldHeight / bounds.size.y;
        previewInstance.transform.localScale *= scale;
    }

    private void CaptureShowcasePose()
    {
        showcaseBasePosition = previewInstance != null ? previewInstance.transform.localPosition : Vector3.zero;
        showcaseBaseRotation = previewInstance != null ? previewInstance.transform.localRotation : Quaternion.identity;
        showcaseBaseScale = previewInstance != null ? previewInstance.transform.localScale : Vector3.one;
        showcasePhase = Random.Range(0f, Mathf.PI * 2f);

        if (TryGetBounds(previewInstance, out Bounds bounds))
            showcaseHeight = Mathf.Max(0.1f, bounds.size.y);
        else
            showcaseHeight = 1f;
    }

    private void FitCamera()
    {
        if (!TryGetBounds(previewInstance, out Bounds bounds))
            return;

        Vector3 center = bounds.center;
        float height = Mathf.Max(0.1f, bounds.size.y);
        float width = Mathf.Max(0.1f, bounds.size.x);
        float aspect = texture != null ? (float)texture.width / texture.height : 0.8f;
        previewCamera.orthographicSize = Mathf.Max(height * 0.515f, (width / Mathf.Max(0.1f, aspect)) * 0.53f);

        Vector3 front = previewInstance.transform.forward;
        if (front.sqrMagnitude < 0.001f)
            front = Vector3.forward;

        previewCamera.transform.position = center + front.normalized * 8f + Vector3.up * (height * 0.01f);
        previewCamera.transform.LookAt(center + Vector3.up * (height * 0.015f), Vector3.up);
    }

    private static bool TryGetBounds(GameObject root, out Bounds bounds)
    {
        Renderer[] renderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : null;
        if (renderers == null || renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bool initialized = false;
        bounds = default;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                continue;

            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return initialized;
    }

    private static void DisableRuntimeComponents(GameObject root)
    {
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null)
                behaviour.enabled = false;
        }

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
            collider.enabled = false;
        foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            body.isKinematic = true;
        foreach (Rigidbody2D body in root.GetComponentsInChildren<Rigidbody2D>(true))
            body.simulated = false;
        foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            source.enabled = false;
    }

    private static void ApplyMaterial(Archer3DRuntimeProfile profile, GameObject root)
    {
        if (profile.CharacterMaterialOverride == null)
            return;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer is LineRenderer || renderer is TrailRenderer || renderer is ParticleSystemRenderer)
                continue;

            Material[] current = renderer.sharedMaterials;
            if (current == null || current.Length == 0)
                continue;

            if (profile.ApplyCharacterMaterialToAllSlots)
            {
                Material[] replacement = new Material[current.Length];
                for (int i = 0; i < replacement.Length; i++)
                    replacement[i] = profile.CharacterMaterialOverride;
                renderer.sharedMaterials = replacement;
            }
            else
            {
                current[0] = profile.CharacterMaterialOverride;
                renderer.sharedMaterials = current;
            }
        }
    }

    private static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursive(root.GetChild(i), layer);
    }

    private void ClearCharacter()
    {
        currentProfile = null;
        previewAnimator = null;

        if (previewInstance != null)
        {
            GameObject oldPreview = previewInstance;
            previewInstance = null;

            oldPreview.SetActive(false);
            SetLayerRecursive(oldPreview.transform, 0);
            oldPreview.transform.position = new Vector3(-100000f, -100000f, -100000f);
            foreach (Renderer renderer in oldPreview.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                    renderer.enabled = false;
            }
            Destroy(oldPreview);
        }

        if (characterRoot != null)
        {
            for (int i = characterRoot.childCount - 1; i >= 0; i--)
            {
                GameObject stale = characterRoot.GetChild(i).gameObject;
                if (stale == null)
                    continue;
                stale.SetActive(false);
                SetLayerRecursive(stale.transform, 0);
                Destroy(stale);
            }
        }
    }

    private void OnDisable()
    {
        if (previewCamera != null)
            previewCamera.enabled = false;
    }

    private void OnDestroy()
    {
        ClearCharacter();
        if (previewCamera != null)
            previewCamera.targetTexture = null;
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
            texture = null;
        }
    }
}

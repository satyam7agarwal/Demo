using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders one selected roster character into a small off-screen RenderTexture.
/// It is active only on the Character Select screen, keeping gameplay overhead at zero.
/// </summary>
public sealed class ATSCharacterPreview : MonoBehaviour
{
    private const int PreviewLayer = 30;

    private RawImage target;
    private Camera previewCamera;
    private RenderTexture texture;
    private GameObject previewInstance;
    private Transform previewRoot;

    public void Initialize(RawImage targetImage)
    {
        target = targetImage;
        EnsureCamera();
    }

    public void Show(Archer3DRuntimeProfile profile)
    {
        EnsureCamera();
        ClearCharacter();

        if (profile == null || profile.ArcherPrefab == null)
        {
            if (target != null)
                target.enabled = false;
            return;
        }

        previewInstance = Instantiate(profile.ArcherPrefab, previewRoot, false);
        previewInstance.name = "Preview_" + profile.CharacterId;
        previewInstance.transform.localPosition = Vector3.zero;
        previewInstance.transform.localRotation = Quaternion.Euler(profile.LocalEulerAngles);
        previewInstance.transform.localScale = profile.LocalScale;

        SetLayerRecursive(previewInstance.transform, PreviewLayer);
        DisableRuntimeComponents(previewInstance);
        ApplyMaterial(profile, previewInstance);

        Animator animator = previewInstance.GetComponentInChildren<Animator>(true);
        if (animator != null && profile.AnimatorController != null)
        {
            animator.runtimeAnimatorController = profile.AnimatorController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        ScaleToProfileHeight(profile);
        FitCamera();

        if (target != null)
        {
            target.texture = texture;
            target.enabled = true;
        }
        previewCamera.enabled = true;
    }

    public void SetVisible(bool visible)
    {
        if (previewCamera != null)
            previewCamera.enabled = visible && previewInstance != null;
        if (target != null)
            target.enabled = visible && previewInstance != null;
    }

    private void EnsureCamera()
    {
        if (previewRoot == null)
        {
            GameObject root = new GameObject("CharacterPreviewRoot");
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(5000f, 5000f, 5000f);
            previewRoot = root.transform;
        }

        if (texture == null)
        {
            texture = new RenderTexture(512, 640, 16, RenderTextureFormat.ARGB32)
            {
                name = "ATS_CharacterPreviewRT",
                antiAliasing = 2,
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();
        }

        if (previewCamera == null)
        {
            GameObject cameraObject = new GameObject("CharacterPreviewCamera", typeof(Camera));
            cameraObject.transform.SetParent(previewRoot, false);
            previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.orthographic = true;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 50f;
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.targetTexture = texture;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;
            previewCamera.enabled = false;
        }
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

    private void FitCamera()
    {
        if (!TryGetBounds(previewInstance, out Bounds bounds))
            return;

        Vector3 center = bounds.center;
        float height = Mathf.Max(0.1f, bounds.size.y);
        float width = Mathf.Max(0.1f, bounds.size.x);
        float aspect = texture != null ? (float)texture.width / texture.height : 0.8f;
        previewCamera.orthographicSize = Mathf.Max(height * 0.57f, (width / Mathf.Max(0.1f, aspect)) * 0.58f);

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
        if (previewInstance != null)
        {
            Destroy(previewInstance);
            previewInstance = null;
        }
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

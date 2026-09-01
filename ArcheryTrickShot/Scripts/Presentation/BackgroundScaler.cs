using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class BackgroundScaler : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool followCamera = true;

    private SpriteRenderer spriteRenderer;
    private float lastOrthographicSize = -1f;
    private float lastAspect = -1f;
    private Sprite lastSprite;

    private void Awake()
    {
        CacheReferences();
        RefreshNow();
    }

    private void OnEnable()
    {
        CacheReferences();
        RefreshNow();
    }

    private void LateUpdate()
    {
        CacheReferences();
        if (targetCamera == null || spriteRenderer == null || spriteRenderer.sprite == null || !targetCamera.orthographic)
            return;

        bool changed =
            !Mathf.Approximately(lastOrthographicSize, targetCamera.orthographicSize) ||
            !Mathf.Approximately(lastAspect, targetCamera.aspect) ||
            lastSprite != spriteRenderer.sprite;

        if (changed)
            RefreshNow();

        if (followCamera)
        {
            Vector3 cameraPosition = targetCamera.transform.position;
            transform.position = new Vector3(cameraPosition.x, cameraPosition.y, transform.position.z);
        }
    }

    private void CacheReferences()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void RefreshNow()
    {
        if (targetCamera == null || spriteRenderer == null || spriteRenderer.sprite == null || !targetCamera.orthographic)
            return;

        float worldHeight = targetCamera.orthographicSize * 2f;
        float worldWidth = worldHeight * targetCamera.aspect;
        Vector2 spriteSize = spriteRenderer.sprite.bounds.size;
        if (spriteSize.x <= 0f || spriteSize.y <= 0f)
            return;

        transform.localScale = new Vector3(worldWidth / spriteSize.x, worldHeight / spriteSize.y, 1f);
        lastOrthographicSize = targetCamera.orthographicSize;
        lastAspect = targetCamera.aspect;
        lastSprite = spriteRenderer.sprite;
    }
}

using UnityEngine;
using UnityEngine.EventSystems;

public sealed class ATSButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private RectTransform rect;
    private Vector3 target = Vector3.one;
    private const float Speed = 14f;

    private void Awake() => rect = transform as RectTransform;
    private void OnEnable() { target = Vector3.one; if (rect != null) rect.localScale = Vector3.one; }

    private void Update()
    {
        if (rect != null)
            rect.localScale = Vector3.Lerp(rect.localScale, target, 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime));
    }

    public void OnPointerEnter(PointerEventData eventData) => target = Vector3.one * 1.025f;
    public void OnPointerExit(PointerEventData eventData) => target = Vector3.one;
    public void OnPointerDown(PointerEventData eventData) => target = Vector3.one * 0.965f;
    public void OnPointerUp(PointerEventData eventData) => target = Vector3.one * 1.025f;
}

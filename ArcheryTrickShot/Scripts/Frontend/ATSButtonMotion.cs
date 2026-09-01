using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Small unscaled-time button response. V2 runs only while a button is actually
/// animating, so idle menu buttons have no per-frame Update cost.
/// </summary>
public sealed class ATSButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private RectTransform rect;
    private Vector3 target = Vector3.one;
    private Coroutine motionRoutine;
    private const float Speed = 16f;

    private void Awake() => rect = transform as RectTransform;

    private void OnEnable()
    {
        target = Vector3.one;
        if (rect != null)
            rect.localScale = Vector3.one;
    }

    private void OnDisable()
    {
        if (motionRoutine != null)
        {
            StopCoroutine(motionRoutine);
            motionRoutine = null;
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => AnimateTo(1.022f);
    public void OnPointerExit(PointerEventData eventData) => AnimateTo(1f);
    public void OnPointerDown(PointerEventData eventData) => AnimateTo(0.968f);
    public void OnPointerUp(PointerEventData eventData) => AnimateTo(eventData.pointerEnter == gameObject ? 1.022f : 1f);

    private void AnimateTo(float scale)
    {
        target = Vector3.one * scale;
        if (motionRoutine == null && isActiveAndEnabled)
            motionRoutine = StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        if (rect == null)
        {
            motionRoutine = null;
            yield break;
        }

        while ((rect.localScale - target).sqrMagnitude > 0.000001f)
        {
            float t = 1f - Mathf.Exp(-Speed * Time.unscaledDeltaTime);
            rect.localScale = Vector3.Lerp(rect.localScale, target, t);
            yield return null;
        }

        rect.localScale = target;
        motionRoutine = null;
    }
}

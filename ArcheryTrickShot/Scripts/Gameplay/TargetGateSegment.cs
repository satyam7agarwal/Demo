using System.Collections;
using UnityEngine;

/// <summary>
/// One physical section of the sealed target gate. It starts at its authored
/// LevelData position and slides by a data-driven offset when opened.
/// </summary>
[DisallowMultipleComponent]
public sealed class TargetGateSegment : MonoBehaviour
{
    private Vector3 closedPosition;
    private Vector3 openPosition;
    private float openDuration;
    private bool opened;
    private Coroutine openRoutine;

    public void Configure(
        Vector2 openOffset,
        float duration)
    {
        closedPosition =
            transform.position;

        openPosition =
            closedPosition +
            new Vector3(
                openOffset.x,
                openOffset.y,
                0f);

        openDuration =
            Mathf.Max(
                0.1f,
                duration);
    }

    public void Open()
    {
        if (opened)
            return;

        opened = true;

        if (openRoutine != null)
            StopCoroutine(openRoutine);

        openRoutine =
            StartCoroutine(
                OpenRoutine());
    }

    private IEnumerator OpenRoutine()
    {
        float elapsed = 0f;

        while (elapsed < openDuration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    openDuration);

            float eased =
                t * t *
                (3f - 2f * t);

            transform.position =
                Vector3.LerpUnclamped(
                    closedPosition,
                    openPosition,
                    eased);

            yield return null;
        }

        transform.position =
            openPosition;

        openRoutine = null;
    }

    private void OnDisable()
    {
        if (openRoutine != null)
        {
            StopCoroutine(openRoutine);
            openRoutine = null;
        }
    }
}

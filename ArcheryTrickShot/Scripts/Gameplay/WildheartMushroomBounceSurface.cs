using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Visual response only. The thin trigger's position/rotation never animates:
/// physics and aim prediction stay stable during squash / rebound.
/// </summary>
[DisallowMultipleComponent]
public sealed class WildheartMushroomBounceSurface : MonoBehaviour
{
    private const float RehitCooldown = 0.11f;
    private Transform visual;
    private Vector3 initialScale;
    private AudioSource audioSource;
    private ParticleSystem spores;
    private Coroutine bounceRoutine;
    private float lastImpactTime = -100f;

    internal void Configure(Transform animatedVisual, AudioClip boing)
    {
        visual = animatedVisual;
        initialScale = visual != null ? visual.localScale : Vector3.one;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0.74f;
        audioSource.clip = boing;

        if (spores == null)
            CreateSporeBurst();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        ArrowController arrow = other != null
            ? other.GetComponentInParent<ArrowController>()
            : null;
        if (arrow == null || arrow.GetVelocity().sqrMagnitude < 0.1f ||
            Time.time - lastImpactTime < RehitCooldown)
            return;

        lastImpactTime = Time.time;
        if (bounceRoutine != null)
        {
            StopCoroutine(bounceRoutine);
            if (visual != null)
                visual.localScale = initialScale;
        }

        if (visual != null)
            bounceRoutine = StartCoroutine(SquashAndRebound());

        if (audioSource != null && audioSource.clip != null)
        {
            audioSource.pitch = UnityEngine.Random.Range(0.96f, 1.08f);
            audioSource.Play();
        }

        if (spores != null)
        {
            spores.transform.position = transform.position;
            spores.Emit(16);
        }
    }

    private IEnumerator SquashAndRebound()
    {
        // Keeping the Collider2D stationary is essential: a changing collider
        // would invalidate the trajectory preview at the exact bounce moment.
        Vector3 compressed = new Vector3(
            initialScale.x * 1.08f, initialScale.y * 0.68f, initialScale.z);
        Vector3 released = new Vector3(
            initialScale.x * 0.96f, initialScale.y * 1.17f, initialScale.z);

        yield return AnimateScale(initialScale, compressed, 0.11f);
        yield return AnimateScale(compressed, released, 0.16f);
        yield return AnimateScale(released, initialScale, 0.19f);
        if (visual != null)
            visual.localScale = initialScale;
        bounceRoutine = null;
    }

    private IEnumerator AnimateScale(Vector3 from, Vector3 to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && visual != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smooth = t * t * (3f - 2f * t);
            visual.localScale = Vector3.LerpUnclamped(from, to, smooth);
            yield return null;
        }
    }

    private void CreateSporeBurst()
    {
        // Newly-added ParticleSystems automatically play on an active GameObject.
        // Configure them while inactive so changing duration never triggers
        // Unity's "cannot set duration whilst playing" warning.
        GameObject go = new GameObject("MushroomSpores");
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        spores = go.AddComponent<ParticleSystem>();
        var main = spores.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.65f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.58f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.105f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.95f, 0.56f, 0.9f),
            new Color(0.76f, 1f, 0.8f, 0.9f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.maxParticles = 48;
        var emission = spores.emission;
        emission.rateOverTime = 0f;
        var shape = spores.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.24f;
        var renderer = spores.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
            renderer.sortingOrder = 14;
        spores.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.SetActive(true);
    }

    private void OnDisable()
    {
        if (visual != null)
            visual.localScale = initialScale;
    }
}

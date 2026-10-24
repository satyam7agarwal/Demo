using UnityEngine;

/// <summary>
/// Collision callback owned by a solid stalk. No invisible button, ring, or
/// tiny hit target: shoot the visible front bamboo anywhere along its trunk.
/// </summary>
[DisallowMultipleComponent]
public sealed class BambooDominoHitReceiver : MonoBehaviour
{
    private WildheartBambooDominoRuntime runtime;
    private int stalkIndex;

    public void Configure(WildheartBambooDominoRuntime owner, int index)
    {
        runtime = owner;
        stalkIndex = index;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (runtime == null || collision == null)
            return;
        // Unity exposes both sides of a Collision2D contact. Resolve either
        // side, so this works independently of which collider generated the
        // callback (static bamboo collider vs moving arrow rigidbody).
        ArrowController arrow = collision.collider != null
            ? collision.collider.GetComponentInParent<ArrowController>() : null;
        if (arrow == null && collision.otherCollider != null)
            arrow = collision.otherCollider.GetComponentInParent<ArrowController>();
        // ArrowController may process its solid-stop callback before this one,
        // so HasFired is the reliable indicator (IsStopped is not).
        if (arrow != null && arrow.HasFired)
            runtime.ReportArrowHit(stalkIndex, arrow);
    }
}

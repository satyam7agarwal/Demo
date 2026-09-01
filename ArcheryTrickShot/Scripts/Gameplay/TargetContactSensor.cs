using UnityEngine;

public enum TargetContactKind
{
    ScoringFace = 0,
    PhysicalPart = 1
}

/// <summary>
/// Small forwarding component that lives beside a target trigger collider.
/// It keeps Target.cs on the root while allowing scoring and physical
/// collision geometry to be split into separate child GameObjects.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class TargetContactSensor : MonoBehaviour
{
    [SerializeField]
    private TargetContactKind kind = TargetContactKind.PhysicalPart;

    private Target target;
    private Collider2D sensorCollider;

    public TargetContactKind Kind => kind;
    public Collider2D SensorCollider => sensorCollider;

    private void Awake()
    {
        target = GetComponentInParent<Target>();
        sensorCollider = GetComponent<Collider2D>();

        if (sensorCollider != null)
            sensorCollider.isTrigger = true;
    }

    public void Configure(TargetContactKind contactKind)
    {
        kind = contactKind;

        if (sensorCollider == null)
            sensorCollider = GetComponent<Collider2D>();

        if (sensorCollider != null)
            sensorCollider.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (target == null)
            target = GetComponentInParent<Target>();

        if (sensorCollider == null)
            sensorCollider = GetComponent<Collider2D>();

        target?.HandleSensorEnter(
            kind,
            sensorCollider,
            other);
    }
}

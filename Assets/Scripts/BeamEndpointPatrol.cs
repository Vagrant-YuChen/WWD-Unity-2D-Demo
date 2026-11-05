using UnityEngine;

[DisallowMultipleComponent]
public sealed class BeamEndpointPatrol : MonoBehaviour
{
    [Header("Beam endpoints")]
    [SerializeField] private Transform lowerLeftEndpoint;
    [SerializeField] private Transform upperRightEndpoint;
    [SerializeField, Min(0.01f)] private float speed = 0.5f;
    [SerializeField] private bool startAtLowerLeft = true;
    [SerializeField] private bool drawGizmos = true;

    private bool movingToUpperRight;
    private bool initialized;

    public void Configure(Transform lowerLeft, Transform upperRight)
    {
        lowerLeftEndpoint = lowerLeft;
        upperRightEndpoint = upperRight;
    }

    private void Awake()
    {
        if (lowerLeftEndpoint == null || upperRightEndpoint == null)
        {
            Debug.LogError($"[{nameof(BeamEndpointPatrol)}] 两个 BeamEndpoint 都必须分配给 {name}。", this);
            enabled = false;
            return;
        }

        Vector3 delta = upperRightEndpoint.position - lowerLeftEndpoint.position;
        delta.z = 0f;
        if (delta.sqrMagnitude < 0.0001f)
        {
            Debug.LogError($"[{nameof(BeamEndpointPatrol)}] 两个端点不能重合：{name}。", this);
            enabled = false;
            return;
        }

        movingToUpperRight = startAtLowerLeft;
        transform.position = startAtLowerLeft
            ? lowerLeftEndpoint.position
            : upperRightEndpoint.position;
        initialized = true;
    }

    private void Update()
    {
        if (!initialized) return;

        Transform target = movingToUpperRight ? upperRightEndpoint : lowerLeftEndpoint;
        transform.position = Vector3.MoveTowards(
            transform.position,
            target.position,
            speed * Time.deltaTime);

        if ((transform.position - target.position).sqrMagnitude <= 0.000001f)
            movingToUpperRight = !movingToUpperRight;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || lowerLeftEndpoint == null || upperRightEndpoint == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(lowerLeftEndpoint.position, upperRightEndpoint.position);
        Gizmos.DrawWireSphere(lowerLeftEndpoint.position, 0.08f);
        Gizmos.DrawWireSphere(upperRightEndpoint.position, 0.08f);
    }
}

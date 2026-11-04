using System;
using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// One-way sensing gate built from the real OneWayDoor sprite set:
///  - OneWayGate.000-006  : striped shutter that retracts upward (top pivot)
///  - SignDetector        : traffic-light housing
///  - SignLights.000/.001 : red / green lamp panel that sits inside the housing
///  - StaticStructure     : the mount bracket on top of the pole
///  - ScanEffect          : the detection cone
///
/// Behaviour (matches the requested gameplay):
///  - Starts CLOSED with the RED light on and the shutter blocking.
///  - Player enters the detection beam -> instantly turn GREEN and raise the
///    shutter (frames 000 -> 006) so the player can walk through.
///  - Player leaves -> wait <see cref="closeDelay"/> seconds, then lower the
///    shutter (reverse the open frames 006 -> 000) and turn the light RED.
/// </summary>
public class OneWayDoorController : MonoBehaviour
{
    [Header("Shutter (OneWayGate frames)")]
    [SerializeField] private SpriteRenderer gateRenderer;
    [SerializeField] private Sprite[] gateFrames;      // 000 (closed) .. 006 (open)

    [Header("Traffic light")]
    [SerializeField] private SpriteRenderer lightsRenderer;
    [SerializeField] private Sprite redLight;
    [SerializeField] private Sprite greenLight;

    [Header("Detection cone")]
    [Tooltip("红色光锥（关门时显示）")]
    [SerializeField] private SpriteRenderer scanRenderer;
    [Tooltip("绿色光锥（开门时显示）。独立子物体，可单独调整位置/大小/旋转")]
    [SerializeField] private SpriteRenderer scanGreenRenderer;

    [Header("Blocking")]
    [SerializeField] private Collider2D gateCollider;

    [Header("Timing")]
    [Tooltip("Seconds the gate stays open after the player leaves the beam.")]
    [SerializeField] private float closeDelay = 1.0f;
    [SerializeField] private float framesPerSecond = 12f;

    [Header("Detection")]
    [SerializeField] private Vector2 detectionSize = new Vector2(2.4f, 3.2f);
    [SerializeField] private Vector2 detectionOffset = new Vector2(-1.2f, -0.3f);
    [SerializeField] private LayerMask playerLayers = ~0;

    [Header("Detection Gizmo")]
    [Tooltip("Always draw a faint detection box even when the door is not selected.")]
    [SerializeField] private bool showDetectionGizmo = true;
    [Tooltip("Radius (world units) of the draggable Scene-view handles.")]
    [SerializeField] private float handleSize = 0.09f;

    private enum Phase { Closed, Opening, Open, Closing }

    private Phase phase = Phase.Closed;
    private int frameIndex;
    private float idleTimer;
    private Coroutine animRoutine;

    // Runtime flag used only to tint the detection gizmo while playing.
    private bool playerInsideDetection;

    private void Awake()
    {
        if (gateRenderer == null) gateRenderer = FindRenderer("OneWayGate");
        if (lightsRenderer == null) lightsRenderer = FindRenderer("OneWayDoor-SignLights");
        if (scanRenderer == null) scanRenderer = FindRenderer("OneWayDoor-ScanEffect");
        if (gateCollider == null && gateRenderer != null) gateCollider = gateRenderer.GetComponent<Collider2D>();
    }

    private void Start()
    {
        ApplyClosedVisuals();
    }

    private void Update()
    {
        bool detected = DetectPlayer();
        playerInsideDetection = detected;

        switch (phase)
        {
            case Phase.Closed:
                if (detected) BeginOpen();
                break;

            case Phase.Open:
                if (detected)
                {
                    idleTimer = 0f;
                }
                else
                {
                    idleTimer += Time.deltaTime;
                    if (idleTimer >= closeDelay) BeginClose();
                }
                break;

            case Phase.Closing:
                // Player came back before the shutter finished dropping -> re-open.
                if (detected) BeginOpen();
                break;

            case Phase.Opening:
                // Keep rising; once it reaches Open the idle timer starts.
                break;
        }
    }

    private void BeginOpen()
    {
        if (phase == Phase.Opening) return;
        phase = Phase.Opening;
        idleTimer = 0f;
        SetLight(green: true);
        if (gateCollider != null) gateCollider.enabled = false;
        StartAnimateTo(gateFrames.Length - 1, onDone: () => phase = Phase.Open);
    }

    private void BeginClose()
    {
        if (phase == Phase.Closing) return;
        phase = Phase.Closing;
        SetLight(green: false);
        StartAnimateTo(0, onDone: () =>
        {
            phase = Phase.Closed;
            if (gateCollider != null) gateCollider.enabled = true;
        });
    }

    private void ApplyClosedVisuals()
    {
        phase = Phase.Closed;
        frameIndex = 0;
        idleTimer = 0f;
        if (animRoutine != null) { StopCoroutine(animRoutine); animRoutine = null; }

        if (gateRenderer != null && gateFrames != null && gateFrames.Length > 0)
            gateRenderer.sprite = gateFrames[0];
        SetLight(green: false);
        if (gateCollider != null) gateCollider.enabled = true;
    }

    private void StartAnimateTo(int target, Action onDone)
    {
        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(AnimateTo(target, onDone));
    }

    private IEnumerator AnimateTo(int target, Action onDone)
    {
        if (gateRenderer == null || gateFrames == null || gateFrames.Length == 0)
        {
            onDone?.Invoke();
            yield break;
        }

        frameIndex = Mathf.Clamp(frameIndex, 0, gateFrames.Length - 1);
        float interval = 1f / Mathf.Max(1f, framesPerSecond);
        int dir = target >= frameIndex ? 1 : -1;

        while (frameIndex != target)
        {
            yield return new WaitForSeconds(interval);
            frameIndex += dir;
            frameIndex = Mathf.Clamp(frameIndex, 0, gateFrames.Length - 1);
            gateRenderer.sprite = gateFrames[frameIndex];
        }

        animRoutine = null;
        onDone?.Invoke();
    }

    private void SetLight(bool green)
    {
        if (lightsRenderer != null)
        {
            Sprite lamp = green ? greenLight : redLight;
            if (lamp != null) lightsRenderer.sprite = lamp;
        }
        if (scanRenderer != null) scanRenderer.enabled = !green;
        if (scanGreenRenderer != null) scanGreenRenderer.enabled = green;
    }

    // World-space center of the detection box.
    private Vector3 WorldDetectionCenter => transform.TransformPoint(detectionOffset);

    private bool DetectPlayer()
    {
        Vector2 center = WorldDetectionCenter;
        Vector2 size = Vector2.Scale(detectionSize, new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)));
        Collider2D[] results = Physics2D.OverlapBoxAll(center, size, transform.eulerAngles.z, playerLayers);
        for (int i = 0; i < results.Length; i++)
        {
            Collider2D hit = results[i];
            if (hit == null) continue;
            if (hit.transform.IsChildOf(transform)) continue;
            if (hit.GetComponentInParent<BunnyController>() != null) return true;
        }
        return false;
    }

    private SpriteRenderer FindRenderer(string childName)
    {
        Transform t = transform.Find(childName);
        return t != null ? t.GetComponent<SpriteRenderer>() : null;
    }

    // ---------------------------------------------------------------------
    // Gizmos
    // ---------------------------------------------------------------------

    // Faint always-on box so the detection volume is visible without selecting.
    private void OnDrawGizmos()
    {
        if (!showDetectionGizmo) return;

        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        Color c = playerInsideDetection ? new Color(1f, 0.55f, 0.1f, 0.12f) : new Color(0.2f, 0.9f, 1f, 0.10f);
        Gizmos.color = c;
        Gizmos.DrawCube(detectionOffset, detectionSize);

        Gizmos.color = playerInsideDetection ? new Color(1f, 0.55f, 0.1f, 0.55f) : new Color(0.2f, 0.9f, 1f, 0.45f);
        Gizmos.DrawWireCube(detectionOffset, detectionSize);

        Gizmos.matrix = old;
    }

    private void OnDrawGizmosSelected()
    {
        Color outline = playerInsideDetection ? new Color(1f, 0.45f, 0.05f) : Color.cyan;
        Color fill = new Color(outline.r, outline.g, outline.b, 0.16f);

        // --- Filled volume + outline (drawn in the door's local space) ---
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = fill;
        Gizmos.DrawCube(detectionOffset, detectionSize);

        Gizmos.color = outline;
        Gizmos.DrawWireCube(detectionOffset, detectionSize);

        // Center cross-hair
        Vector2 h = detectionSize * 0.5f;
        Vector3 o = detectionOffset;
        Gizmos.DrawLine(o + new Vector3(-h.x, 0f, 0f), o + new Vector3(h.x, 0f, 0f));
        Gizmos.DrawLine(o + new Vector3(0f, -h.y, 0f), o + new Vector3(0f, h.y, 0f));

        Gizmos.matrix = old;

#if UNITY_EDITOR
        DrawSceneHandles(outline);
#endif
    }

#if UNITY_EDITOR
    private void DrawSceneHandles(Color color)
    {
        Vector3 center = WorldDetectionCenter;
        Vector3 halfX = transform.TransformVector(new Vector3(detectionSize.x * 0.5f, 0f, 0f));
        Vector3 halfY = transform.TransformVector(new Vector3(0f, detectionSize.y * 0.5f, 0f));

        // Four corners: [0]=BL [1]=BR [2]=TR [3]=TL
        Vector3[] corners =
        {
            center - halfX - halfY,
            center + halfX - halfY,
            center + halfX + halfY,
            center - halfX + halfY,
        };

        Handles.color = color;

        bool changed = false;
        Vector3[] newCorners = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            newCorners[i] = Handles.FreeMoveHandle(corners[i], handleSize, Vector3.zero, Handles.SphereHandleCap);
            if ((newCorners[i] - corners[i]).sqrMagnitude > 0.0000001f) changed = true;
        }

        // Center move handle (drag the whole box).
        Vector3 newCenter = Handles.FreeMoveHandle(center, handleSize * 1.25f, Vector3.zero, Handles.CubeHandleCap);
        bool centerMoved = (newCenter - center).sqrMagnitude > 0.0000001f;

        if (changed || centerMoved)
        {
            Undo.RecordObject(this, "Adjust Detection Area");

            if (changed)
            {
                // Rebuild box from the (possibly) dragged corners; the opposite
                // corner stays put because it was not moved.
                Vector3 min = newCorners[0];
                Vector3 max = newCorners[0];
                for (int i = 1; i < 4; i++)
                {
                    min = Vector3.Min(min, newCorners[i]);
                    max = Vector3.Max(max, newCorners[i]);
                }

                Vector3 newCenterWorld = (min + max) * 0.5f;
                Vector3 sizeWorld = max - min;
                detectionOffset = transform.InverseTransformPoint(newCenterWorld);
                detectionSize = new Vector2(
                    Mathf.Max(0.1f, Mathf.Abs(sizeWorld.x)),
                    Mathf.Max(0.1f, Mathf.Abs(sizeWorld.y)));
            }
            else
            {
                detectionOffset += (Vector2)transform.InverseTransformVector(newCenter - center);
            }

            EditorUtility.SetDirty(this);
        }

        // Live read-out above the box.
        GUIStyle style = new GUIStyle();
        style.normal.textColor = color;
        style.fontSize = 11;
        style.alignment = TextAnchor.LowerCenter;
        Vector3 labelPos = center + halfY + Vector3.up * (handleSize * 3f);
        Handles.Label(labelPos,
            $"Detection\nSize ({detectionSize.x:0.00}, {detectionSize.y:0.00})\nOffset ({detectionOffset.x:0.00}, {detectionOffset.y:0.00})",
            style);
    }
#endif
}

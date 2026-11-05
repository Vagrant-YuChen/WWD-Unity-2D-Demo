using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a Ferris-wheel style trap: the arms (and pivot anchors) rotate
/// clockwise/counter-clockwise around a center anchor, while the landing
/// platforms only follow the anchors' positions (they never rotate).
/// </summary>
public class FerrisWheelController : MonoBehaviour
{
    [Header("Rotation")]
    [Tooltip("Degrees per second.")]
    public float rotationSpeed = 30f;
    public bool clockwise = true;

    [Header("References (auto-filled if left empty)")]
    public Transform center;          // Ferris-CenterTwo.000
    public Transform[] arms;          // Ferris-Arm.000 (x4)
    public Transform[] pivots;        // Ferris-CenterTwo.000 (1..4)
    public Transform[] platforms;     // CollapsingBlock2x / Ferris-Platform (x4)

    private Vector3 centerInitialLocalPos;
    private Vector3[] armInitialLocalPos;
    private Quaternion[] armInitialLocalRot;
    private Vector3[] pivotInitialLocalPos;
    private Quaternion[] pivotInitialLocalRot;
    private Quaternion[] platformInitialLocalRot;
    private int[] platformToPivotIndex;

    private float angle;

    private void Awake()
    {
        AutoDiscover();
        CacheBaselines();
    }

    private void AutoDiscover()
    {
        if (center == null)
        {
            foreach (Transform child in transform)
            {
                if (child.name == "Ferris-CenterTwo.000")
                {
                    center = child;
                    break;
                }
            }
        }

        if (arms == null || arms.Length == 0)
        {
            var list = new List<Transform>();
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Ferris-Arm.000"))
                    list.Add(child);
            }
            arms = list.ToArray();
        }

        if (pivots == null || pivots.Length == 0)
        {
            var list = new List<Transform>();
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Ferris-CenterTwo.000 ("))
                    list.Add(child);
            }
            // Sort by the number in parentheses so mapping is stable.
            list.Sort((a, b) => ExtractIndex(a.name).CompareTo(ExtractIndex(b.name)));
            pivots = list.ToArray();
        }

        if (platforms == null || platforms.Length == 0)
        {
            string[] wanted =
            {
                "CollapsingBlock2x.000",
                "CollapsingBlock2x.000 (1)",
                "Ferris-Platform.000",
                "Ferris-Platform.000 (1)"
            };
            var list = new List<Transform>();
            foreach (string n in wanted)
            {
                Transform found = transform.Find(n);
                if (found != null) list.Add(found);
            }
            // Fallback: any child not already used that has a collider.
            if (list.Count == 0)
            {
                foreach (Transform child in transform)
                {
                    if (child.GetComponent<Collider2D>() != null &&
                        !child.name.StartsWith("Ferris-Arm") &&
                        child.name != "Ferris-CenterTwo.000" &&
                        !child.name.StartsWith("Ferris-CenterTwo.000 ("))
                    {
                        list.Add(child);
                    }
                }
            }
            platforms = list.ToArray();
        }

        // Make platforms kinematic so moving them carries the player smoothly.
        foreach (Transform p in platforms)
        {
            if (p == null) continue;
            Rigidbody2D rb = p.GetComponent<Rigidbody2D>();
            if (rb == null) rb = p.gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            // 平台自身摩擦置 0：水平跟随改由 BunnyController 叠加平台速度实现，
            // 避免物理摩擦造成额外阻力与抖动。
            Collider2D col = p.GetComponent<Collider2D>();
            if (col != null)
            {
                if (col.sharedMaterial == null)
                {
                    var mat = new PhysicsMaterial2D("FerrisPlatformNoFriction") { friction = 0f, bounciness = 0f };
                    col.sharedMaterial = mat;
                }
                else
                {
                    col.sharedMaterial.friction = 0f;
                }
            }
        }
    }

    private static int ExtractIndex(string name)
    {
        int open = name.LastIndexOf('(');
        int close = name.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            string num = name.Substring(open + 1, close - open - 1).Trim();
            if (int.TryParse(num, out int v)) return v;
        }
        return 0;
    }

    private void CacheBaselines()
    {
        if (center == null) return;

        centerInitialLocalPos = center.localPosition;

        armInitialLocalPos = new Vector3[arms.Length];
        armInitialLocalRot = new Quaternion[arms.Length];
        for (int i = 0; i < arms.Length; i++)
        {
            if (arms[i] == null) continue;
            armInitialLocalPos[i] = arms[i].localPosition;
            armInitialLocalRot[i] = arms[i].localRotation;
        }

        pivotInitialLocalPos = new Vector3[pivots.Length];
        pivotInitialLocalRot = new Quaternion[pivots.Length];
        for (int i = 0; i < pivots.Length; i++)
        {
            if (pivots[i] == null) continue;
            pivotInitialLocalPos[i] = pivots[i].localPosition;
            pivotInitialLocalRot[i] = pivots[i].localRotation;
        }

        platformInitialLocalRot = new Quaternion[platforms.Length];
        for (int i = 0; i < platforms.Length; i++)
        {
            if (platforms[i] == null) continue;
            platformInitialLocalRot[i] = platforms[i].localRotation;
        }
        // Map each platform to the nearest pivot anchor (by initial distance).
        platformToPivotIndex = new int[platforms.Length];
        for (int i = 0; i < platforms.Length; i++)
        {
            platformToPivotIndex[i] = -1;
            if (platforms[i] == null) continue;
            float best = float.MaxValue;
            for (int j = 0; j < pivots.Length; j++)
            {
                if (pivots[j] == null) continue;
                float d = Vector3.SqrMagnitude(platforms[i].localPosition - pivots[j].localPosition);
                if (d < best)
                {
                    best = d;
                    platformToPivotIndex[i] = j;
                }
            }
        }
    }

    private void Update()
    {
        if (center == null) return;

        float signedSpeed = clockwise ? -rotationSpeed : rotationSpeed;
        angle += signedSpeed * Time.deltaTime;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);

        for (int i = 0; i < arms.Length; i++)
        {
            if (arms[i] == null) continue;
            Vector3 offset = armInitialLocalPos[i] - centerInitialLocalPos;
            arms[i].localPosition = centerInitialLocalPos + rot * offset;
            arms[i].localRotation = armInitialLocalRot[i] * Quaternion.Euler(0f, 0f, angle);
        }

        for (int i = 0; i < pivots.Length; i++)
        {
            if (pivots[i] == null) continue;
            Vector3 offset = pivotInitialLocalPos[i] - centerInitialLocalPos;
            pivots[i].localPosition = centerInitialLocalPos + rot * offset;
            pivots[i].localRotation = pivotInitialLocalRot[i] * Quaternion.Euler(0f, 0f, angle);
        }

        for (int i = 0; i < platforms.Length; i++)
        {
            if (platforms[i] == null) continue;
            int idx = platformToPivotIndex[i];
            if (idx >= 0 && idx < pivots.Length && pivots[idx] != null)
            {
                platforms[i].localPosition = pivots[idx].localPosition;
            }
            // Platforms keep their original (upright) rotation.
            platforms[i].localRotation = platformInitialLocalRot[i];

            // 平台只通过位移驱动：把 Kinematic 刚体的 velocity 清零，
            // 让接触解算不会因噪声速度把站在上面的角色向上弹/踢飞。
            // （角色的跟随由 BunnyController 按平台世界位移携带，不依赖该速度。）
            Rigidbody2D prb = platforms[i].GetComponent<Rigidbody2D>();
            if (prb != null)
            {
                prb.velocity = Vector2.zero;
            }
        }

    }
}



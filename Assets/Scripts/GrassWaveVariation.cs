using UnityEngine;

[RequireComponent(typeof(Animator))]
public class GrassWaveVariation : MonoBehaviour
{
    [Tooltip("Normalized offset into the grass loop (0-1).")]
    [Range(0f, 1f)] public float startPhase = 0f;

    [Tooltip("Playback speed multiplier.")]
    public float speed = 1f;

    [Tooltip("Randomize phase and speed at startup instead of using the values above.")]
    public bool randomizePhase = false;

    public float minRandomSpeed = 0.85f;
    public float maxRandomSpeed = 1.15f;

    static readonly int GrassStateHash = Animator.StringToHash("GrassWaving");

    void Start()
    {
        var animator = GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        float phase = startPhase;
        float playSpeed = speed;

        if (randomizePhase)
        {
            phase = Random.value;
            playSpeed = Random.Range(minRandomSpeed, maxRandomSpeed);
        }

        animator.speed = Mathf.Max(0.01f, playSpeed);
        animator.Play(GrassStateHash, 0, Mathf.Repeat(phase, 1f));
    }
}

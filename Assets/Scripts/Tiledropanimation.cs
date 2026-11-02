using System;
using UnityEngine;

// Added automatically to a tile when it is placed. The tile drops in from above and bounces to a stop.
// It removes itself when the animation is finished.
public class TileDropAnimation : MonoBehaviour
{
    // The first bounce lands at this fraction of the animation (see EaseOutBounce).
    private const float FirstImpactTime = 1f / 2.75f;

    private Vector3 targetPosition;
    private float height;
    private float duration;
    private float elapsed;
    private bool impacted;
    private Action onFirstImpact;

    public void Begin(float dropHeight, float dropDuration, Action firstImpactCallback)
    {
        targetPosition = transform.position;
        height = dropHeight;
        duration = Mathf.Max(0.05f, dropDuration);
        onFirstImpact = firstImpactCallback;
        elapsed = 0f;
        impacted = false;

        transform.position = targetPosition + Vector3.up * height;
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        transform.position = targetPosition + Vector3.up * (height * (1f - EaseOutBounce(t)));

        if (!impacted && t >= FirstImpactTime)
        {
            impacted = true;
            onFirstImpact?.Invoke();
        }

        if (t >= 1f)
        {
            transform.position = targetPosition;
            Destroy(this);
        }
    }

    // Standard "ease out bounce" curve: 0 at the start, 1 at the end, with a few shrinking bounces.
    static float EaseOutBounce(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;

        if (t < 1f / d1) return n1 * t * t;
        if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
        if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
        t -= 2.625f / d1;
        return n1 * t * t + 0.984375f;
    }
}
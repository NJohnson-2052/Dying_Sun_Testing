using UnityEngine;

// A tiny camera shake. Call CameraThump.Thump() when something heavy lands.
// It attaches itself to the Main Camera the first time it's used.
public class CameraThump : MonoBehaviour
{
    private Vector3 restPosition;
    private float timeLeft;
    private float duration;
    private float strength;
    private bool shaking;

    public static void Thump(float strength = 0.15f, float duration = 0.18f)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        CameraThump thump = cam.GetComponent<CameraThump>();
        if (thump == null) thump = cam.gameObject.AddComponent<CameraThump>();
        thump.Begin(strength, duration);
    }

    void Begin(float newStrength, float newDuration)
    {
        if (!shaking) restPosition = transform.localPosition; // remember where the camera really is
        strength = newStrength;
        duration = Mathf.Max(0.02f, newDuration);
        timeLeft = duration;
        shaking = true;
    }

    void LateUpdate()
    {
        if (!shaking) return;

        timeLeft -= Time.deltaTime;
        if (timeLeft <= 0f)
        {
            transform.localPosition = restPosition;
            shaking = false;
            return;
        }

        float falloff = timeLeft / duration; // strongest at the start, fading out
        transform.localPosition = restPosition + Random.insideUnitSphere * strength * falloff;
    }
}
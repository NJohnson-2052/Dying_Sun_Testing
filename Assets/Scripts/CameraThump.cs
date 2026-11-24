using UnityEngine;

// A tiny camera shake. Call CameraThump.Thump() when something heavy lands.
// It attaches itself to the Main Camera the first time it's used.
//
// If the camera also has a CameraFollow, the shake is handed over as CurrentOffset and CameraFollow
// adds it on top of its own movement, so the shake and the chase camera never fight each other.
public class CameraThump : MonoBehaviour
{
    private Vector3 restPosition;
    private float timeLeft;
    private float duration;
    private float strength;
    private bool shaking;
    private bool followedByCamera; // true when a CameraFollow is moving this camera

    // How far the shake is pushing the camera right now.
    public Vector3 CurrentOffset { get; private set; }

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
        followedByCamera = GetComponent<CameraFollow>() != null;
        if (!shaking && !followedByCamera) restPosition = transform.localPosition; // remember where the camera really is
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
            CurrentOffset = Vector3.zero;
            if (!followedByCamera) transform.localPosition = restPosition;
            shaking = false;
            return;
        }

        float falloff = timeLeft / duration; // strongest at the start, fading out
        CurrentOffset = Random.insideUnitSphere * strength * falloff;
        if (!followedByCamera) transform.localPosition = restPosition + CurrentOffset;
    }
}
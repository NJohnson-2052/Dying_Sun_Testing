using UnityEngine;

// A soft red glow that slowly pulses, with a quiet low hum. Created when the player reaches the goal.
// Everything is generated in code: no light or audio assets needed.
public class GoalGlow : MonoBehaviour
{
    // Tweak these to taste.
    private readonly Color color = new Color(1f, 0.15f, 0.1f);
    private const float MinIntensity = 1.5f;
    private const float MaxIntensity = 4f;
    private const float PulsesPerSecond = 0.5f;  // slow and calm
    private const float FadeInSeconds = 1.5f;
    private const float HumVolume = 0.12f;       // 0 = silent, 1 = full volume

    private Light glow;
    private AudioSource hum;
    private float age;

    public static GoalGlow Create(Vector3 position, float range)
    {
        GameObject go = new GameObject("GoalGlow");
        go.transform.position = position;
        GoalGlow g = go.AddComponent<GoalGlow>(); // Awake runs right away
        g.glow.range = range;
        return g;
    }

    void Awake()
    {
        glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = 8f;
        glow.intensity = 0f;
        glow.shadows = LightShadows.None;

        hum = gameObject.AddComponent<AudioSource>();
        hum.clip = BuildHumClip();
        hum.loop = true;
        hum.volume = 0f;
        hum.spatialBlend = 0f; // plain 2D sound, same volume wherever the camera is
        hum.Play();
    }

    void Update()
    {
        age += Time.deltaTime;
        float fade = Mathf.Clamp01(age / FadeInSeconds);
        float pulse = 0.5f + 0.5f * Mathf.Sin(age * PulsesPerSecond * 2f * Mathf.PI); // 0..1

        glow.intensity = Mathf.Lerp(MinIntensity, MaxIntensity, pulse) * fade;
        hum.volume = HumVolume * fade * (0.7f + 0.3f * pulse); // the hum swells a little with the glow
    }

    // A low note (110 Hz) with two soft overtones. Each frequency fits a whole number of cycles
    // into the 2 second clip, so it loops without a click.
    static AudioClip BuildHumClip()
    {
        const int sampleRate = 44100;
        const float seconds = 2f;
        int samples = (int)(sampleRate * seconds);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)sampleRate;
            data[i] = Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.60f
                    + Mathf.Sin(2f * Mathf.PI * 165f * t) * 0.15f
                    + Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.25f;
        }

        AudioClip clip = AudioClip.Create("GoalHum", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
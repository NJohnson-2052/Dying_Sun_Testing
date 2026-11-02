using UnityEngine;

// A little burst of colored sparks that pop upward, fall a bit, and shrink away.
// Spawned when the player collects a resource.
public class PickupBurst : MonoBehaviour
{
    private const float Lifetime = 0.8f;
    private const float SparkSize = 0.14f;
    private const float Gravity = 3f;

    private Transform[] parts;
    private Vector3[] velocities;
    private float age;

    public static void Spawn(Vector3 position, Color color, int count = 10)
    {
        GameObject go = new GameObject("PickupBurst");
        go.transform.position = position;
        go.AddComponent<PickupBurst>().Build(color, count);
    }

    void Build(Color color, int count)
    {
        parts = new Transform[count];
        velocities = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(spark.GetComponent<Collider>());
            spark.transform.SetParent(transform, false);
            spark.transform.localScale = Vector3.one * SparkSize;

            Renderer rend = spark.GetComponent<Renderer>();
            rend.material.color = color;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Vector2 sideways = Random.insideUnitCircle * 0.8f;
            parts[i] = spark.transform;
            velocities[i] = new Vector3(sideways.x, Random.Range(1.5f, 2.6f), sideways.y);
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = age / Lifetime;
        if (t >= 1f) { Destroy(gameObject); return; }

        for (int i = 0; i < parts.Length; i++)
        {
            velocities[i] += Vector3.down * Gravity * Time.deltaTime;
            parts[i].localPosition += velocities[i] * Time.deltaTime;
            parts[i].localScale = Vector3.one * SparkSize * (1f - t);
        }
    }
}
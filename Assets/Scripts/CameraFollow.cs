using UnityEngine;

// Chase camera. While the player walks, the camera swings in behind the pill and follows it.
// When the walk ends it waits a moment, then glides back to where the camera started (the overview),
// so you can place tiles from the usual top-down view. Turn Return To Overview off to keep the
// camera where the walk ended.
//
// Put this on the Main Camera. Path Game > Add Energy And Chase Camera To Scene does that for you.
public class CameraFollow : MonoBehaviour
{
    [SerializeField] private PlayerController player;

    [Header("Chase view")]
    [Tooltip("How far behind the player the camera sits.")]
    [SerializeField] private float distanceBehind = 5f;

    [Tooltip("How high above the player the camera sits.")]
    [SerializeField] private float height = 4f;

    [Tooltip("The camera aims this far above the player's feet-level center.")]
    [SerializeField] private float lookAtHeight = 0.5f;

    [Tooltip("Bigger = a lazier, smoother camera. Smaller = sticks to the player.")]
    [SerializeField] private float followSmoothTime = 0.35f;

    [Tooltip("How quickly the camera swings around when the player turns a corner.")]
    [SerializeField] private float turnSpeed = 3f;

    [Header("After the walk")]
    [Tooltip("On: the camera glides back to the overview when the walk ends. Off: it stays where it is.")]
    [SerializeField] private bool returnToOverview = true;

    [Tooltip("Seconds to wait after the walk ends before going back.")]
    [SerializeField] private float returnDelay = 0.8f;

    private enum Mode { Overview, Chase, Hold }
    private Mode mode = Mode.Overview;

    private Vector3 overviewPosition;
    private Quaternion overviewRotation;

    // Where the camera really is, without the shake from CameraThump added on top.
    private Vector3 basePosition;
    private Quaternion baseRotation;
    private Vector3 velocity;

    private float headingYaw;        // which way the camera thinks the player is facing, in degrees
    private bool needsHeading;       // set at the start of a walk so the first step snaps the heading
    private Vector3 lastPlayerPosition;
    private float idleTime;
    private CameraThump thump;

    void Awake()
    {
        if (player == null) player = FindObjectOfType<PlayerController>();
        CaptureOverview();
    }

    void Start()
    {
        if (player != null)
        {
            lastPlayerPosition = player.transform.position;
            headingYaw = player.transform.eulerAngles.y;
        }
    }

    // Remembers the camera's current pose as the overview. Call this if you move the camera yourself.
    public void CaptureOverview()
    {
        overviewPosition = transform.position;
        overviewRotation = transform.rotation;
        basePosition = overviewPosition;
        baseRotation = overviewRotation;
    }

    void LateUpdate()
    {
        if (player == null) return;

        float dt = Time.deltaTime;
        Vector3 playerPosition = player.transform.position;

        // Work out which way the player is travelling from how far it moved this frame.
        Vector3 moved = playerPosition - lastPlayerPosition;
        moved.y = 0f;
        lastPlayerPosition = playerPosition;

        if (player.IsMoving && moved.sqrMagnitude > 0.000001f)
        {
            float targetYaw = Mathf.Atan2(moved.x, moved.z) * Mathf.Rad2Deg;
            if (needsHeading)
            {
                headingYaw = targetYaw;
                needsHeading = false;
            }
            else
            {
                headingYaw = Mathf.LerpAngle(headingYaw, targetYaw, 1f - Mathf.Exp(-turnSpeed * dt));
            }
        }

        UpdateMode(dt);

        // Where the camera wants to be.
        Vector3 targetPosition;
        Quaternion targetRotation;
        switch (mode)
        {
            case Mode.Chase:
            {
                Vector3 heading = Quaternion.Euler(0f, headingYaw, 0f) * Vector3.forward;
                targetPosition = playerPosition - heading * distanceBehind + Vector3.up * height;

                // Aim from where the camera really is, so it always keeps the player in view while catching up.
                Vector3 look = playerPosition + Vector3.up * lookAtHeight - basePosition;
                targetRotation = look.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(look) : baseRotation;
                break;
            }
            case Mode.Overview:
                targetPosition = overviewPosition;
                targetRotation = overviewRotation;
                break;
            default: // Hold
                targetPosition = basePosition;
                targetRotation = baseRotation;
                break;
        }

        basePosition = Vector3.SmoothDamp(basePosition, targetPosition, ref velocity, Mathf.Max(0.01f, followSmoothTime));
        baseRotation = Quaternion.Slerp(baseRotation, targetRotation, 1f - Mathf.Exp(-6f * dt));

        // CameraThump hands us its shake as an offset, so a thump never fights the camera movement.
        if (thump == null) thump = GetComponent<CameraThump>();
        Vector3 shake = thump != null ? thump.CurrentOffset : Vector3.zero;

        transform.SetPositionAndRotation(basePosition + shake, baseRotation);
    }

    void UpdateMode(float dt)
    {
        if (player.IsMoving)
        {
            if (mode != Mode.Chase) needsHeading = true;
            mode = Mode.Chase;
            idleTime = 0f;
        }
        else if (mode == Mode.Chase)
        {
            idleTime += dt;
            if (idleTime >= returnDelay) mode = returnToOverview ? Mode.Overview : Mode.Hold;
        }
    }
}
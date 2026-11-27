using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Sky Museum/Tour Camera")]
public sealed class SkyMuseumTour : MonoBehaviour
{
    [Header("Camera and five optional viewpoints (5 = lever)")]
    public Camera tourCamera;
    public Transform[] viewpoints = new Transform[5];

    [Header("Free flight")]
    [Min(0.000001f)] public float worldUnitScale = 1f;
    [Min(0.1f)] public float moveSpeed = 6f;
    [Min(1f)] public float boostMultiplier = 3f;
    [Range(0.1f, 10f)] public float lookSensitivity = 2f;
    public bool showOverlay = true;

    private readonly Transform[] resolvedViews = new Transform[5];
    private readonly Vector3[] fallbackPositions = new Vector3[5];
    private readonly Quaternion[] fallbackRotations = new Quaternion[5];
    private Transform cameraTransform;
    private bool ready;
    private bool flying;
    private float yaw;
    private float pitch;
    private int selectedView;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private GUIStyle overlayStyle;
    private GUIStyle titleStyle;

    private void Start()
    {
        if (tourCamera == null)
            tourCamera = GetComponent<Camera>();
        if (tourCamera == null)
            tourCamera = Camera.main;

        if (tourCamera == null)
        {
            Debug.LogWarning("SkyMuseumTour：请挂到浏览相机，或提供一个 MainCamera。", this);
            enabled = false;
            return;
        }

        // 只移动独立浏览相机；拒绝把静态对象或带几何子物体的相机当作飞行节点。
        if (tourCamera.gameObject.isStatic || tourCamera.GetComponentsInChildren<Renderer>(true).Length > 0)
        {
            Debug.LogWarning("SkyMuseumTour：浏览相机必须为非静态对象，且子级不能包含场景几何。原对象未被移动。", this);
            enabled = false;
            return;
        }

        cameraTransform = tourCamera.transform;
        for (int i = 0; i < 5; i++)
        {
            if (viewpoints != null && i < viewpoints.Length)
                resolvedViews[i] = viewpoints[i];
            if (resolvedViews[i] == null)
            {
                GameObject namedView = GameObject.Find("SkyMuseum_View_" + (i + 1));
                if (namedView != null)
                    resolvedViews[i] = namedView.transform;
            }
            // 防止把相机自身误当成重置基准。
            if (resolvedViews[i] == cameraTransform ||
                (resolvedViews[i] != null && resolvedViews[i].IsChildOf(cameraTransform)))
                resolvedViews[i] = null;
        }

        BuildFallbackViews();
        ready = true;
        GoToViewpoint(0);

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        Debug.LogWarning("SkyMuseumTour 使用 UnityEngine.Input；请将 Active Input Handling 设为 Input Manager (Old) 或 Both。", this);
#endif
    }

    private void BuildFallbackViews()
    {
        Vector3 origin = resolvedViews[0] != null ? resolvedViews[0].position : cameraTransform.position;
        Quaternion rotation = resolvedViews[0] != null ? resolvedViews[0].rotation : cameraTransform.rotation;
        fallbackPositions[0] = origin;
        fallbackRotations[0] = rotation;
        float distance = Mathf.Max(6f, moveSpeed * 3f) * worldUnitScale;
        Vector3 focus = origin + rotation * Vector3.forward * distance;
        float[] angles = { 0f, -35f, 35f, 145f, 0f };
        for (int i = 1; i < 5; i++)
        {
            Vector3 offset = Quaternion.AngleAxis(angles[i], Vector3.up) * (origin - focus);
            Vector3 position = focus + offset + Vector3.up * (i == 3 ? distance * 0.15f : 0f);
            fallbackPositions[i] = position;
            fallbackRotations[i] = Quaternion.LookRotation(focus - position, Vector3.up);
        }
    }

    // 索引为 0..4；第5视角检查可动拉杆，不移动原场景物体。
    public void GoToViewpoint(int index)
    {
        if (!ready || index < 0 || index > 4)
            return;

        EndFlight();
        Transform preset = resolvedViews[index];
        Vector3 position = preset != null ? preset.position : fallbackPositions[index];
        Quaternion rotation = preset != null ? preset.rotation : fallbackRotations[index];
        cameraTransform.SetPositionAndRotation(position, rotation);
        tourCamera.fieldOfView = index == 1 ? 70f : index == 4 ? 40f : 43f;
        tourCamera.nearClipPlane = (index == 4 ? .0005f : .003f) * worldUnitScale;
        Vector3 angles = cameraTransform.eulerAngles;
        yaw = angles.y;
        pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x), -89f, 89f);
        selectedView = index;
    }

    private void Update()
    {
        if (!ready || cameraTransform == null)
            return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        return;
#else
        if (Input.GetKeyDown(KeyCode.H))
            showOverlay = !showOverlay;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            GoToViewpoint(0);
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            GoToViewpoint(1);
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            GoToViewpoint(2);
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            GoToViewpoint(3);
        else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5))
            GoToViewpoint(4);
        else if (Input.GetKeyDown(KeyCode.F))
            GoToViewpoint(0);

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            EndFlight();
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            flying = true;
            // 锁定鼠标的第一帧不采样，避免切换焦点导致视角跳变。
            return;
        }

        if (!Input.GetMouseButton(1))
            EndFlight();
        if (!flying)
            return;

        float mouseX = Input.GetAxisRaw("Mouse X");
        float mouseY = Input.GetAxisRaw("Mouse Y");
        if (Mathf.Abs(mouseX) + Mathf.Abs(mouseY) > 0.0001f)
        {
            yaw = Mathf.Repeat(yaw + mouseX * lookSensitivity, 360f);
            pitch = Mathf.Clamp(pitch - mouseY * lookSensitivity, -89f, 89f);
            cameraTransform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            selectedView = -1;
        }

        // 滚轮根据当前速度成比例调速，适合模型单位不同的场景。
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.001f)
            moveSpeed = Mathf.Clamp(moveSpeed * Mathf.Pow(1.25f, wheel), 0.25f, 120f);

        float horizontal = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float forward = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        float vertical = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        Vector3 movement = cameraTransform.right * horizontal + cameraTransform.forward * forward + Vector3.up * vertical;
        if (movement.sqrMagnitude > 0.0001f)
        {
            movement = Vector3.ClampMagnitude(movement, 1f);
            bool boosted = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float speed = Mathf.Max(0.1f, moveSpeed) * worldUnitScale * (boosted ? Mathf.Max(1f, boostMultiplier) : 1f);
            cameraTransform.position += movement * speed * Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            selectedView = -1;
        }
#endif
    }

    private void EndFlight()
    {
        if (!flying)
            return;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        flying = false;
    }

    private void OnDisable()
    {
        EndFlight();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            EndFlight();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            EndFlight();
    }

    private void OnGUI()
    {
        if (!ready || !showOverlay)
            return;
        if (overlayStyle == null)
        {
            overlayStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            overlayStyle.normal.textColor = new Color(0.87f, 0.95f, 0.97f, 1f);
            titleStyle = new GUIStyle(overlayStyle) { fontSize = 12, fontStyle = FontStyle.Bold };
        }

        float width = Mathf.Min(370f, Screen.width - 24f);
        if (width < 180f || Screen.height < 110)
            return;
        Rect panel = new Rect(12f, 12f, width, 78f);
        Color previousColor = GUI.color;
        GUI.color = new Color(0.08f, 0.17f, 0.23f, 0.78f);
        GUI.Box(panel, GUIContent.none);
        GUI.color = Color.white;
        string viewLabel = selectedView >= 0 ? "VIEW " + (selectedView + 1).ToString("00") : "FREE FLIGHT";
        GUI.Label(new Rect(24f, 19f, width - 24f, 20f), "SKY MUSEUM  /  " + viewLabel, titleStyle);
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        string controls = "Enable Input Manager (Old) or Both in Player Settings.";
#else
        string controls = "1-4 Views | 5 Lever | Click / E / Up-Down: Lever\nHold RMB + WASD/QE Fly | F Reset | H Hide";
#endif
        GUI.Label(new Rect(24f, 42f, width - 24f, 42f), controls, overlayStyle);
        GUI.color = previousColor;
    }
}

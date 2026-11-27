using UnityEngine;
using UnityEngine.Events;

[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Sky Museum/Lift Lever")]
public sealed class SkyMuseumLiftLever : MonoBehaviour
{
    [Header("转轴与可编辑端点（相对转轴父节点）")]
    public Transform leverPivot;
    public Vector3 upLocalPosition = new Vector3(0f, 0f, -0.0037445f);
    public Vector3 downLocalPosition = new Vector3(0f, 0f, -0.0037445f);
    public Vector3 upLocalEuler = new Vector3(-32f, 0f, 0f);
    public Vector3 downLocalEuler = new Vector3(32f, 0f, 0f);
    [Min(0.01f)] public float transitionSeconds = 0.5f;
    [SerializeField, Range(0f, 1f)] private float normalizedPosition = 1f;

    [Header("原指示灯：只覆写指定材质槽，不实例化共享材质")]
    public Renderer indicatorRenderer;
    [Min(0)] public int indicatorMaterialIndex = 6;
    public Color upColor = new Color(0.24f, 1f, 0.045f, 1f);
    public Color downColor = new Color(1f, 0.025f, 0.012f, 1f);
    [Min(0f)] public float emissionStrength = 0.8f;

    [Header("近距离操作：鼠标点击、E、上/下方向键")]
    public bool allowPlayerInput = true;
    public Camera interactionCamera;
    [Min(0.001f)] public float interactionDistance = 0.25f;

    [Header("原生动画预览资产；运行时由脚本平滑插值")]
    public AnimationClip pullUpClip;
    public AnimationClip pullDownClip;

    [Header("接口事件：不直接移动升降台")]
    public UnityEvent<float> onPositionChanged = new UnityEvent<float>();
    public UnityEvent<bool> onStateChanged = new UnityEvent<bool>();
    public UnityEvent onPulledUp = new UnityEvent();
    public UnityEvent onPulledDown = new UnityEvent();

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock indicatorProperties;
    private float appliedPosition = float.NaN;
    private float startPosition;
    private float targetPosition = 1f;
    private float elapsed;
    private bool moving;

    public float NormalizedPosition => normalizedPosition;
    public bool IsMoving => moving;
    public bool IsUp => normalizedPosition >= 0.5f;
    public Vector3 UpLocalPosition => upLocalPosition;
    public Vector3 DownLocalPosition => downLocalPosition;
    public Vector3 UpLocalEuler => upLocalEuler;
    public Vector3 DownLocalEuler => downLocalEuler;

    private void OnEnable()
    {
        normalizedPosition = Mathf.Clamp01(normalizedPosition);
        targetPosition = normalizedPosition;
        moving = false;
        ApplyPose();
    }

    private void OnDisable()
    {
        moving = false;
    }

    private void OnValidate()
    {
        normalizedPosition = Mathf.Clamp01(normalizedPosition);
        transitionSeconds = Mathf.Max(0.01f, transitionSeconds);
        interactionDistance = Mathf.Max(0.001f, interactionDistance);
        indicatorMaterialIndex = Mathf.Max(0, indicatorMaterialIndex);
        appliedPosition = float.NaN;
    }

    private void Update()
    {
        if (!Application.IsPlaying(gameObject))
        {
            if (float.IsNaN(appliedPosition) || !Mathf.Approximately(appliedPosition, normalizedPosition))
                ApplyPose();
            return;
        }

        if (moving)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, transitionSeconds));
            float eased = t * t * (3f - 2f * t);
            SetPosition(Mathf.Lerp(startPosition, targetPosition, eased), true);
            if (t >= 1f)
            {
                moving = false;
                InvokeEndpoint();
            }
        }
        else if (float.IsNaN(appliedPosition) || !Mathf.Approximately(appliedPosition, normalizedPosition))
        {
            ApplyPose();
        }

        HandleInput();
    }

    [ContextMenu("拉到上方（绿灯）")]
    public void PullUp()
    {
        BeginPull(1f);
    }

    [ContextMenu("拉到下方（红灯）")]
    public void PullDown()
    {
        BeginPull(0f);
    }

    [ContextMenu("切换上下位置")]
    public void Toggle()
    {
        BeginPull((moving ? targetPosition : normalizedPosition) >= 0.5f ? 0f : 1f);
    }

    public void SetNormalizedImmediate(float value)
    {
        moving = false;
        targetPosition = Mathf.Clamp01(value);
        SetPosition(targetPosition, Application.IsPlaying(gameObject));
        if (Application.IsPlaying(gameObject))
            InvokeEndpoint();
    }

    // 无参数版本用于重现序列化状态；有参数版本适合时间轴或外部升降台逻辑。
    public void ApplyPose()
    {
        normalizedPosition = Mathf.Clamp01(normalizedPosition);
        if (leverPivot != null)
        {
            leverPivot.localPosition = Vector3.Lerp(downLocalPosition, upLocalPosition, normalizedPosition);
            leverPivot.localRotation = Quaternion.Slerp(
                Quaternion.Euler(downLocalEuler), Quaternion.Euler(upLocalEuler), normalizedPosition);
        }
        ApplyIndicator();
        appliedPosition = normalizedPosition;
    }

    public void ApplyPose(float value)
    {
        SetNormalizedImmediate(value);
    }

    private void BeginPull(float target)
    {
        if (!Application.IsPlaying(gameObject))
        {
            SetNormalizedImmediate(target);
            return;
        }
        if ((moving && Mathf.Approximately(targetPosition, target)) ||
            (!moving && Mathf.Approximately(normalizedPosition, target)))
            return;
        startPosition = normalizedPosition;
        targetPosition = target;
        elapsed = 0f;
        moving = true;
    }

    private void SetPosition(float value, bool notify)
    {
        float previous = normalizedPosition;
        bool wasUp = IsUp;
        normalizedPosition = Mathf.Clamp01(value);
        ApplyPose();
        if (!notify || Mathf.Approximately(previous, normalizedPosition))
            return;
        onPositionChanged.Invoke(normalizedPosition);
        if (wasUp != IsUp)
            onStateChanged.Invoke(IsUp);
    }

    private void InvokeEndpoint()
    {
        if (Mathf.Approximately(normalizedPosition, 1f))
            onPulledUp.Invoke();
        else if (Mathf.Approximately(normalizedPosition, 0f))
            onPulledDown.Invoke();
    }

    private void ApplyIndicator()
    {
        if (indicatorRenderer == null)
            return;
        if (indicatorProperties == null)
            indicatorProperties = new MaterialPropertyBlock();
        indicatorRenderer.GetPropertyBlock(indicatorProperties, indicatorMaterialIndex);
        Color color = IsUp ? upColor : downColor;
        indicatorProperties.SetColor(BaseColorId, color);
        indicatorProperties.SetColor(EmissionColorId, color.linear * emissionStrength);
        indicatorRenderer.SetPropertyBlock(indicatorProperties, indicatorMaterialIndex);
    }

    private void OnDidApplyAnimationProperties()
    {
        ApplyPose();
    }

    private void HandleInput()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        return;
#else
        if (!allowPlayerInput || Input.GetMouseButton(1))
            return;
        Camera camera = interactionCamera != null ? interactionCamera : Camera.main;
        if (camera == null || (camera.transform.position - transform.position).sqrMagnitude >
            interactionDistance * interactionDistance)
            return;

        if (Input.GetKeyDown(KeyCode.UpArrow))
            PullUp();
        else if (Input.GetKeyDown(KeyCode.DownArrow))
            PullDown();
        else if (Input.GetKeyDown(KeyCode.E))
            Toggle();
        else if (Input.GetMouseButtonDown(0))
        {
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, interactionDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<SkyMuseumLiftLever>() == this)
                Toggle();
        }
#endif
    }
}

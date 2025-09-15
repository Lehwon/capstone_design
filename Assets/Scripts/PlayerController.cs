using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    Rigidbody rb;
    Collider col;

    [Header("Rotate")]
    public float mouseSpeed;
    public float moveSpeed; // 최대 지상 속도
    float yRotation;
    float xRotation;
    float h;
    float v;

    [Header("Ground Move")]
    public float groundAcceleration = 25f; // 지상 가속도(유닛/초^2) - 빠르게 최고속도 도달
    public float groundDeceleration = 35f; // 지상 감속도(유닛/초^2) - 빠르게 멈추되 약간의 관성
    public float groundDrag = 0f;          // 지상 드래그(선택, 0이면 사용 안 함)

    [Header("Jump")]
    public float jumpStrength = 5f;
    [SerializeField] float groundCheckDistance = 0.15f;
    [SerializeField] LayerMask groundMask = 0; // Inspector에서 Ground만 포함
    bool isGrounded;
    bool jumpQueued;

    [Header("Air Control")]
    [Range(0f, 1f)] public float airControl = 0.4f;     // 공중에서 목표 속도 비율(지상 속도의 몇 %까지 영향을 줄지)
    public float airAcceleration = 10f;                 // 공중 가속도(유닛/초^2)
    public float airDrag = 0f;                          // 공중 마찰(0이면 완전한 관성 유지)

    [SerializeField] private Camera cam;

    // Input System
    InputAction lookAction;
    InputAction moveAction;
    InputAction jumpAction;

    void Awake()
    {
        lookAction = new InputAction(type: InputActionType.Value, binding: "<Mouse>/delta");

        moveAction = new InputAction(type: InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        jumpAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/space");
    }

    void OnEnable()
    {
        lookAction.Enable();
        moveAction.Enable();
        jumpAction.Enable();
    }

    void OnDisable()
    {
        lookAction.Disable();
        moveAction.Disable();
        jumpAction.Disable();
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("[PlayerController] Rigidbody is missing.");
            enabled = false;
            return;
        }

        col = GetComponent<Collider>();
        rb.freezeRotation = true;

        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) cam = GetComponentInChildren<Camera>();
        }
        if (cam == null)
            Debug.LogError("[PlayerController] Camera reference is missing.");

        yRotation = transform.eulerAngles.y;
    }

    void Update()
    {
        RotateView();
        ReadMoveInput();
        ReadJumpInput();
    }

    void FixedUpdate()
    {
        UpdateGrounded();
        ApplyRotationPhysics();
        ApplyMovementPhysics();
    }

    void RotateView()
    {
        Vector2 look = lookAction.ReadValue<Vector2>() * mouseSpeed * Time.deltaTime;
        float mouseX = look.x;
        float mouseY = look.y;

        yRotation += mouseX;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        if (cam != null)
            cam.transform.rotation = Quaternion.Euler(xRotation, yRotation, 0f);
    }

    void ReadMoveInput()
    {
        Vector2 move = moveAction.ReadValue<Vector2>();
        h = move.x;
        v = move.y;
    }

    void ReadJumpInput()
    {
        if (jumpAction.WasPressedThisFrame())
            jumpQueued = true;
    }

    void ApplyRotationPhysics()
    {
        Quaternion targetYaw = Quaternion.Euler(0f, yRotation, 0f);
        rb.MoveRotation(targetYaw);
    }

    void ApplyMovementPhysics()
    {
        Vector3 inputDir = transform.forward * v + transform.right * h;
        Vector3 desiredDir = inputDir.sqrMagnitude > 0f ? inputDir.normalized : Vector3.zero;

        Vector3 velocity = rb.linearVelocity;
        Vector3 horizVel = new Vector3(velocity.x, 0f, velocity.z);

        if (isGrounded)
        {
            if (desiredDir != Vector3.zero)
            {
                // 지상 가속: 현재 수평 속도를 목표 속도(moveSpeed)로 가속
                Vector3 target = desiredDir * moveSpeed;
                float maxChange = groundAcceleration * Time.fixedDeltaTime;
                horizVel = Vector3.MoveTowards(horizVel, target, maxChange);
            }
            else
            {
                // 지상 감속: 입력이 없으면 0으로 빠르게 감속(약간의 관성 유지)
                float maxReduce = groundDeceleration * Time.fixedDeltaTime;
                horizVel = Vector3.MoveTowards(horizVel, Vector3.zero, maxReduce);
            }

            // 선택적 지상 드래그
            if (groundDrag > 0f)
            {
                float dragFactor = Mathf.Clamp01(groundDrag * Time.fixedDeltaTime);
                horizVel *= (1f - dragFactor);
            }
        }
        else
        {
            // 공중: 관성 유지 + 약한 에어 컨트롤
            if (desiredDir != Vector3.zero)
            {
                Vector3 target = desiredDir * (moveSpeed * airControl);
                float maxChange = airAcceleration * Time.fixedDeltaTime;
                horizVel = Vector3.MoveTowards(horizVel, target, maxChange);
            }

            if (airDrag > 0f)
            {
                float dragFactor = Mathf.Clamp01(airDrag * Time.fixedDeltaTime);
                horizVel *= (1f - dragFactor);
            }
        }

        // 최대 수평 속도 제한(지상/공중 공통)
        float maxHoriz = moveSpeed;
        if (horizVel.sqrMagnitude > maxHoriz * maxHoriz)
            horizVel = horizVel.normalized * maxHoriz;

        velocity.x = horizVel.x;
        velocity.z = horizVel.z;

        // 점프(수평 관성 유지)
        if (jumpQueued && isGrounded)
        {
            jumpQueued = false;
            isGrounded = false;
            velocity.y = 0f; // 낙하 관성 제거
            rb.linearVelocity = velocity;
            rb.AddForce(Vector3.up * jumpStrength, ForceMode.VelocityChange);
            return;
        }

        rb.linearVelocity = velocity;
    }

    void UpdateGrounded()
    {
        if (col != null)
        {
            Vector3 origin = col.bounds.center;
            float distance = col.bounds.extents.y + groundCheckDistance;
            isGrounded = Physics.Raycast(origin, Vector3.down, distance, groundMask, QueryTriggerInteraction.Ignore);
        }
        else
        {
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            isGrounded = Physics.Raycast(origin, Vector3.down, 0.1f + groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);
        }
    }
}

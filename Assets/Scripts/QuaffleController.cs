using UnityEngine;

public class Quaffle : MonoBehaviour
{
    [Header("Settings")]
    public float throwForce = 15f;
    public float hoverHeight = 1f;
    public LayerMask groundLayer;

    [Header("State")]
    public bool isHeld = false;
    public Transform holder = null;

    private Rigidbody rb;
    private Vector3 startPos;
    private Quaternion startRot;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 1f;
        rb.angularDamping = 2f;
        startPos = transform.position;
        startRot = transform.rotation;
    }

    void Update()
    {
        if (isHeld && holder != null)
        {
            // Держим перед игроком, плавно следуем за ним
            Vector3 offset = holder.forward * 1.2f + holder.up * 0.5f;
            transform.position = Vector3.Lerp(transform.position, holder.position + offset, Time.deltaTime * 15f);
            transform.rotation = Quaternion.Lerp(transform.rotation, holder.rotation, Time.deltaTime * 15f);
        }
        else
        {
            HoverAboveGround();
        }
    }

    private void HoverAboveGround()
    {
        // Если мяч просто валяется, он должен висеть над землей
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, hoverHeight + 2f, groundLayer))
        {
            float targetY = hit.point.y + hoverHeight;
            float error = targetY - transform.position.y;
            // Пружиним вверх
            rb.linearVelocity += Vector3.up * error * 5f * Time.deltaTime;
        }
    }

    public void Pickup(Transform newHolder)
    {
        if (isHeld) return; // Уже у кого-то в руках

        isHeld = true;
        holder = newHolder;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true; // Отключаем физику, чтобы не толкал игрока
    }

    public void Throw(Vector3 direction)
    {
        isHeld = false;
        holder = null;
        rb.isKinematic = false;
        rb.useGravity = true;

        // Добавляем импульс броска
        rb.AddForce(direction * throwForce, ForceMode.Impulse);
    }

    public void Respawn()
    {
        isHeld = false;
        holder = null;
        rb.isKinematic = false;
        rb.useGravity = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(startPos, startRot);
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log("Мяча коснулись");
        if (isHeld) return;
        Debug.Log("Мяча коснулись");
        // Проверяем, это Игрок (BroomController)?
        BroomController broom = other.GetComponentInParent<BroomController>();
        if (broom != null)
        {
            Debug.Log("Подобрал");
            Pickup(broom.transform);
            return;
        }

        // Проверяем, это Бот (AIPlayer)?
        AIPlayer ai = other.GetComponentInParent<AIPlayer>();
        if (ai != null && !ai.hasBall)  // Дополнительно проверяем, что бот не держит мяч
        {
            Pickup(ai.transform);
            ai.SetHasBall(true, this);  // Передаем ссылку на себя, чтобы бот знал, ЧТО он держит
        }
    }

}

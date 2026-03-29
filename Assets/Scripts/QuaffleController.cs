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

    internal Rigidbody rb;
    private Vector3 startPos;
    private Quaternion startRot;
    private float canBePickedUpTime = 0f;
    public Collider col;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 1f;
        rb.angularDamping = 2f;
        startPos = transform.position;
        startRot = transform.rotation;
        
        // Регистрируем мяч в менеджере
        GameObjectManager.Instance.RegisterQuaffle(this);
    }

    void OnDestroy()
    {
        // Удаляем мяч из менеджера при уничтожении
        if (GameObjectManager.Instance != null)
        {
            GameObjectManager.Instance.UnregisterQuaffle(this);
        }
    }

    void Update()
    {
        if (isHeld && holder != null)
        {
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
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, hoverHeight + 2f, groundLayer))
        {
            float targetY = hit.point.y + hoverHeight;
            float error = targetY - transform.position.y;
            rb.linearVelocity += Vector3.up * error * 5f * Time.deltaTime;
        }
    }

    public void TryPickup(Transform newHolder)
    {
        if (Time.time < canBePickedUpTime) return;

        IPlayerController newPlayer = newHolder.GetComponentInParent<IPlayerController>();
        bool newIsAI = newHolder.GetComponentInParent<AIPlayer>() != null;

        if (isHeld && holder != null)
        {
            IPlayerController currentPlayer = holder.GetComponentInParent<IPlayerController>();
            bool currentIsAI = holder.GetComponentInParent<AIPlayer>() != null;

            // Если сейчас мяч у игрока
            if (currentPlayer != null && !currentIsAI)
            {
                // Позволяем только ИИ (боту) отобрать мяч у игрока
                if (!newIsAI)
                    return;

                // Снимем флаг у игрока — отбирают ботом
                currentPlayer.SetHasBall(false, null);
            }
            // Если сейчас мяч у бота
            else if (currentIsAI)
            {
                // Если новый держатель — игрок, позволяем (игрок подбирает)
                if (newPlayer != null && !newIsAI)
                {
                    AIPlayer ai = holder.GetComponentInParent<AIPlayer>();
                    if (ai != null) ai.SetHasBall(false, null);
                }
                else
                {
                    // Боты не крадут друг у друга
                    return;
                }
            }
        }

        // Назначаем нового владельца
        holder = newHolder;
        isHeld = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
        col.gameObject.SetActive(false);

        IPlayerController playerController = newHolder.GetComponentInParent<IPlayerController>();
        if (playerController != null && !newIsAI)
        {
            rb.mass = 0;
            playerController.SetHasBall(true, this);
            return;
        }

        AIPlayer aiPlayer = newHolder.GetComponentInParent<AIPlayer>();
        if (aiPlayer != null)
        {
            rb.mass = 1;
            aiPlayer.SetHasBall(true, this);
        }
    }


    public void Pickup(Transform newHolder)
    {
        if (isHeld) return;

        isHeld = true;
        holder = newHolder;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
        col.gameObject.SetActive(false);
    }

    public void Throw(Vector3 direction)
    {
        // Защита от повторного броска
        if (!isHeld)
        {
            Debug.LogWarning("[Quaffle] Попытка бросить мяч, который не удерживается!", this);
            return;
        }

        isHeld = false;
        holder = null;
        rb.isKinematic = false;
        rb.useGravity = true;
        canBePickedUpTime = Time.time + 1f;
        col.gameObject.SetActive(true);
        rb.AddForce(direction * throwForce, ForceMode.Impulse);
        
        Debug.Log("[Quaffle] Мяч брошен успешно", this);
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
        if (Time.time < canBePickedUpTime || isHeld) return;

        Transform root = other.transform.root;
        IPlayerController player = root.GetComponentInChildren<IPlayerController>();
        if (player != null)
        {
            TryPickup(player.Transform);
            return;
        }

        AIPlayer ai = root.GetComponentInChildren<AIPlayer>();
        if (ai != null && !ai.hasBall)
        {
            Pickup(ai.transform);
            rb.mass = 1;
            ai.SetHasBall(true, this);
        }
    }
}

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

    #region State Check Methods

    // Проверка, принадлежит ли мяч конкретному держателю
    public bool IsHeldBy(Transform checkHolder)
    {
        return isHeld && holder == checkHolder;
    }

    // Проверка, свободен ли мяч
    public bool IsFree()
    {
        return !isHeld && holder == null && Time.time >= canBePickedUpTime;
    }

    // Получить текущего держателя
    public Transform GetCurrentHolder()
    {
        return isHeld ? holder : null;
    }

    #endregion

    #region Centralized Ownership Management

    /// <summary>
    /// Единственный метод для смены владельца мяча.
    /// Автоматически снимает флаги у старого владельца и устанавливает у нового.
    /// </summary>
    /// <param name="newHolder">Новый владелец</param>
    /// <param name="forceSteal">Принудительная кража (игнорирует проверку isHeld)</param>
    /// <returns>true если смена владельца успешна</returns>
    public bool TryChangeOwner(Transform newHolder, bool forceSteal = false)
    {
        if (newHolder == null)
        {
            Debug.LogWarning("[Quaffle] TryChangeOwner: newHolder is null!");
            return false;
        }
        
        // Проверка cooldown
        if (Time.time < canBePickedUpTime && !forceSteal)
        {
            return false;
        }
        
        // Если мяч уже у этого держателя
        if (isHeld && holder == newHolder)
        {
            return false;
        }
        
        // Получаем информацию о текущем владельце (только если holder не null)
        IPlayerController currentPlayer = null;
        AIPlayer currentAI = null;
        
        if (holder != null)
        {
            currentPlayer = holder.GetComponentInParent<IPlayerController>();
            currentAI = holder.GetComponentInParent<AIPlayer>();
        }
        
        // Получаем информацию о новом владельце
        IPlayerController newPlayer = newHolder.GetComponentInParent<IPlayerController>();
        AIPlayer newAI = newHolder.GetComponentInParent<AIPlayer>();
        
        // Если мяч занят и не разрешена кража
        if (isHeld && holder != null && !forceSteal)
        {
            return false;
        }
        
        // Снимаем флаг у текущего владельца
        if (isHeld && holder != null)
        {
            if (currentPlayer != null)
            {
                currentPlayer.SetHasBall(false, null);
            }
            else if (currentAI != null)
            {
                currentAI.SetHasBall(false, null);
            }
        }
        
        // Устанавливаем нового владельца
        holder = newHolder;
        isHeld = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
        col.gameObject.SetActive(false);
        
        // Устанавливаем флаг у нового владельца
        if (newPlayer != null)
        {
            rb.mass = 0;
            newPlayer.SetHasBall(true, this);
        }
        else if (newAI != null)
        {
            rb.mass = 1;
            newAI.SetHasBall(true, this);
        }
        
        return true;
    }

    #endregion

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
        TryChangeOwner(newHolder, forceSteal: false);
    }


    public void Pickup(Transform newHolder)
    {
        TryChangeOwner(newHolder, forceSteal: false);
    }

    public void Throw(Vector3 direction)
    {
        // Защита от повторного броска
        if (!isHeld)
        {
            Debug.LogWarning("[Quaffle] Попытка бросить мяч, который не удерживается!", this);
            return;
        }
        
        // Снимаем флаг у текущего владельца
        IPlayerController currentPlayer = holder?.GetComponentInParent<IPlayerController>();
        AIPlayer currentAI = holder?.GetComponentInParent<AIPlayer>();
        
        if (currentPlayer != null)
        {
            currentPlayer.SetHasBall(false, null);
        }
        else if (currentAI != null)
        {
            currentAI.SetHasBall(false, null);
        }

        isHeld = false;
        holder = null;
        rb.isKinematic = false;
        rb.useGravity = true;
        canBePickedUpTime = Time.time + 1f;
        col.gameObject.SetActive(true);
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
        // Если мяч занят или на cooldown - игнорируем
        if (isHeld || Time.time < canBePickedUpTime) return;

        Transform root = other.transform.root;
        
        // Проверяем игрока
        IPlayerController player = root.GetComponentInChildren<IPlayerController>();
        if (player != null)
        {
            TryChangeOwner(player.Transform, forceSteal: false);
            return;
        }

        // Проверяем AI бота
        AIPlayer ai = root.GetComponentInChildren<AIPlayer>();
        if (ai != null && !ai.hasBall)
        {
            TryChangeOwner(ai.transform, forceSteal: false);
        }
    }
}

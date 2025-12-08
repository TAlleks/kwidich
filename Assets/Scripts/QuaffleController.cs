using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class QuaffleController : MonoBehaviour
{
    #region Inspector Fields

    [Header("Physics")]
    [SerializeField] private float throwForce = 12f;          // Сила броска (импульс)
    [SerializeField] private float maxSpeed = 18f;             // Макс. скорость после броска
    [SerializeField] private float linearDamping = 0.8f;       // Затухание линейной скорости (аналог drag)
    [SerializeField] private float angularDamping = 2f;        // Затухание вращения (аналог angularDrag)

    [Header("Pickup & Throw")]
    [SerializeField] private LayerMask playerLayer;           // Слой игрока (например, "Player")
    [SerializeField] private float throwRaycastDistance = 15f; // Дальность луча для направления

    [Header("Visual & Audio")]
    [SerializeField] private ParticleSystem pickupEffect;
    [SerializeField] private AudioClip pickupSound;
    [SerializeField] private AudioClip throwSound;
    [SerializeField] private AudioSource audioSource;

    #endregion

    #region Private Fields

    private Rigidbody rb;
    private bool isHeld;
    private Transform holder;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        Collider collider = GetComponent<Collider>();
        rb.useGravity = false;
        rb.linearDamping = linearDamping;
        rb.angularDamping = angularDamping;

        // Убеждаемся, что коллайдер — триггер
        if (collider != null && !collider.isTrigger)
        {
            Debug.LogWarning($"[Quaffle] Collider не был триггером — исправлено.", this);
            collider.isTrigger = true;
        }

        // Инициализация AudioSource
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        ResetState();
    }

    private void Update()
    {
        // Бросок по нажатию (Fire1 = ЛКМ, R2, A)
        if (isHeld && Input.GetButtonDown("Fire1"))
        {
            ThrowQuaffle();
        }

        if (isHeld && holder != null)
        {
            Vector3 holdOffset = holder.forward * 1.1f + holder.up * 0.4f + holder.right * 0.2f;
            transform.position = Vector3.Lerp(transform.position, holder.position + holdOffset, Time.deltaTime * 12f);
            transform.rotation = Quaternion.Lerp(transform.rotation, holder.rotation, Time.deltaTime * 10f);
        }
    }

    private void FixedUpdate()
    {
        // Ограничение скорости полёта (только когда НЕ в руках)
        if (!isHeld)
        {
            if (rb.linearVelocity.magnitude > maxSpeed)
            {
                rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
            }
        }
        else
        {
            // Полная остановка физики, когда в руках
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    #endregion

    #region Pickup Logic

    private void OnTriggerEnter(Collider other)
    {
        // Проверка по слою ИЛИ по наличию BroomController + правильной команды
        if (((1 << other.gameObject.layer) & playerLayer.value) != 0)
        {
            BroomController broom = other.attachedRigidbody?.GetComponent<BroomController>()
                                 ?? other.GetComponentInParent<BroomController>();

            if (broom != null && broom.team == Team.Player)
            {
                Pickup(broom.transform);
            }
        }
    }

    private void Pickup(Transform newHolder)
    {
        if (isHeld) return;

        isHeld = true;
        holder = newHolder;

        // Визуальные/звуковые эффекты
        if (pickupEffect != null)
        {
            var ps = Instantiate(pickupEffect, transform.position, Quaternion.identity);
            Destroy(ps.gameObject, ps.main.duration + 0.2f);
        }

        if (pickupSound != null)
        {
            audioSource.PlayOneShot(pickupSound);
        }

        Debug.Log("[Quaffle] Подобран игроком", this);
    }

    #endregion

    #region Throw Logic

    private void ThrowQuaffle()
    {
        if (!isHeld || holder == null) return;

        // Определяем направление: базовое — вперёд от игрока
        Vector3 direction = holder.forward;

        // Уточняем через Raycast (если хочется "умного" броска в цель)
        Vector3 rayOrigin = holder.position + holder.up * 0.8f;
        if (Physics.Raycast(rayOrigin, holder.forward, out RaycastHit hit, throwRaycastDistance))
        {
            // Если попали в "игровой" объект (не в небо/пустоту) — корректируем
            if (hit.collider.CompareTag("Ring") || hit.collider.CompareTag("Goal") || hit.collider.CompareTag("Enemy"))
            {
                Vector3 toTarget = (hit.point - transform.position).normalized;
                if (Vector3.Angle(holder.forward, toTarget) < 40f)
                {
                    direction = toTarget;
                }
            }
        }

        // Освобождаем
        isHeld = false;
        holder = null;

        // Применяем импульс
        rb.AddForce(direction * throwForce, ForceMode.Impulse);

        // Звук
        if (throwSound != null)
        {
            audioSource.PlayOneShot(throwSound);
        }

        Debug.Log($"[Quaffle] Брошен со скоростью ~{throwForce} в направлении {direction}", this);
    }

    #endregion

    #region Reset & Utilities

    private void ResetState()
    {
        isHeld = false;
        holder = null;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void OnDisable()
    {
        ResetState();
    }

    #endregion

    #region Public Interface

    public bool IsHeld() => isHeld;
    public Transform GetHolder() => holder;

    public void ForceDrop()
    {
        if (isHeld)
        {
            isHeld = false;
            holder = null;
            rb.AddForce(Vector3.up * 3f + transform.forward * 2f, ForceMode.Impulse);
        }
    }

    public void Respawn(Vector3 position, Quaternion rotation)
    {
        ResetState();
        transform.SetPositionAndRotation(position, rotation);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    #endregion
}
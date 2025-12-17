using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class AIPlayer : MonoBehaviour
{
    [Header("Debug")]
    public bool debugLogs = true;

    void Log(string msg)
    {
        if (debugLogs)
            Debug.Log($"[AIPlayer:{name}] {msg}");
    }

    [Header("Goal Approach")]
    public float stopDistanceFromGoal = 12f;

    [Header("Steal Settings")]
    public float stealCooldown = 2f;
    private float lastStealTime = -999f;

    [Header("Settings")]
    public Team team = Team.Enemy;
    public float moveSpeed = 15f;
    public float turnSpeed = 5f;
    public float scoringDistance = 20f;
    public float minThrowDistance = 5f;
    public float throwChance = 0.85f;

    [Header("Pickup Settings")]
    public float pickupCooldown = 2f;
    private float lastThrowTime = -999f;

    [Header("References")]
    public Transform model;

    private Rigidbody rb;
    public bool hasBall = false;
    private Quaffle currentQuaffle;
    private Transform currentTarget;

    private GoalRing[] allGoals;


    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 1f;
        rb.angularDamping = 3f;
    }

    void Start()
    {
        allGoals = FindObjectsByType<GoalRing>(FindObjectsSortMode.None);
        Log("Инициализирован");
    }

    void Update()
    {
        RotateModel();

    }

    void FixedUpdate()
    {
        MakeDecision();
        MoveToTarget();
        CheckActions();
    }

    void MakeDecision()
    {
        if (hasBall)
        {
            currentTarget = FindBestGoal();
            Log("Есть мяч → лечу к воротам");
            return;
        }

        if (Time.time >= lastThrowTime + pickupCooldown)
        {
            currentTarget = FindNearestFreeQuaffle();
            if (currentTarget != null)
                Log("Ищу свободный мяч");
        }
        else
        {
            Log("Кулдаун подбора — игнорирую мячи");
        }

        if (currentTarget != null) return;

        currentTarget = FindNearestBotWithBall();
        if (currentTarget != null)
            Log("Лечу к врагу с мячом");
    }

    void MoveToTarget()
    {
        if (currentTarget == null)
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * 3f);
            return;
        }

        float distance = Vector3.Distance(transform.position, currentTarget.position);

        if (hasBall && currentTarget.GetComponent<GoalRing>() != null)
        {
            if (distance <= stopDistanceFromGoal)
            {
                Log($"Останавливаюсь перед воротами (dist={distance:F1})");

                rb.linearVelocity = Vector3.Lerp(
                    rb.linearVelocity,
                    Vector3.zero,
                    Time.fixedDeltaTime * 5f
                );
                return;
            }
        }

        Vector3 dir = (currentTarget.position - transform.position).normalized;
        rb.linearVelocity = dir * moveSpeed;

        Log($"Лечу к {currentTarget.name}, dist={distance:F1}");
    }

    void RotateModel()
    {

        // Поворачиваем питомца на камеру при появлении
        if (currentTarget != null)
        {
            Vector3 lookDirection = currentTarget.transform.position  - transform.position;
            //lookDirection.y = 0f; // убираем наклон по вертикали

            if (lookDirection != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(lookDirection);
            }
        }

    }

    void CheckActions()
    {
        if (currentTarget == null) return;

        AIPlayer targetBot = currentTarget.GetComponent<AIPlayer>();
        if (!hasBall && targetBot != null && targetBot.hasBall)
        {
            float dist = Vector3.Distance(transform.position, currentTarget.position);

            if (dist <= 3f && Time.time >= lastStealTime + stealCooldown)
            {
                Log("Пытаюсь украсть мяч");
                StealBall(targetBot);
                lastStealTime = Time.time;
            }
        }

        if (hasBall && currentTarget.GetComponent<GoalRing>() != null)
        {
            float dist = Vector3.Distance(transform.position, currentTarget.position);

            if (dist <= scoringDistance && dist >= minThrowDistance)
            {
                Log($"БРОСОК по воротам (dist={dist:F1})");
                ThrowBall(currentTarget.position);
            }
        }
    }

    void StealBall(AIPlayer targetBot)
    {
        if (targetBot == null || !targetBot.hasBall) return;

        Quaffle q = targetBot.currentQuaffle;
        if (q != null)
        {
            targetBot.SetHasBall(false, null);
            SetHasBall(true, q);
            Log("УКРАЛ мяч");
        }
    }

    Transform FindBestGoal()
    {
        Transform best = null;
        float bestDist = Mathf.Infinity;

        foreach (var goal in allGoals)
        {
            if (goal == null) continue;
            if (goal.GetScoredTeam() == team) continue;

            float d = Vector3.Distance(transform.position, goal.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = goal.transform;
            }
        }

        return best;
    }

    Transform FindNearestFreeQuaffle()
    {
        Quaffle[] quaffles = FindObjectsByType<Quaffle>(FindObjectsSortMode.None);
        Transform best = null;
        float bestDist = Mathf.Infinity;

        foreach (var q in quaffles)
        {
            if (q == null || q.isHeld) continue;

            float d = Vector3.Distance(transform.position, q.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = q.transform;
            }
        }

        return best;
    }

    public void SetHasBall(bool value, Quaffle quaffle)
    {
        if (value == true)
        {
            if (Time.time < lastThrowTime + pickupCooldown)
            {
                Log("❌ Не могу взять мяч — кулдаун");
                return;
            }

            Log("✅ Взял мяч");
        }
        else
        {
            Log("❌ Потерял мяч");
        }

        hasBall = value;
        currentQuaffle = value ? quaffle : null;
    }

    Transform FindNearestBotWithBall()
    {
        AIPlayer[] players = FindObjectsByType<AIPlayer>(FindObjectsSortMode.None);
        Transform best = null;
        float bestDist = Mathf.Infinity;

        foreach (var p in players)
        {
            if (p == null || p == this || !p.hasBall) continue;

            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = p.transform;
            }
        }

        return best;
    }

    void ThrowBall(Vector3 goalPos)
    {
        if (!hasBall || currentQuaffle == null) return;

        Log("🏐 Бросаю мяч");

        Vector3 dir = (goalPos - transform.position).normalized;
        dir.y += 0.2f;

        if (Random.value > throwChance)
        {
            dir += Random.insideUnitSphere * 0.5f;
            dir.Normalize();
            Log("❗ Неточный бросок");
        }

        currentQuaffle.Throw(dir);
        SetHasBall(false, null);

        lastThrowTime = Time.time;
    }
}

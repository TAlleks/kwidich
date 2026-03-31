using UnityEngine;
using System.Collections;

/// <summary>
/// Интерфейс для всех контроллеров игрока (BroomController, FuturiftMoving)
/// Позволяет другим скриптам работать с любым типом контроллера единообразно
/// </summary>
public interface IPlayerController
{
    /// <summary>
    /// Есть ли у игрока мяч
    /// </summary>
    bool HasBall { get; }

    /// <summary>
    /// Текущий мяч в руках игрока
    /// </summary>
    Quaffle CurrentQuaffle { get; }

    /// <summary>
    /// Команда игрока
    /// </summary>
    Team Team { get; }

    /// <summary>
    /// Transform игрока
    /// </summary>
    Transform Transform { get; }

    /// <summary>
    /// Установить состояние владения мячом
    /// </summary>
    void SetHasBall(bool value, Quaffle incomingQuaffle);

    /// <summary>
    /// Сохранить стартовую позицию (вызывается в начале игры)
    /// </summary>
    void SaveStartPosition();

    /// <summary>
    /// Респавн на стартовую позицию (полная последовательность)
    /// </summary>
    void RespawnToStartPosition();

    /// <summary>
    /// Плавное замедление игрока (БЕЗ телепортации)
    /// Используется для синхронной телепортации с ботами
    /// </summary>
    IEnumerator SlowdownSequence();

    /// <summary>
    /// Телепортация на стартовую позицию (БЕЗ замедления)
    /// </summary>
    void TeleportToStart();

    /// <summary>
    /// Разблокировка управления игрока
    /// </summary>
    void EnableInput();
}

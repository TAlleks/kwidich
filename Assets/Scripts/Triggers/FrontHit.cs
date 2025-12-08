using UnityEngine;
using Bhaptics.SDK2;

public class FrontHit : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        BhapticsLibrary.Play(eventId: BhapticsEvent.FRONT_HIT);
    }
}

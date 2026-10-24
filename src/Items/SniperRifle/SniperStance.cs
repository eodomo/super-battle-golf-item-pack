using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Lives on every held sniper rifle. Not aiming, the rifle is carried at "low ready": the right arm hangs by the side with
/// the hand at waist height, and the rifle is turned about the hand's grip so the muzzle points forward and down (a little
/// across the body) while the stock comes back up under the arm, into the armpit. Aiming hands the rifle and the arm back to
/// the elephant gun's own aim pose. Runs before <see cref="HeldGrip"/>, which keeps the grip in the palm.
/// </summary>
[DefaultExecutionOrder(50)]
internal class SniperStance : MonoBehaviour
{
    private const float BlendPerSecond = 8f;
    private const float DownDegrees = 32f;     // how far the muzzle points below level
    private const float AcrossDegrees = 10f;   // and how far to the player's left of straight ahead

    private Quaternion restLocalRotation;
    private bool hasRest;
    private PlayerInfo ownerInfo;
    private Transform shoulder, wrist;
    private float weight;

    private void OnEnable()
    {
        if (!hasRest)
        {
            restLocalRotation = transform.localRotation;
            hasRest = true;
        }
        weight = 0f;
    }

    private void LateUpdate()
    {
        transform.localRotation = restLocalRotation;
        ownerInfo ??= GetComponentInParent<PlayerInfo>();
        if (ownerInfo == null)
        {
            return; // the equipment template, not on a player
        }
        bool ready = !ownerInfo.NetworkedIsAimingItem && !ownerInfo.Inventory.IsUsingItemAtAll;
        weight = Mathf.MoveTowards(weight, ready ? 1f : 0f, BlendPerSecond * Time.deltaTime);
        if (weight <= 0.001f)
        {
            return;
        }
        if (shoulder == null)
        {
            for (Transform t = transform.parent; t != null && t != ownerInfo.transform; t = t.parent)
            {
                if (t.name == "R_Wrist")
                {
                    wrist = t;
                }
                else if (t.name == "R_Shoulder")
                {
                    shoulder = t;
                    break;
                }
            }
        }
        Vector3 forward = Vector3.ProjectOnPlane(ownerInfo.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        // The arm hangs down by the side, the hand a little in front of the hip.
        if (shoulder != null && wrist != null)
        {
            Vector3 toHand = wrist.position - shoulder.position;
            Vector3 target = (forward * 0.45f + right * 0.12f - Vector3.up * 0.9f).normalized;
            if (toHand.sqrMagnitude > 1e-4f)
            {
                shoulder.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(toHand, target * toHand.magnitude), weight) * shoulder.rotation;
            }
        }

        Vector3 muzzle = Quaternion.AngleAxis(-AcrossDegrees, Vector3.up) * (Quaternion.AngleAxis(DownDegrees, right) * forward);
        // The model's muzzle is along its -Z (see the OBJ export), so its +Z, the stock, points back and up under the arm.
        Quaternion pose = Quaternion.LookRotation(-muzzle, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, pose, weight);
    }
}

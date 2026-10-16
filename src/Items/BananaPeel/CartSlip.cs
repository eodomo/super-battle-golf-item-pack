using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// A golf cart (or Megazord) that drove over a banana peel: it hops and spins out (Mario Kart style), losing most of its
/// speed. Runs on whichever client simulates the cart; everyone else sees it through the cart's normal syncing.
/// </summary>
internal sealed class CartSlip : MonoBehaviour
{
    private const float Seconds = 1.2f;

    private Rigidbody body;
    private float until;
    private float spin;

    public static bool IsSlipping(GolfCartInfo cart) => cart != null && cart.TryGetComponent(out CartSlip slip) && Time.time < slip.until;

    public static void Begin(GolfCartInfo cart, Rigidbody body)
    {
        if (!cart.TryGetComponent(out CartSlip slip))
        {
            slip = cart.gameObject.AddComponent<CartSlip>();
        }
        slip.body = body;
        slip.until = Time.time + Seconds;
        // Spin whichever way it was already turning (or a random way), as many turns as configured, over the slip.
        float yaw = Vector3.Dot(body.angularVelocity, cart.transform.up);
        float sign = Mathf.Abs(yaw) > 0.1f ? Mathf.Sign(yaw) : (Random.value < 0.5f ? -1f : 1f);
        slip.spin = sign * Mathf.Max(0f, BananaPeelItem.CartSpinTurns.Value) * 2f * Mathf.PI / Seconds;
        body.linearVelocity += Vector3.up * 2.5f; // a little hop as the wheels lose grip
    }

    private void FixedUpdate()
    {
        if (body == null || Time.time >= until)
        {
            Destroy(this);
            return;
        }
        if (body.isKinematic)
        {
            return;
        }
        // Hold the spin against the cart's own steering and grip, and bleed off speed.
        Vector3 up = transform.up;
        Vector3 w = body.angularVelocity;
        body.angularVelocity = w - up * Vector3.Dot(w, up) + up * spin;
        Vector3 v = body.linearVelocity;
        Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
        body.linearVelocity = v - flat * 0.04f;
    }
}

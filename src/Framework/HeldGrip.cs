using System.Collections.Generic;
using UnityEngine;

namespace ItemPack;

/// <summary>
/// Keeps a held item in the hand. Every item borrows a vanilla item's hold animation, but the game's attach bone
/// (Tool1) sits wherever that animation's hand pose puts it, so a model parked at a fixed offset from it ends up
/// floating in front of the palm. This puts the model's grip point (its middle unless the item says otherwise)
/// on the palm every frame, after the animator has posed the hand, for everyone's copy of the model.
/// </summary>
[DefaultExecutionOrder(100)]
internal sealed class HeldGrip : MonoBehaviour
{
    /// <summary>Where the hand closes, between the wrist and the finger bones (0 = wrist, 1 = finger centroid).</summary>
    private const float PalmAlong = 0.6f;

    /// <summary>Item id (a plain int, so it survives the equipment prefab being instantiated).</summary>
    public int ItemId;

    private CustomItem item;

    private Transform wrist;
    private Transform tool;
    private readonly List<Transform> fingers = new();
    private bool searched;
    private bool hasGrip;
    private Vector3 grip;

    private void LateUpdate()
    {
        if (!Find())
        {
            return; // the equipment template, not on a player
        }
        if (item == null && !ItemCatalog.TryGet((ItemType)ItemId, out item))
        {
            return;
        }
        if (!hasGrip && !MeasureGrip())
        {
            return;
        }
        Vector3 centroid = Vector3.zero;
        foreach (Transform finger in fingers)
        {
            centroid += finger.position / fingers.Count;
        }
        Vector3 palm = fingers.Count > 0 ? Vector3.Lerp(wrist.position, centroid, PalmAlong) : tool.position;
        palm += tool.TransformVector(item.HeldOffset.Value);
        transform.position += palm - transform.TransformPoint(grip);
    }

    private bool Find()
    {
        if (searched)
        {
            return wrist != null;
        }
        for (Transform t = transform.parent; t != null; t = t.parent)
        {
            if (t.name == "R_Wrist")
            {
                wrist = t;
                break;
            }
            if (t.name == "Tool1")
            {
                tool = t;
            }
        }
        if (wrist == null)
        {
            // Not under a player (yet): look again next frame only if we might still be getting parented.
            searched = transform.parent != null && transform.GetComponentInParent<PlayerInfo>() != null;
            return false;
        }
        searched = true;
        tool ??= wrist;
        foreach (Transform t in wrist.GetComponentsInChildren<Transform>(true))
        {
            if (t != wrist && !t.IsChildOf(tool) && t.name.StartsWith("R_") && (t.name.EndsWith("1") || t.name.EndsWith("2")))
            {
                fingers.Add(t);
            }
        }
        return true;
    }

    /// <summary>The item's grip point in the model's own space: its own say, or the middle of what it renders.</summary>
    private bool MeasureGrip()
    {
        if (item.HeldGrip is Vector3 custom)
        {
            grip = custom;
            hasGrip = true;
            return true;
        }
        Bounds bounds = default;
        bool any = false;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(false))
        {
            if (renderer is TrailRenderer or ParticleSystemRenderer || !renderer.enabled)
            {
                continue;
            }
            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        if (!any)
        {
            return false;
        }
        grip = transform.InverseTransformPoint(bounds.center);
        hasGrip = true;
        return true;
    }
}

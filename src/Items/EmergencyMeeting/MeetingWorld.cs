using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

/// <summary>
/// A meeting on every client: the screen and klaxon right away, then (while the screen still covers the view) the
/// table appears and the local player is moved to their seat facing it, frozen with the camera free, until the
/// meeting ends and the table vanishes.
/// </summary>
internal static class MeetingWorld
{
    private const float TeleportDelay = 1.1f;
    private const float TableExtraSeconds = 2f;

    private static MeetingCalled current;
    private static bool active;
    private static float calledAt;
    private static bool seated;
    private static bool frozen;
    private static GameObject table;
    private static bool sceneHookInstalled;

    /// <summary>A meeting is on (from the button press until the table is gone).</summary>
    public static bool InProgress => active;

    /// <summary>The local player is held at the table.</summary>
    public static bool Frozen => frozen;

    public static void OnCalled(MeetingCalled msg)
    {
        EnsureSceneHook();
        ClearAll();
        current = msg;
        active = true;
        calledAt = Time.time;
        seated = false;
        string caller = "Someone";
        if (NetworkClient.spawned.TryGetValue(msg.callerNetId, out NetworkIdentity identity) && identity != null
            && identity.TryGetComponent(out PlayerInfo player) && player.PlayerId != null)
        {
            caller = player.PlayerId.PlayerNameNoRichText;
        }
        MeetingOverlay.Show(caller);
        MeetingSounds.PlayKlaxon(MeetingItem.SoundVolume.Value);
    }

    public static void Tick()
    {
        if (!active)
        {
            return;
        }
        float t = Time.time - calledAt;
        if (!seated && t >= TeleportDelay)
        {
            seated = true;
            SpawnTable();
            TakeSeat();
        }
        float freezeEnd = TeleportDelay + current.freezeSeconds;
        if (frozen)
        {
            if (t >= freezeEnd || GameManager.LocalPlayerInfo == null)
            {
                Unfreeze();
            }
            else
            {
                SetGameplayControls(false); // the game turns them back on when its input mode changes
            }
        }
        if (t >= freezeEnd + TableExtraSeconds)
        {
            ClearAll();
        }
    }

    private static void SpawnTable()
    {
        Vector3 center = Ground(current.center, WorldMask.Solid);
        table = new GameObject("MeetingTable");
        table.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, current.seatYaw, 0f));
        if (MeetingItem.Table?.Mesh != null)
        {
            MeetingItem.Table.InitMaterials(ModelAsset.SharedTemplate);
            MeetingItem.Table.CreateModel(table.transform, 1f);
        }
        VfxManager.PlayPooledVfxLocalOnly(VfxType.SmokeBombActivation, center, Quaternion.identity);
    }

    /// <summary>Moves the local player to their seat, facing the table, and freezes them there.</summary>
    private static void TakeSeat()
    {
        PlayerInfo local = GameManager.LocalPlayerInfo;
        if (local == null || local.Movement == null || !local.Movement.IsVisible)
        {
            return; // not playing right now (e.g. spectating)
        }
        int seat = System.Array.IndexOf(current.seats, local.netId);
        if (seat < 0)
        {
            return;
        }
        Vector3 outward = Quaternion.Euler(0f, current.seatYaw + seat * 360f / current.seats.Length, 0f) * Vector3.forward;
        Vector3 feet = Ground(current.center + outward * current.seatRadius, GameManager.LayerSettings.PlayerGroundableMask) + Vector3.up * 0.02f;
        Quaternion facing = Quaternion.LookRotation(-outward, Vector3.up);
        local.Inventory.CancelItemUse();
        frozen = true;
        SetGameplayControls(false);
        Plugin.Runner.StartCoroutine(MoveTo(local, feet, facing));
    }

    private static IEnumerator MoveTo(PlayerInfo local, Vector3 feet, Quaternion facing)
    {
        if (local.ActiveGolfCartSeat.IsValid())
        {
            local.ExitGolfCart(GolfCartExitType.Default);
            yield return null;
            yield return null;
        }
        if (local == null)
        {
            yield break;
        }
        local.Movement.Teleport(feet, facing, resetState: false);
        if (CameraModuleController.CurrentModule is OrbitCameraModule orbit)
        {
            orbit.SetYaw(facing.eulerAngles.y);
        }
    }

    private static Vector3 Ground(Vector3 point, int mask) =>
        Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 12f, mask, QueryTriggerInteraction.Ignore) ? hit.point : point;

    private static void Unfreeze()
    {
        if (!frozen)
        {
            return;
        }
        frozen = false;
        if (!BoogieDance.IsDancing)
        {
            SetGameplayControls(true);
        }
    }

    /// <summary>Movement, jumping, swinging and items off (the camera stays free), only in normal play, like the boogie bomb's dance.</summary>
    private static void SetGameplayControls(bool enabled)
    {
        if (InputManager.CurrentMode != InputMode.Regular || InputManager.Controls == null)
        {
            return;
        }
        if (enabled)
        {
            InputManager.Controls.Gameplay.Enable();
            InputManager.Controls.Hotkeys.Enable();
        }
        else
        {
            InputManager.Controls.Gameplay.Disable();
            InputManager.Controls.Hotkeys.Disable();
        }
    }

    public static void ClearAll()
    {
        Unfreeze();
        if (table != null)
        {
            Object.Destroy(table);
            table = null;
        }
        active = false;
    }

    private static void EnsureSceneHook()
    {
        if (sceneHookInstalled)
        {
            return;
        }
        sceneHookInstalled = true;
        SceneManager.activeSceneChanged += (_, _) =>
        {
            ClearAll();
            MeetingOverlay.Hide();
        };
    }
}

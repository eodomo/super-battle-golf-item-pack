#if DEVTOOLS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Mirror;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ItemPack.DevMcp;

/// <summary>The dev MCP server's commands. Each runs on the main thread as a coroutine and fills a <see cref="DevReply"/>.</summary>
internal static class DevCommands
{
    public static IEnumerator Begin(string tool, JObject a, DevReply r)
    {
        switch (tool)
        {
            case "state": return State(r);
            case "held_report": return Once(() => HeldReport(r));
            case "spawn_dummy": return Once(() => SpawnDummy(a, r));
            case "remove_dummies": return Once(() => r.Text($"Removed {DevDummies.RemoveAll()} dummies."));
            case "teleport": return Teleport(a, r);
            case "camera": return CameraCommand(a, r);
            case "screenshot": return Screenshot(a, r);
            case "list_items": return ListItems(r);
            case "give_item": return GiveItem(a, r);
            case "select_slot": return SelectSlot(a, r);
            case "use_item": return UseItem(a, r);
            case "interact": return Interact(a, r);
            case "input": return Input(a, r);
            case "list_actions": return Once(() => r.Json(DevInput.ListActions()));
            case "inspect": return Once(() => DevInspect.Inspect(a, r));
            case "read_log": return Once(() => DevInspect.ReadLog(a, r));
            case "spawn_item": return SpawnItem(a, r);
            case "list_prefabs": return ListPrefabs(a, r);
            case "spawn_prefab": return SpawnPrefab(a, r);
            case "find_objects": return FindObjects(a, r);
            case "ui": return Ui(a, r);
            case "start_game": return StartGame(r);
            case "time_scale": return Once(() => { Time.timeScale = (float?)a["value"] ?? 1f; r.Text($"Time scale {Time.timeScale}."); });
            case "wait": return Wait(a, r);
            case "sequence": return Sequence(a, r);
            default: return Once(() => r.Fail($"Unknown tool '{tool}'."));
        }
    }

    private static IEnumerator Once(Action action)
    {
        action();
        yield break;
    }

    private static PlayerMovement RequirePlayer(DevReply r)
    {
        PlayerMovement player = GameManager.LocalPlayerMovement;
        if (player == null)
        {
            r.Fail("No local player: start a game first (start_game from the main menu).");
        }
        return player;
    }

    private static bool RequireHost(DevReply r)
    {
        if (!NetworkServer.active)
        {
            r.Fail("Only the host can do this.");
            return false;
        }
        return true;
    }

    /// <summary>A dummy player at position (default: 3 m in front of the local player, facing them).</summary>
    private static void SpawnDummy(JObject a, DevReply r)
    {
        PlayerMovement player = RequirePlayer(r);
        if (player == null || !RequireHost(r))
        {
            return;
        }
        Vector3 position = DevJson.ReadVector(a["position"]) is Vector3 p ? p : player.transform.position + player.transform.forward * 3f;
        float yaw = (float?)a["yaw"] ?? player.transform.eulerAngles.y + 180f;
        if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 20f, GameManager.LayerSettings.PlayerGroundableMask, QueryTriggerInteraction.Ignore) && !(a["position"] is JArray))
        {
            position = hit.point + Vector3.up * 0.1f;
        }
        PlayerInfo dummy = DevDummies.Spawn(position, yaw, (string)a["name"], out string error);
        if (dummy == null)
        {
            r.Fail(error);
            return;
        }
        r.Text($"Spawned a dummy player (net id {dummy.netId}) at {position}.");
    }

    // ---- state ----

    /// <summary>Where the held item sits relative to the hand: bounds center and the finger bones' centroid, in Tool1 space.</summary>
    private static void HeldReport(DevReply r)
    {
        PlayerMovement player = RequirePlayer(r);
        if (player == null)
        {
            return;
        }
        Transform wrist = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "R_Wrist");
        Transform tool = wrist != null ? wrist.Find("Tool1") : null;
        if (tool == null || tool.childCount == 0)
        {
            r.Fail("No held equipment under R_Wrist/Tool1.");
            return;
        }
        Transform equipment = null;
        foreach (Transform child in tool)
        {
            if (child.gameObject.activeInHierarchy && !child.name.StartsWith("Golf club"))
            {
                equipment = child;
            }
        }
        if (equipment == null)
        {
            r.Fail("No active held equipment under Tool1.");
            return;
        }
        var fingers = new List<Vector3>();
        foreach (Transform t in wrist.GetComponentsInChildren<Transform>(true))
        {
            if (t != wrist && !t.IsChildOf(tool) && t.name.StartsWith("R_") && (t.name.EndsWith("1") || t.name.EndsWith("2")))
            {
                fingers.Add(t.position);
            }
        }
        Vector3 fingerCenter = Vector3.zero;
        foreach (Vector3 f in fingers)
        {
            fingerCenter += f / fingers.Count;
        }
        Bounds bounds = default;
        bool any = false;
        foreach (Renderer renderer in equipment.GetComponentsInChildren<Renderer>(false))
        {
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
        r.Json(new Dictionary<string, object>
        {
            ["equipment"] = equipment.name,
            ["equipment_local_pos"] = DevJson.V(equipment.localPosition),
            ["equipment_local_euler"] = DevJson.V(equipment.localEulerAngles),
            ["tool_world"] = DevJson.V(tool.position),
            ["tool_euler_world"] = DevJson.V(tool.eulerAngles),
            ["fingers"] = fingers.Count,
            ["finger_center_tool_local"] = DevJson.V(tool.InverseTransformPoint(fingerCenter)),
            ["wrist_tool_local"] = DevJson.V(tool.InverseTransformPoint(wrist.position)),
            ["bounds_center_tool_local"] = any ? DevJson.V(tool.InverseTransformPoint(bounds.center)) : null,
            ["bounds_size_world"] = any ? DevJson.V(bounds.size) : null,
        });
    }

    private static IEnumerator State(DevReply r)
    {
        var state = new Dictionary<string, object>
        {
            ["scene"] = SceneManager.GetActiveScene().name,
            ["host"] = NetworkServer.active,
            ["connected"] = NetworkClient.isConnected,
            ["driving_range"] = SingletonBehaviour<DrivingRangeManager>.HasInstance,
            ["input_mode"] = InputManager.CurrentMode.ToString(),
            ["time_scale"] = Time.timeScale,
            ["held_inputs"] = DevInput.Held(),
            ["frame"] = Time.frameCount,
        };
        Camera cam = Camera.main;
        if (cam != null)
        {
            var camera = new Dictionary<string, object>
            {
                ["module"] = CameraModuleController.CurrentModuleType.ToString(),
                ["position"] = DevJson.V(cam.transform.position),
                ["forward"] = DevJson.V(cam.transform.forward),
                ["fov"] = DevJson.R(cam.fieldOfView),
                ["free_override"] = DevCamera.Free,
            };
            if (CameraModuleController.TryGetOrbitModule(out OrbitCameraModule orbit))
            {
                camera["orbit_yaw"] = DevJson.R(orbit.Yaw);
                camera["orbit_pitch"] = DevJson.R(orbit.Pitch);
            }
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore))
            {
                camera["crosshair_hit"] = new { point = DevJson.V(hit.point), distance = DevJson.R(hit.distance), collider = hit.collider.name };
            }
            state["camera"] = camera;
        }
        PlayerMovement player = GameManager.LocalPlayerMovement;
        if (player != null)
        {
            PlayerInfo info = GameManager.LocalPlayerInfo;
            var p = new Dictionary<string, object>
            {
                ["name"] = info != null && info.PlayerId != null ? info.PlayerId.PlayerNameNoRichText : null,
                ["position"] = DevJson.V(player.transform.position),
                ["yaw"] = DevJson.R(player.transform.eulerAngles.y),
                ["grounded"] = player.IsGrounded,
            };
            if (info != null && info.Rigidbody != null)
            {
                p["velocity"] = DevJson.V(info.Rigidbody.linearVelocity);
            }
            PlayerInventory inventory = GameManager.LocalPlayerInventory;
            if (inventory != null)
            {
                var slots = new List<string>();
                for (int i = 0; i < inventory.slots.Count; i++)
                {
                    InventorySlot slot = inventory.slots[i];
                    slots.Add(slot.itemType == ItemType.None ? $"{i}: empty" : $"{i}: {ItemName(slot.itemType)} ({slot.remainingUses} uses)");
                }
                p["inventory"] = slots;
                p["equipped_slot"] = inventory.EquippedItemIndex;
                p["equipped_item"] = ItemName(inventory.GetEffectivelyEquippedItem());
            }
            PlayerInteractableTargeter targeter = GameManager.LocalPlayerInteractableTargeter;
            if (targeter != null && targeter.HasTarget)
            {
                p["interact_target"] = targeter.CurrentTargetEntity.name;
            }
            state["player"] = p;
            var others = new List<object>();
            foreach (PlayerInfo other in GameManager.RemotePlayers)
            {
                if (other != null)
                {
                    others.Add(new { name = other.PlayerId?.PlayerNameNoRichText, net_id = other.netId, dummy = DevDummies.IsDummy(other.netId), position = DevJson.V(other.transform.position) });
                }
            }
            state["other_players"] = others;
        }
        r.Json(state);
        yield break;
    }

    // ---- player ----

    private static IEnumerator Teleport(JObject a, DevReply r)
    {
        PlayerMovement player = RequirePlayer(r);
        if (player == null)
        {
            yield break;
        }
        Vector3 position = player.transform.position;
        bool relative = (bool?)a["relative"] ?? false;
        float? x = (float?)a["x"], y = (float?)a["y"], z = (float?)a["z"];
        if (DevJson.ReadVector(a["position"]) is Vector3 p)
        {
            x = p.x;
            y = p.y;
            z = p.z;
        }
        if (relative)
        {
            position += new Vector3(x ?? 0f, y ?? 0f, z ?? 0f);
        }
        else
        {
            position = new Vector3(x ?? position.x, y ?? position.y, z ?? position.z);
            if (y == null && !Ground(position, out position))
            {
                r.Text("No ground found below that spot; kept the current height.");
            }
        }
        float yaw = (float?)a["yaw"] ?? player.transform.eulerAngles.y;
        player.Teleport(position, Quaternion.Euler(0f, yaw, 0f), (bool?)a["reset_state"] ?? true);
        yield return null;
        yield return null;
        r.Text($"Teleported to {Fmt(player.transform.position)}, yaw {yaw:0}.");
    }

    /// <summary>
    /// Drops a point onto the first surface below it (searching from a little above, so it stays under trees and
    /// roofs); if there is none, onto the top surface at that x/z.
    /// </summary>
    private static bool Ground(Vector3 point, out Vector3 grounded)
    {
        return CastDown(point + Vector3.up * 3f, 500f, point, out grounded)
            || CastDown(new Vector3(point.x, 1000f, point.z), 2000f, point, out grounded);
    }

    private static bool CastDown(Vector3 origin, float length, Vector3 point, out Vector3 grounded)
    {
        grounded = point;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, length, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.GetComponentInParent<PlayerInfo>() != null)
            {
                continue; // a player, maybe ourselves
            }
            if (hit.distance < best)
            {
                best = hit.distance;
                grounded = hit.point + Vector3.up * 0.1f;
            }
        }
        return best < float.MaxValue;
    }

    // ---- camera ----

    private static IEnumerator CameraCommand(JObject a, DevReply r)
    {
        string mode = ((string)a["mode"] ?? "free").ToLowerInvariant();
        Camera cam = Camera.main;
        if (mode == "release")
        {
            DevCamera.Release();
            r.Text("Camera back under game control.");
            yield break;
        }
        if (cam == null)
        {
            r.Fail("No main camera.");
            yield break;
        }
        Vector3? lookAt = DevJson.ReadVector(a["look_at"]);
        if (mode == "orbit" || mode == "aim")
        {
            DevCamera.Release();
            if (lookAt.HasValue)
            {
                if (!DevCamera.AimOrbitAt(lookAt.Value, out string info))
                {
                    r.Fail(info);
                    yield break;
                }
                r.Text("Aimed the orbit camera at " + Fmt(lookAt.Value) + ": " + info);
            }
            else if (CameraModuleController.TryGetOrbitModule(out OrbitCameraModule orbit))
            {
                if (a["yaw"] != null)
                {
                    orbit.SetYaw((float)a["yaw"]);
                }
                if (a["pitch"] != null)
                {
                    orbit.SetPitch((float)a["pitch"]);
                }
                r.Text($"Orbit camera yaw {orbit.Yaw:0.0}, pitch {orbit.Pitch:0.0}.");
            }
            else
            {
                r.Fail("No orbit camera.");
            }
            yield return null;
            yield break;
        }
        if (mode != "free")
        {
            r.Fail("mode must be free, orbit or release.");
            yield break;
        }
        Vector3 position = DevJson.ReadVector(a["position"]) ?? (DevCamera.Free ? DevCamera.Position : cam.transform.position);
        if ((bool?)a["relative_to_player"] == true && GameManager.LocalPlayerMovement != null)
        {
            position += GameManager.LocalPlayerMovement.transform.position;
        }
        Quaternion rotation;
        if (lookAt.HasValue)
        {
            rotation = Quaternion.LookRotation(lookAt.Value - position, Vector3.up);
        }
        else if (a["yaw"] != null || a["pitch"] != null)
        {
            rotation = Quaternion.Euler((float?)a["pitch"] ?? 0f, (float?)a["yaw"] ?? 0f, 0f);
        }
        else
        {
            rotation = DevCamera.Free ? DevCamera.Rotation : cam.transform.rotation;
        }
        DevCamera.SetFree(position, rotation, (float?)a["fov"]);
        yield return null;
        r.Text($"Free camera at {Fmt(position)} looking {Fmt(rotation * Vector3.forward)}. Use mode=release to give it back.");
    }

    private static IEnumerator Screenshot(JObject a, DevReply r)
    {
        int width = Mathf.Clamp((int?)a["width"] ?? 960, 160, 3840);
        int quality = Mathf.Clamp((int?)a["quality"] ?? 70, 10, 100);
        Vector3? position = DevJson.ReadVector(a["position"]);
        Texture2D shot;
        string caption;
        if (position.HasValue)
        {
            // Render a separate camera: no UI, doesn't disturb the game camera.
            Camera main = Camera.main;
            if (main == null)
            {
                r.Fail("No main camera.");
                yield break;
            }
            Vector3 look = DevJson.ReadVector(a["look_at"]) ?? (GameManager.LocalPlayerMovement != null ? GameManager.LocalPlayerMovement.transform.position + Vector3.up : position.Value + Vector3.forward);
            int height = Mathf.RoundToInt(width * 9f / 16f);
            var go = new GameObject("DevMcpShotCamera");
            Camera cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.enabled = false;
            cam.fieldOfView = (float?)a["fov"] ?? 50f;
            go.transform.SetPositionAndRotation(position.Value, Quaternion.LookRotation(look - position.Value, Vector3.up));
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            shot = ReadBack(rt, width, height);
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(go);
            caption = $"Rendered from {Fmt(position.Value)} looking at {Fmt(look)} (no UI).";
        }
        else
        {
            yield return new WaitForEndOfFrame();
            Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();
            int height = Mathf.RoundToInt(width * (float)full.height / full.width);
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(full, rt);
            shot = ReadBack(rt, width, height);
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(full);
            Transform camTransform = Camera.main != null ? Camera.main.transform : null;
            caption = camTransform != null ? $"Game view; camera at {Fmt(camTransform.position)} looking {Fmt(camTransform.forward)}." : "Game view.";
        }
        byte[] jpg = shot.EncodeToJPG(quality);
        Object.Destroy(shot);
        if ((bool?)a["save"] == true)
        {
            string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "shots");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"mcp_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
            File.WriteAllBytes(file, jpg);
            caption += " Saved to " + file;
        }
        r.Image(jpg, "image/jpeg");
        r.Text(caption);
    }

    private static Texture2D ReadBack(RenderTexture rt, int width, int height)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        return tex;
    }

    // ---- items ----

    private static string ItemName(ItemType type)
    {
        if (type == ItemType.None)
        {
            return "none";
        }
        if (ItemCatalog.TryGet(type, out CustomItem custom))
        {
            return custom.MenuName;
        }
        return type.ToString();
    }

    private static string Squash(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Item from an enum name, pack item name, or id ("RocketLauncher", "rocket launcher", "Portal Gun", "101").</summary>
    private static bool TryParseItem(JToken token, out ItemType type, out string error)
    {
        error = null;
        type = ItemType.None;
        string text = (string)token;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Give an item (name or id; list_items shows them).";
            return false;
        }
        if (int.TryParse(text, out int id))
        {
            type = (ItemType)id;
            return true;
        }
        string key = Squash(text);
        foreach (CustomItem item in ItemCatalog.All)
        {
            if (Squash(item.MenuName) == key || Squash(item.DisplayName) == key)
            {
                type = item.Type;
                return true;
            }
        }
        foreach (ItemType t in (ItemType[])Enum.GetValues(typeof(ItemType)))
        {
            if (Squash(t.ToString()) == key)
            {
                type = t;
                return true;
            }
        }
        foreach (CustomItem item in ItemCatalog.All)
        {
            if (Squash(item.MenuName).Contains(key))
            {
                type = item.Type;
                return true;
            }
        }
        error = $"Unknown item '{text}'. Use list_items.";
        return false;
    }

    private static IEnumerator ListItems(DevReply r)
    {
        var lines = new List<string>();
        foreach (ItemType t in (ItemType[])Enum.GetValues(typeof(ItemType)))
        {
            if (t != ItemType.None && !ItemCatalog.IsCustom(t))
            {
                lines.Add($"{(int)t}: {t} (game)");
            }
        }
        foreach (CustomItem item in ItemCatalog.All)
        {
            lines.Add($"{item.Id}: {item.MenuName} (pack, {item.Uses} uses)");
        }
        r.Text(string.Join("\n", lines));
        yield break;
    }

    private static IEnumerator GiveItem(JObject a, DevReply r)
    {
        PlayerInventory inventory = GameManager.LocalPlayerInventory;
        if (inventory == null)
        {
            r.Fail("No local player.");
            yield break;
        }
        if (!RequireHost(r))
        {
            yield break;
        }
        if (!TryParseItem(a["item"], out ItemType type, out string error))
        {
            r.Fail(error);
            yield break;
        }
        if (GameManager.AllItems == null || !GameManager.AllItems.TryGetItemData(type, out ItemData data))
        {
            r.Fail($"{type} isn't loaded.");
            yield break;
        }
        int uses = (int?)a["uses"] ?? (ItemCatalog.TryGet(type, out CustomItem custom) ? custom.Uses : data.MaxUses);
        if (!inventory.ServerTryAddItem(type, uses))
        {
            r.Fail("Inventory full.");
            yield break;
        }
        yield return null;
        yield return null;
        int slot = -1;
        for (int i = 0; i < inventory.slots.Count; i++)
        {
            if (inventory.slots[i].itemType == type)
            {
                slot = i;
            }
        }
        string message = $"Gave {ItemName(type)} ({uses} uses), slot {slot}.";
        if ((bool?)a["select"] ?? true)
        {
            bool selected = slot >= 0 && Select(inventory, slot);
            yield return null;
            message += selected ? " Selected it." : " Couldn't select it.";
        }
        r.Text(message);
    }

    private static IEnumerator SelectSlot(JObject a, DevReply r)
    {
        PlayerInventory inventory = GameManager.LocalPlayerInventory;
        if (inventory == null)
        {
            r.Fail("No local player.");
            yield break;
        }
        int index = (int?)a["index"] ?? 0;
        bool ok = Select(inventory, index);
        yield return null;
        r.Text(ok ? $"Selected slot {index}: {ItemName(inventory.GetEffectivelyEquippedItem())}." : $"Couldn't select slot {index}.");
    }

    /// <summary>Selects an inventory slot the way the number keys do (hotbar 0 is the club, so slot i is hotkey i + 1).</summary>
    private static bool Select(PlayerInventory inventory, int slot)
    {
        if (SingletonBehaviour<Hotkeys>.HasInstance)
        {
            return Hotkeys.Select(slot + 1);
        }
        return inventory.TrySelectItemSlot(slot, false);
    }

    private static IEnumerator SpawnItem(JObject a, DevReply r)
    {
        if (!RequireHost(r))
        {
            yield break;
        }
        if (!TryParseItem(a["item"], out ItemType type, out string error))
        {
            r.Fail(error);
            yield break;
        }
        Vector3? where = DevJson.ReadVector(a["position"]);
        if (!where.HasValue)
        {
            PlayerMovement player = RequirePlayer(r);
            if (player == null)
            {
                yield break;
            }
            where = player.transform.position + player.transform.forward * 2f;
        }
        Vector3 position = where.Value;
        if (a["position"] is not JArray { Count: 3 } || (bool?)a["ground"] == true)
        {
            Ground(position, out position);
            position += Vector3.up * 0.3f;
        }
        int uses = (int?)a["uses"] ?? (ItemCatalog.TryGet(type, out CustomItem custom) ? custom.Uses : GameManager.AllItems.TryGetItemData(type, out ItemData data) ? data.MaxUses : 1);
        CourseManager.ServerSpawnItem(type, uses, position, Quaternion.identity, Vector3.zero, Vector3.zero, default, null);
        yield return null;
        r.Text($"Spawned a {ItemName(type)} pickup at {Fmt(position)}.");
    }

    private static IEnumerator UseItem(JObject a, DevReply r)
    {
        PlayerInventory inventory = GameManager.LocalPlayerInventory;
        if (inventory == null)
        {
            r.Fail("No local player.");
            yield break;
        }
        if (a["slot"] != null)
        {
            Select(inventory, (int)a["slot"]);
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }
        }
        if (DevJson.ReadVector(a["aim_at"]) is Vector3 target)
        {
            DevCamera.Release();
            DevCamera.AimOrbitAt(target, out _);
            yield return null;
        }
        string button = ((string)a["button"] ?? "primary").ToLowerInvariant();
        bool aim = (bool?)a["aim"] ?? false;
        float holdSeconds = ((float?)a["hold_ms"] ?? 100f) / 1000f;
        ItemType before = inventory.GetEffectivelyEquippedItem();
        ButtonControl secondary = DevInput.Find(null, "Gameplay/Aim", out _) ?? DevInput.Find("RMB", null, out _);
        ButtonControl primary = DevInput.Find(null, "Gameplay/Swing", out string error) ?? DevInput.Find("LMB", null, out _);
        if (aim || button == "secondary")
        {
            DevInput.Set(secondary, true);
            float aimSeconds = ((float?)a["aim_ms"] ?? 300f) / 1000f;
            yield return WaitSeconds(aim ? aimSeconds : holdSeconds);
        }
        if (button == "primary")
        {
            DevInput.Set(primary, true);
            yield return WaitSeconds(holdSeconds);
            DevInput.Set(primary, false);
        }
        if (aim || button == "secondary")
        {
            yield return null;
            DevInput.Set(secondary, false);
        }
        // Used-up items leave the inventory a few frames later.
        yield return WaitSeconds(0.15f);
        r.Text($"Used {ItemName(before)} ({button}{(aim ? " while aiming" : "")}, held {holdSeconds * 1000f:0} ms). Now equipped: {ItemName(inventory.GetEffectivelyEquippedItem())}.");
    }

    private static IEnumerator Interact(JObject a, DevReply r)
    {
        PlayerInteractableTargeter targeter = GameManager.LocalPlayerInteractableTargeter;
        if (targeter == null)
        {
            r.Fail("No local player.");
            yield break;
        }
        string target = targeter.HasTarget ? targeter.CurrentTargetEntity.name : null;
        string cartBefore = CartName();
        // In a cart, E is the cart map's Exit action (Gameplay/Interact is disabled there but bound to the same key).
        ButtonControl button = DevInput.Find(null, cartBefore != null ? "Golf Cart Shared/Exit" : "Gameplay/Interact", out string error)
                               ?? DevInput.Find(null, "Gameplay/Interact", out error);
        if (button == null)
        {
            r.Fail(error);
            yield break;
        }
        DevInput.Set(button, true);
        yield return WaitSeconds(((float?)a["hold_ms"] ?? 100f) / 1000f);
        DevInput.Set(button, false);
        yield return WaitSeconds(0.2f);
        string cartAfter = CartName();
        if (cartBefore != null)
        {
            r.Text(cartAfter == null ? $"Exited {cartBefore}." : $"Pressed exit, but still seated in {cartAfter}.");
        }
        else if (cartAfter != null)
        {
            r.Text($"Entered {cartAfter} (input mode {InputManager.CurrentMode}).");
        }
        else
        {
            r.Text(target != null ? $"Interacted with {target}." : "Pressed interact, but nothing was targeted (walk/teleport next to it and face it).");
        }
    }

    /// <summary>The cart the local player sits in, or null.</summary>
    private static string CartName()
    {
        if (GameManager.LocalPlayerInfo == null)
        {
            return null;
        }
        GolfCartSeat seat = GameManager.LocalPlayerInfo.ActiveGolfCartSeat;
        return seat.golfCart != null ? seat.golfCart.name : null;
    }

    // ---- input ----

    private static IEnumerator Input(JObject a, DevReply r)
    {
        string mode = ((string)a["mode"] ?? "press").ToLowerInvariant();
        if (mode == "release_all")
        {
            DevInput.ReleaseAll();
            yield return null;
            r.Text("Released all held inputs.");
            yield break;
        }
        ButtonControl button = DevInput.Find((string)a["key"], (string)a["action"], out string error);
        if (button == null)
        {
            r.Fail(error ?? "No such input device.");
            yield break;
        }
        switch (mode)
        {
            case "down":
                DevInput.Set(button, true);
                yield return null;
                r.Text($"Holding {button.path} (release with mode=up).");
                break;
            case "up":
                DevInput.Set(button, false);
                yield return null;
                r.Text($"Released {button.path}.");
                break;
            default:
                DevInput.Set(button, true);
                yield return null; // at least one frame down
                yield return WaitSeconds(((float?)a["hold_ms"] ?? 80f) / 1000f);
                DevInput.Set(button, false);
                yield return null;
                r.Text($"Pressed {button.path}.");
                break;
        }
    }

    // ---- world ----

    private static Dictionary<string, GameObject> NetworkPrefabs()
    {
        var prefabs = new Dictionary<string, GameObject>();
        if (NetworkManager.singleton != null)
        {
            if (NetworkManager.singleton.playerPrefab != null)
            {
                prefabs[NetworkManager.singleton.playerPrefab.name] = NetworkManager.singleton.playerPrefab;
            }
            foreach (GameObject prefab in NetworkManager.singleton.spawnPrefabs)
            {
                if (prefab != null)
                {
                    prefabs[prefab.name] = prefab;
                }
            }
        }
        foreach (GameObject prefab in NetworkClient.prefabs.Values)
        {
            if (prefab != null)
            {
                prefabs[prefab.name] = prefab;
            }
        }
        return prefabs;
    }

    private static IEnumerator ListPrefabs(JObject a, DevReply r)
    {
        string filter = ((string)a["filter"] ?? "").ToLowerInvariant();
        List<string> names = NetworkPrefabs().Keys.Where(n => n.ToLowerInvariant().Contains(filter)).OrderBy(n => n).ToList();
        r.Text(names.Count == 0 ? "No network prefabs match." : string.Join("\n", names));
        yield break;
    }

    private static IEnumerator SpawnPrefab(JObject a, DevReply r)
    {
        if (!RequireHost(r))
        {
            yield break;
        }
        string name = (string)a["name"] ?? "";
        Dictionary<string, GameObject> prefabs = NetworkPrefabs();
        GameObject prefab = prefabs.TryGetValue(name, out GameObject exact) ? exact
            : prefabs.Values.FirstOrDefault(p => p.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (prefab == null)
        {
            r.Fail($"No network prefab '{name}'. Use list_prefabs.");
            yield break;
        }
        Vector3? where = DevJson.ReadVector(a["position"]);
        PlayerMovement player = GameManager.LocalPlayerMovement;
        if (!where.HasValue && player == null)
        {
            r.Fail("Give a position (no local player to place it in front of).");
            yield break;
        }
        Vector3 position = where ?? player.transform.position + player.transform.forward * 4f;
        if (!where.HasValue || (bool?)a["ground"] == true)
        {
            Ground(position, out position);
        }
        Quaternion rotation = Quaternion.Euler(0f, (float?)a["yaw"] ?? (player != null ? player.transform.eulerAngles.y : 0f), 0f);
        GameObject instance = Object.Instantiate(prefab, position, rotation);
        if (instance.GetComponent<NetworkIdentity>() != null)
        {
            NetworkServer.Spawn(instance);
        }
        yield return null;
        r.Text($"Spawned {prefab.name} at {Fmt(position)} (instance '{instance.name}', id {instance.GetInstanceID()}). " +
               "Note: spawned raw, without whatever setup the game normally does for it.");
    }

    private static IEnumerator FindObjects(JObject a, DevReply r)
    {
        string name = ((string)a["name"] ?? "").ToLowerInvariant();
        string componentName = (string)a["component"];
        Vector3? near = DevJson.ReadVector(a["near"]);
        if (!near.HasValue && (bool?)a["near_player"] != false && GameManager.LocalPlayerMovement != null && a["radius"] != null)
        {
            near = GameManager.LocalPlayerMovement.transform.position;
        }
        float radius = (float?)a["radius"] ?? float.PositiveInfinity;
        int limit = (int?)a["limit"] ?? 30;
        bool showComponents = (bool?)a["components"] ?? componentName != null;

        IEnumerable<Component> candidates;
        if (componentName != null)
        {
            Type type = AccessTools.TypeByName(componentName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
            {
                r.Fail($"No component type '{componentName}'.");
                yield break;
            }
            candidates = Object.FindObjectsByType(type, FindObjectsSortMode.None).Cast<Component>();
        }
        else
        {
            candidates = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        }
        var matches = new List<(float distance, Component c)>();
        foreach (Component c in candidates)
        {
            if (c == null || (name.Length > 0 && !c.name.ToLowerInvariant().Contains(name)))
            {
                continue;
            }
            float distance = near.HasValue ? Vector3.Distance(near.Value, c.transform.position) : 0f;
            if (distance <= radius)
            {
                matches.Add((distance, c));
            }
        }
        var lines = new List<string> { $"{matches.Count} match(es){(matches.Count > limit ? $", showing {limit}" : "")}:" };
        foreach ((float distance, Component c) in matches.OrderBy(m => m.distance).Take(limit))
        {
            string line = $"{ObjectPath(c.transform)} (id {c.gameObject.GetInstanceID()}) at {Fmt(c.transform.position)}";
            if (near.HasValue)
            {
                line += $" ({distance:0.0} m)";
            }
            if (!c.gameObject.activeInHierarchy)
            {
                line += " [inactive]";
            }
            if (showComponents)
            {
                line += " [" + DevInspect.ComponentSummary(c.gameObject) + "]";
            }
            lines.Add(line);
        }
        r.Text(string.Join("\n", lines));
        yield break;
    }

    private static string ObjectPath(Transform t)
    {
        string path = t.name;
        int depth = 0;
        for (Transform parent = t.parent; parent != null && depth < 3; parent = parent.parent, depth++)
        {
            path = parent.name + "/" + path;
        }
        return path;
    }

    // ---- menus ----

    private static List<(Selectable selectable, string label)> VisibleSelectables()
    {
        var list = new List<(Selectable, string)>();
        foreach (Selectable s in Selectable.allSelectablesArray)
        {
            if (s == null || !s.isActiveAndEnabled)
            {
                continue;
            }
            string label = s.GetComponentInChildren<TMP_Text>()?.text ?? s.GetComponentInChildren<Text>()?.text ?? "";
            list.Add((s, label.Trim().Replace("\n", " ")));
        }
        return list;
    }

    private static IEnumerator Ui(JObject a, DevReply r)
    {
        string action = ((string)a["action"] ?? "list").ToLowerInvariant();
        List<(Selectable selectable, string label)> items = VisibleSelectables();
        if (action == "list")
        {
            var lines = new List<string>();
            for (int i = 0; i < items.Count; i++)
            {
                Selectable s = items[i].selectable;
                lines.Add($"{i}: [{s.GetType().Name}] \"{items[i].label}\" ({s.name}){(s.interactable ? "" : " not interactable")}");
            }
            r.Text(lines.Count == 0 ? "No visible UI controls." : string.Join("\n", lines));
            yield break;
        }
        if (action != "click")
        {
            r.Fail("action must be list or click.");
            yield break;
        }
        (Selectable selectable, string label) chosen = default;
        if (a["index"] != null)
        {
            int index = (int)a["index"];
            if (index >= 0 && index < items.Count)
            {
                chosen = items[index];
            }
        }
        else
        {
            string text = ((string)a["text"] ?? "").ToLowerInvariant();
            chosen = items.FirstOrDefault(i => i.label.ToLowerInvariant() == text);
            if (chosen.selectable == null)
            {
                chosen = items.FirstOrDefault(i => i.label.ToLowerInvariant().Contains(text) || i.selectable.name.ToLowerInvariant().Contains(text));
            }
        }
        if (chosen.selectable == null)
        {
            r.Fail("No such control. Use ui with action=list.");
            yield break;
        }
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(chosen.selectable.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(chosen.selectable.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        yield return null;
        r.Text($"Clicked \"{chosen.label}\" ({chosen.selectable.name}).");
    }

    private static IEnumerator StartGame(DevReply r)
    {
        if (GameManager.LocalPlayerMovement != null)
        {
            r.Text("Already in a game.");
            yield break;
        }
        MainMenu menu = Object.FindFirstObjectByType<MainMenu>();
        if (menu == null)
        {
            r.Fail("Not on the main menu.");
            yield break;
        }
        menu.StartHost();
        // Wait for the Driving Range and the local player (up to 60 s).
        float until = Time.realtimeSinceStartup + 60f;
        while (GameManager.LocalPlayerMovement == null && Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
        if (GameManager.LocalPlayerMovement == null)
        {
            r.Fail("Started hosting, but no local player after 60 s.");
            yield break;
        }
        yield return WaitSeconds(2f);
        r.Text($"In game: {SceneManager.GetActiveScene().name}, player at {Fmt(GameManager.LocalPlayerMovement.transform.position)}.");
    }

    // ---- timing ----

    private static IEnumerator WaitSeconds(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
        {
            yield return null;
        }
    }

    private static IEnumerator Wait(JObject a, DevReply r)
    {
        if (a["frames"] != null)
        {
            int frames = (int)a["frames"];
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }
            r.Text($"Waited {frames} frames.");
            yield break;
        }
        float ms = (float?)a["ms"] ?? 500f;
        yield return WaitSeconds(ms / 1000f);
        r.Text($"Waited {ms:0} ms.");
    }

    private static IEnumerator Sequence(JObject a, DevReply r)
    {
        if (a["steps"] is not JArray steps)
        {
            r.Fail("steps must be an array of {tool, args, wait_ms}.");
            yield break;
        }
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i] is not JObject step)
            {
                continue;
            }
            string tool = (string)step["tool"] ?? "wait";
            if (tool == "sequence")
            {
                r.Text($"[{i} {tool}] skipped: no nested sequences.");
                continue;
            }
            var stepReply = new DevReply();
            IEnumerator routine = DevServer.Step(tool, step["args"] as JObject ?? new JObject(), stepReply);
            while (routine.MoveNext())
            {
                yield return routine.Current;
            }
            stepReply.AppendTo(r, $"[{i} {tool}] ");
            if (stepReply.Error != null && (bool?)a["stop_on_error"] != false)
            {
                r.Fail($"Step {i} ({tool}) failed: {stepReply.Error}");
                yield break;
            }
            if (step["wait_ms"] != null)
            {
                yield return WaitSeconds((float)step["wait_ms"] / 1000f);
            }
        }
    }

    private static string Fmt(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";
}
#endif

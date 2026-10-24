using System.Text.Json.Nodes;

/// <summary>The MCP tool list. Game tools are implemented in the plugin (src/DevMcp/DevCommands.cs) under the same names.</summary>
internal static class Tools
{
    private const string Vec = """{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3,"description":"[x, y, z] in world meters (y is up)"}""";

    private static readonly (string name, bool game, string description, string schema)[] All =
    [
        ("game_status", false,
            "Is the game running, are mods enabled, and is the DevTools dev server answering? Also returns state if it is. Works without the game.",
            """{"type":"object","properties":{}}"""),
        ("read_log", false,
            "Tail of BepInEx\\LogOutput.log (plugin and game log). Works without the game.",
            """{"type":"object","properties":{"lines":{"type":"integer","description":"max lines returned (default 60)"},"contains":{"type":"string","description":"only lines containing this (case-insensitive)"},"new_only":{"type":"boolean","description":"only lines written since the previous read_log call"}}}"""),
        ("launch_game", false,
            "Launches the game through Steam and waits for the dev server. ONLY call this when the user has said it's OK to launch the game (they may be playing something else).",
            """{"type":"object","properties":{"wait_seconds":{"type":"integer","description":"default 90"}}}"""),

        ("state", true,
            "Snapshot: scene, host/driving range, input mode, camera (pose, orbit yaw/pitch, what the crosshair hits), local player (position, yaw, velocity, inventory slots, equipped item, interact target), other players.",
            """{"type":"object","properties":{}}"""),
        ("spawn_dummy", true,
            "Host only: spawns a dummy player (a stand-in for another person) at position, default 3 m in front of you facing you. Nobody controls it; it stands where it is. Lists in state.other_players with dummy=true.",
            """{"type":"object","properties":{"position":VEC,"yaw":{"type":"number"},"name":{"type":"string"}}}"""),
        ("place_portal", true,
            "Places your portal (color 0 orange, 1 blue) at position on a surface whose outward normal is normal, e.g. a wall.",
            """{"type":"object","properties":{"color":{"type":"integer"},"position":VEC,"normal":VEC},"required":["position","normal"]}"""),
        ("damage_me", true,
            "Host only: an ownerless rocket-sized explosion at offset (world-space, from your chest; default 1.5 m ahead) to test block/knockout logic on yourself. scale default 0.5.",
            """{"type":"object","properties":{"offset":VEC,"scale":{"type":"number"}}}"""),
        ("remove_dummies", true,
            "Removes every dummy player.",
            """{"type":"object","properties":{}}"""),
        ("held_report", true,
            "Where the held item sits relative to the hand (Tool1 space): the model's bounds center and the finger bones' centroid, to calibrate held offsets.",
            """{"type":"object","properties":{}}"""),
        ("start_game", true,
            "From the main menu, host a game (lands in the Driving Range, like the Start game button) and wait for the local player.",
            """{"type":"object","properties":{}}"""),
        ("teleport", true,
            "Moves the local player. Omit y to drop onto the ground at x/z. relative=true adds the offset to the current position.",
            """{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"},"position":VEC,"yaw":{"type":"number","description":"facing in degrees, 0 = +Z"},"relative":{"type":"boolean"},"reset_state":{"type":"boolean","description":"default true"}}}"""),
        ("camera", true,
            "mode=free: park the game camera at position looking at look_at (or yaw/pitch), overriding the game until mode=release. mode=orbit: point the normal player camera (what you aim with) at look_at or set yaw/pitch (pitch positive = looking down). mode=release: give the camera back.",
            """{"type":"object","properties":{"mode":{"type":"string","enum":["free","orbit","release"]},"position":VEC,"relative_to_player":{"type":"boolean","description":"free mode: position is an offset from the player"},"look_at":VEC,"yaw":{"type":"number"},"pitch":{"type":"number"},"fov":{"type":"number"}},"required":["mode"]}"""),
        ("screenshot", true,
            "Captures the game view with UI (default; on the main menu it comes out as a white mask, use ui list there), or renders from an arbitrary position/look_at without UI and without moving the game camera. Returns a JPEG.",
            """{"type":"object","properties":{"width":{"type":"integer","description":"pixels, default 960"},"quality":{"type":"integer","description":"JPEG quality, default 70"},"position":VEC,"look_at":VEC,"fov":{"type":"number"},"save":{"type":"boolean","description":"also save to BepInEx\\shots"}}}"""),
        ("list_items", true,
            "All item ids and names (game items and pack items).",
            """{"type":"object","properties":{}}"""),
        ("give_item", true,
            "Host only: adds an item to the local player's inventory (and selects it unless select=false). item = name or id, e.g. \"Portal Gun\", \"RocketLauncher\", \"101\".",
            """{"type":"object","properties":{"item":{"type":"string"},"uses":{"type":"integer"},"select":{"type":"boolean"}},"required":["item"]}"""),
        ("select_slot", true,
            "Equips the inventory slot at index (0-based).",
            """{"type":"object","properties":{"index":{"type":"integer"}},"required":["index"]}"""),
        ("use_item", true,
            "Uses the equipped item with simulated input: button=primary clicks the use/swing action; button=secondary holds right mouse; aim=true holds right mouse (aim_ms first) while clicking. Optional slot to select first and aim_at to point the orbit camera first.",
            """{"type":"object","properties":{"slot":{"type":"integer"},"button":{"type":"string","enum":["primary","secondary"]},"aim":{"type":"boolean"},"aim_ms":{"type":"number"},"hold_ms":{"type":"number","description":"default 100"},"aim_at":VEC}}"""),
        ("interact", true,
            "Presses the interact action (pick up items, enter carts/buildings) and reports what was targeted. Stand next to and face the thing first.",
            """{"type":"object","properties":{"hold_ms":{"type":"number"}}}"""),
        ("input", true,
            "Simulated key/mouse button through the game's input system (works without window focus). key = Input System key name (W, Space, LeftShift, Digit2, E, F7, Escape) or LMB/RMB/MMB; or action = \"Map/Action\" from list_actions. mode press (default, hold_ms), down, up, release_all.",
            """{"type":"object","properties":{"key":{"type":"string"},"action":{"type":"string"},"mode":{"type":"string","enum":["press","down","up","release_all"]},"hold_ms":{"type":"number"}}}"""),
        ("list_actions", true,
            "The game's input actions by map with their keyboard/mouse bindings and whether each is enabled now.",
            """{"type":"object","properties":{}}"""),
        ("spawn_item", true,
            "Host only: spawns an item pickup on the ground (default 2 m in front of the player).",
            """{"type":"object","properties":{"item":{"type":"string"},"position":VEC,"ground":{"type":"boolean","description":"snap position to the ground"},"uses":{"type":"integer"}},"required":["item"]}"""),
        ("list_prefabs", true,
            "Network-spawnable prefab names (golf cart, item spawner, rockets...).",
            """{"type":"object","properties":{"filter":{"type":"string"}}}"""),
        ("spawn_prefab", true,
            "Host only: instantiates and network-spawns a prefab from list_prefabs (default 4 m in front of the player, on the ground). Spawned raw, without the game's usual setup.",
            """{"type":"object","properties":{"name":{"type":"string"},"position":VEC,"yaw":{"type":"number"},"ground":{"type":"boolean"}},"required":["name"]}"""),
        ("find_objects", true,
            "Scene objects by name substring and/or component type name, nearest first. radius limits to around near (default: the player).",
            """{"type":"object","properties":{"name":{"type":"string"},"component":{"type":"string","description":"type name, e.g. GolfCartInfo, ItemSpawner, PhysicalItem"},"near":VEC,"radius":{"type":"number"},"limit":{"type":"integer"},"components":{"type":"boolean","description":"list each object's components"}}}"""),
        ("ui", true,
            "Unity UI controls on screen: action=list, or action=click by text (label or object name) or index. Doesn't see the F7 IMGUI menu (use give_item instead).",
            """{"type":"object","properties":{"action":{"type":"string","enum":["list","click"]},"text":{"type":"string"},"index":{"type":"integer"}}}"""),
        ("inspect", true,
            "Looks inside a scene object: pick it by id (from find_objects) or name / a/b/c path suffix (nearest to near or the player). Without component: its components (with collider trigger/disabled counts and layer); children=true lists its children too. With component (type or base type name, e.g. Collider, Rigidbody, GolfCartInfo): every field and property value (members limits the list), on the object or with children=true its children. set + value writes a field/property first (numbers, bools, strings, enums, [x,y,z]).",
            """{"type":"object","properties":{"id":{"type":"integer"},"name":{"type":"string"},"near":VEC,"component":{"type":"string"},"children":{"type":"boolean"},"members":{"type":"array","items":{"type":"string"}},"set":{"type":"string"},"value":{}}}"""),
        ("time_scale", true,
            "Sets Time.timeScale (1 = normal; 0.2 for slow motion).",
            """{"type":"object","properties":{"value":{"type":"number"}},"required":["value"]}"""),
        ("wait", true,
            "Waits ms milliseconds (real time) or a number of frames inside the game.",
            """{"type":"object","properties":{"ms":{"type":"number"},"frames":{"type":"integer"}}}"""),
        ("sequence", true,
            "Runs several game tools back to back inside the game with exact timing (no round trips), e.g. hold RMB, click, then burst screenshots, then read_log. Each step: {tool, args, wait_ms (after the step)}. Stops at the first error unless stop_on_error=false.",
            """{"type":"object","properties":{"steps":{"type":"array","items":{"type":"object","properties":{"tool":{"type":"string"},"args":{"type":"object"},"wait_ms":{"type":"number"}},"required":["tool"]}},"stop_on_error":{"type":"boolean"}},"required":["steps"]}"""),
    ];

    public static JsonArray List()
    {
        var list = new JsonArray();
        foreach (var (name, _, description, schema) in All)
        {
            list.Add(new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["inputSchema"] = JsonNode.Parse(schema.Replace("VEC", Vec)),
            });
        }
        return list;
    }

    public static bool IsGameTool(string name) => All.Any(t => t.name == name && t.game);
}

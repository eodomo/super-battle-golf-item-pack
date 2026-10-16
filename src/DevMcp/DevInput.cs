#if DEVTOOLS
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace ItemPack.DevMcp;

/// <summary>
/// Simulated keyboard and mouse input, written into the real devices' state through the Input System's event queue,
/// so the game reacts exactly as to a player (bindings, action maps, input modes) without the window needing focus.
/// </summary>
internal static class DevInput
{
    private static readonly HashSet<ButtonControl> held = new();

    /// <summary>
    /// Finds a button from a key name ("E", "Space", "Digit2", "F7"), a mouse button ("mouse:left", "LMB", "RMB",
    /// "MMB") or a game action ("Gameplay/Swing"), using its keyboard/mouse binding.
    /// </summary>
    public static ButtonControl Find(string key, string action, out string error)
    {
        error = null;
        if (!string.IsNullOrEmpty(action))
        {
            return FindForAction(action, out error);
        }
        if (string.IsNullOrEmpty(key))
        {
            error = "Give a key or an action.";
            return null;
        }
        string k = key.Trim().ToLowerInvariant();
        Mouse mouse = Mouse.current;
        switch (k)
        {
            case "lmb":
            case "mouse:left":
            case "leftbutton":
                return mouse?.leftButton;
            case "rmb":
            case "mouse:right":
            case "rightbutton":
                return mouse?.rightButton;
            case "mmb":
            case "mouse:middle":
            case "middlebutton":
                return mouse?.middleButton;
        }
        if (Keyboard.current == null)
        {
            error = "No keyboard device.";
            return null;
        }
        if (k.Length == 1 && char.IsDigit(k[0]))
        {
            k = "digit" + k;
        }
        if (Enum.TryParse(k, true, out Key parsed) && parsed != Key.None)
        {
            return Keyboard.current[parsed];
        }
        error = $"Unknown key '{key}'. Use Input System key names (A, Space, LeftShift, Digit1, F7, Escape...) or LMB/RMB/MMB.";
        return null;
    }

    private static ButtonControl FindForAction(string name, out string error)
    {
        error = null;
        InputAction action = InputManager.Controls?.asset.FindAction(name);
        if (action == null)
        {
            error = $"No action '{name}'. Use list_actions to see them.";
            return null;
        }
        foreach (InputControl control in action.controls)
        {
            if (control is ButtonControl button && (control.device is Keyboard || control.device is Mouse))
            {
                return button;
            }
        }
        // The action's map may be disabled right now: resolve its keyboard/mouse bindings by hand.
        foreach (InputBinding binding in action.bindings)
        {
            if (binding.isComposite || binding.isPartOfComposite)
            {
                continue;
            }
            string path = binding.effectivePath;
            if (path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>"))
            {
                if (InputSystem.FindControl(path) is ButtonControl button)
                {
                    return button;
                }
            }
        }
        error = $"Action '{name}' has no keyboard/mouse button binding (composite actions like Move need keys, e.g. W).";
        return null;
    }

    public static void Set(ButtonControl button, bool down)
    {
        InputDevice device = button.device;
        using (StateEvent.From(device, out InputEventPtr eventPtr))
        {
            button.WriteValueIntoEvent(down ? 1f : 0f, eventPtr);
            InputSystem.QueueEvent(eventPtr);
        }
        if (down)
        {
            held.Add(button);
        }
        else
        {
            held.Remove(button);
        }
    }

    public static List<string> Held()
    {
        var list = new List<string>();
        foreach (ButtonControl button in held)
        {
            list.Add(button.path);
        }
        return list;
    }

    public static void ReleaseAll()
    {
        foreach (ButtonControl button in new List<ButtonControl>(held))
        {
            Set(button, false);
        }
    }

    /// <summary>Every action with its keyboard/mouse bindings, by map.</summary>
    public static Dictionary<string, List<string>> ListActions()
    {
        var result = new Dictionary<string, List<string>>();
        if (InputManager.Controls == null)
        {
            return result;
        }
        foreach (InputActionMap map in InputManager.Controls.asset.actionMaps)
        {
            var actions = new List<string>();
            foreach (InputAction action in map.actions)
            {
                var bindings = new List<string>();
                foreach (InputBinding binding in action.bindings)
                {
                    string path = binding.effectivePath;
                    if (path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>"))
                    {
                        bindings.Add(path);
                    }
                }
                actions.Add($"{map.name}/{action.name}{(action.enabled ? "" : " (disabled now)")}: {string.Join(", ", bindings)}");
            }
            result[map.name + (map.enabled ? "" : " (map disabled now)")] = actions;
        }
        return result;
    }
}
#endif

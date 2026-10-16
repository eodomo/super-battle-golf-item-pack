#if DEVTOOLS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ItemPack.DevMcp;

/// <summary>Dev MCP helpers for looking inside objects (component fields, collider setup) and the log.</summary>
internal static class DevInspect
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>"Transform, BoxCollider x10 (3 trigger, 1 disabled), Rigidbody".</summary>
    public static string ComponentSummary(GameObject go)
    {
        var parts = new List<string>();
        foreach (IGrouping<Type, Component> group in go.GetComponents<Component>().Where(c => c != null).GroupBy(c => c.GetType()))
        {
            string part = group.Key.Name + (group.Count() > 1 ? $" x{group.Count()}" : "");
            if (typeof(Collider).IsAssignableFrom(group.Key))
            {
                int triggers = group.Count(c => ((Collider)c).isTrigger);
                int disabled = group.Count(c => !((Collider)c).enabled);
                var notes = new List<string>();
                if (triggers > 0)
                {
                    notes.Add($"{triggers} trigger");
                }
                if (disabled > 0)
                {
                    notes.Add($"{disabled} disabled");
                }
                if (notes.Count > 0)
                {
                    part += $" ({string.Join(", ", notes)})";
                }
            }
            parts.Add(part);
        }
        return $"layer {LayerMask.LayerToName(go.layer)}: " + string.Join(", ", parts);
    }

    /// <summary>
    /// Finds objects (by instance id, or name/path substring, optionally near a point) and dumps one component's
    /// fields and properties, or all its colliders; can also set a simple field/property.
    /// </summary>
    public static void Inspect(JObject a, DevReply r)
    {
        GameObject go = FindObject(a, out string error);
        if (go == null)
        {
            r.Fail(error);
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"{go.name} (id {go.GetInstanceID()}) at {go.transform.position}, active {go.activeInHierarchy}, {ComponentSummary(go)}");
        string componentName = (string)a["component"];
        bool children = (bool?)a["children"] ?? false;
        if (componentName == null)
        {
            if (children)
            {
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true).Skip(1).Take(100))
                {
                    sb.AppendLine($"  {t.name}: {ComponentSummary(t.gameObject)}");
                }
            }
            sb.Append("Pass component (type name, e.g. Collider, Rigidbody, GolfCartInfo) to see its fields.");
            r.Text(sb.ToString());
            return;
        }
        Component[] components = (children ? go.GetComponentsInChildren<Component>(true) : go.GetComponents<Component>())
            .Where(c => c != null && TypeMatches(c.GetType(), componentName)).Take(30).ToArray();
        if (components.Length == 0)
        {
            r.Fail($"{go.name} has no {componentName}{(children ? " in its children" : "")}.");
            return;
        }
        string setMember = (string)a["set"];
        foreach (Component c in components)
        {
            if (setMember != null)
            {
                sb.AppendLine(SetMember(c, setMember, a["value"]));
            }
            sb.AppendLine($"--- {c.GetType().Name} on {c.gameObject.name}");
            var only = (a["members"] as JArray)?.Select(m => (string)m).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, object value) in ReadMembers(c, only))
            {
                sb.AppendLine($"  {name} = {Describe(value)}");
            }
        }
        r.Text(sb.ToString());
    }

    private static bool TypeMatches(Type type, string name)
    {
        for (Type t = type; t != null && t != typeof(Component); t = t.BaseType)
        {
            if (t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static GameObject FindObject(JObject a, out string error)
    {
        error = null;
        if (a["id"] != null)
        {
            int id = (int)a["id"];
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.gameObject.GetInstanceID() == id || t.GetInstanceID() == id)
                {
                    return t.gameObject;
                }
            }
            error = $"No object with id {id}.";
            return null;
        }
        string name = (string)a["name"];
        if (string.IsNullOrEmpty(name))
        {
            error = "Give name (substring of the object name, or a/b/c path suffix) or id.";
            return null;
        }
        Vector3 near = DevJson.ReadVector(a["near"])
                       ?? (GameManager.LocalPlayerMovement != null ? GameManager.LocalPlayerMovement.transform.position : Vector3.zero);
        GameObject best = null;
        float bestDistance = float.MaxValue;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!PathEndsWith(t, name) && t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            float distance = Vector3.Distance(near, t.position) + (t.name.Equals(name, StringComparison.OrdinalIgnoreCase) ? 0f : 10000f);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = t.gameObject;
            }
        }
        if (best == null)
        {
            error = $"No object named like '{name}'.";
        }
        return best;
    }

    private static bool PathEndsWith(Transform t, string path)
    {
        string[] parts = path.Split('/');
        for (int i = parts.Length - 1; i >= 0; i--, t = t.parent)
        {
            if (t == null || !t.name.Equals(parts[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static IEnumerable<(string, object)> ReadMembers(Component c, HashSet<string> only)
    {
        var seen = new HashSet<string>();
        for (Type t = c.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component) && t != typeof(Object); t = t.BaseType)
        {
            foreach (FieldInfo field in t.GetFields(Members | BindingFlags.DeclaredOnly))
            {
                if (field.Name.Contains("<") || !seen.Add(field.Name) || (only != null && !only.Contains(field.Name)))
                {
                    continue;
                }
                yield return (field.Name, Safe(() => field.GetValue(c)));
            }
            foreach (PropertyInfo property in t.GetProperties(Members | BindingFlags.DeclaredOnly))
            {
                if (property.GetIndexParameters().Length > 0 || property.GetGetMethod(true) == null || !seen.Add(property.Name)
                    || (only != null && !only.Contains(property.Name)))
                {
                    continue;
                }
                // Skip Unity properties that allocate or warn (mesh/material instancing).
                if (only == null && (property.Name == "mesh" || property.Name == "material" || property.Name == "materials"))
                {
                    continue;
                }
                yield return (property.Name, Safe(() => property.GetValue(c)));
            }
        }
        if (c is Behaviour behaviour && (only == null || only.Contains("enabled")))
        {
            yield return ("enabled", behaviour.enabled);
        }
    }

    private static object Safe(Func<object> read)
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            return "<" + (e.InnerException ?? e).GetType().Name + ">";
        }
    }

    private static string Describe(object value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string s:
                return "\"" + s + "\"";
            case Object unityObject:
                return unityObject == null ? "null (destroyed)" : $"{unityObject.GetType().Name} '{unityObject.name}'";
            case IDictionary dictionary:
                return $"{value.GetType().Name} ({dictionary.Count} entries)";
            case ICollection collection when value is not string:
                var items = collection.Cast<object>().Take(8).Select(Describe);
                return $"[{collection.Count}] {{{string.Join(", ", items)}{(collection.Count > 8 ? ", ..." : "")}}}";
            default:
                return value.ToString();
        }
    }

    private static string SetMember(Component c, string name, JToken value)
    {
        Type type = c.GetType();
        FieldInfo field = AccessTools.Field(type, name);
        PropertyInfo property = field == null ? AccessTools.Property(type, name) : null;
        Type valueType = field?.FieldType ?? property?.PropertyType;
        if (valueType == null)
        {
            return $"No member '{name}' on {type.Name}.";
        }
        try
        {
            object converted;
            if (valueType == typeof(Vector3))
            {
                converted = DevJson.ReadVector(value) ?? throw new FormatException("expected [x, y, z]");
            }
            else if (valueType.IsEnum)
            {
                converted = Enum.Parse(valueType, (string)value, true);
            }
            else
            {
                converted = value.ToObject(valueType);
            }
            if (field != null)
            {
                field.SetValue(c, converted);
            }
            else
            {
                property.SetValue(c, converted);
            }
            return $"Set {type.Name}.{name} = {Describe(converted)} on {c.gameObject.name}.";
        }
        catch (Exception e)
        {
            return $"Couldn't set {name}: {(e.InnerException ?? e).Message}";
        }
    }

    // ---- log ----

    private static long logOffset = -1;

    /// <summary>The same as the MCP side's read_log, but runnable as a sequence step.</summary>
    public static void ReadLog(JObject a, DevReply r)
    {
        string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
        int maxLines = (int?)a["lines"] ?? 60;
        string contains = (string)a["contains"];
        bool newOnly = (bool?)a["new_only"] ?? false;
        string text;
        long start = 0;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (newOnly && logOffset >= 0 && logOffset <= stream.Length)
            {
                start = logOffset;
            }
            stream.Seek(start, SeekOrigin.Begin);
            text = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
            logOffset = stream.Length;
        }
        IEnumerable<string> lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);
        if (!string.IsNullOrEmpty(contains))
        {
            lines = lines.Where(l => l.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        List<string> list = lines.ToList();
        List<string> tail = list.Skip(Math.Max(0, list.Count - maxLines)).ToList();
        r.Text($"{tail.Count} of {list.Count} matching log line(s){(newOnly && start > 0 ? " since the last in-game read" : "")}:\n" + string.Join("\n", tail));
    }
}
#endif

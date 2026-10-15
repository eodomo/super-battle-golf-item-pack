namespace ItemPack;

/// <summary>
/// Switches the local player's gameplay controls off (golfer can't move, swing or use items) while any pack
/// feature needs it, e.g. while flying a predator missile or being made to dance. Counted, so overlapping
/// users don't turn controls back on for each other. The mouse cursor stays locked to the game.
/// </summary>
internal static class InputLock
{
    private static int holders;

    public static bool IsLocked => holders > 0;

    public static void Acquire()
    {
        if (holders++ == 0)
        {
            InputManager.EnableMode(InputMode.ForceDisabled);
            InputManager.SetCursorForceLocked(true);
        }
    }

    public static void Release()
    {
        if (holders == 0)
        {
            return;
        }
        if (--holders == 0)
        {
            InputManager.SetCursorForceLocked(false);
            InputManager.DisableMode(InputMode.ForceDisabled);
        }
    }
}

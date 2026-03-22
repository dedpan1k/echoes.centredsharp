using Microsoft.Xna.Framework.Input;

namespace CentrED;

/// <summary>
/// Tracks keyboard state and resolves editor actions to configured shortcuts.
/// </summary>
public class Keymap
{
    // Modifier keys are treated specially by some UI code when building or
    // sorting shortcuts, so keep the canonical list in one place.
    private static readonly Keys[] FunctionKeys =
        [Keys.LeftControl, Keys.LeftAlt, Keys.LeftShift, Keys.RightControl, Keys.RightAlt, Keys.RightShift];
    
    // Keep both the current and previous keyboard snapshots so the helper
    // methods can answer edge-triggered questions like "pressed this frame".
    private KeyboardState currentState;
    private KeyboardState previousState;

    /// <summary>
    /// Represents an empty shortcut binding for actions with no assigned keys.
    /// </summary>
    // Shared empty binding used for actions that intentionally have no shortcut.
    public static readonly Keys[] NotAssigned = [];

    /// <summary>
    /// Action identifier for moving the camera or selection up.
    /// </summary>
    // Action identifiers persisted in Config.Instance.Keymap.
    public const string MoveUp = "move_up";

    /// <summary>
    /// Action identifier for moving the camera or selection down.
    /// </summary>
    public const string MoveDown = "move_down";

    /// <summary>
    /// Action identifier for moving the camera or selection left.
    /// </summary>
    public const string MoveLeft = "move_left";

    /// <summary>
    /// Action identifier for moving the camera or selection right.
    /// </summary>
    public const string MoveRight = "move_right";

    /// <summary>
    /// Action identifier for toggling animated statics.
    /// </summary>
    public const string ToggleAnimatedStatics = "toggle_animated_statics";

    /// <summary>
    /// Action identifier for toggling the minimap.
    /// </summary>
    public const string Minimap = "minimap";

    /// <summary>
    /// Advances the stored keyboard history with a new snapshot.
    /// </summary>
    /// <param name="newState">The latest keyboard state for the current frame.</param>
    public void Update(KeyboardState newState)
    {
        // Advance the input history once per frame before any queries are made.
        previousState = currentState;
        currentState = newState;
    }

    /// <summary>
    /// Determines whether the specified key is currently held down.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><c>true</c> if the key is down; otherwise, <c>false</c>.</returns>
    public bool IsKeyDown(Keys key)
    {
        return currentState.IsKeyDown(key);
    }

    /// <summary>
    /// Determines whether the specified key is currently up.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><c>true</c> if the key is up; otherwise, <c>false</c>.</returns>
    public bool IsKeyUp(Keys key)
    {
        return currentState.IsKeyUp(key);
    }
    
    /// <summary>
    /// Determines whether the specified key was pressed on the current frame.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><c>true</c> if the key transitioned from up to down this frame; otherwise, <c>false</c>.</returns>
    public bool IsKeyPressed(Keys key)
    {
        // A key counts as pressed only on the transition from up to down.
        return currentState.IsKeyDown(key) && previousState.IsKeyUp(key);
    }

    /// <summary>
    /// Determines whether the specified key was released on the current frame.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><c>true</c> if the key transitioned from down to up this frame; otherwise, <c>false</c>.</returns>
    public bool IsKeyReleased(Keys key)
    {
        // Likewise, released is the transition from down to up.
        return currentState.IsKeyUp(key) && previousState.IsKeyDown(key);
    }
    
    /// <summary>
    /// Gets all keys that are currently held down.
    /// </summary>
    /// <returns>An array containing the currently pressed keys.</returns>
    public Keys[] GetKeysDown()
    {
        return currentState.GetPressedKeys();
    }

    /// <summary>
    /// Gets keys that became pressed on the current frame.
    /// </summary>
    /// <returns>An array containing keys newly pressed this frame.</returns>
    public Keys[] GetKeysPressed()
    {
        // Compare the two snapshots so callers only see keys that became active
        // this frame, not every key currently being held.
        return currentState.GetPressedKeys().Except(previousState.GetPressedKeys()).ToArray();
    }

    /// <summary>
    /// Gets keys that were released on the current frame.
    /// </summary>
    /// <returns>An array containing keys released this frame.</returns>
    public Keys[] GetKeysReleased()
    {
        // The inverse comparison returns keys that were let go this frame.
        return previousState.GetPressedKeys().Except(currentState.GetPressedKeys()).ToArray();
    }
    
    /// <summary>
    /// Determines whether either configured shortcut for an action is currently active.
    /// </summary>
    /// <param name="action">The action identifier.</param>
    /// <returns><c>true</c> if one of the action bindings is fully held; otherwise, <c>false</c>.</returns>
    public bool IsActionDown(string action)
    {
        var assignedKeys = GetKeys(action);

        // Each action can have up to two alternative bindings. If either chord
        // is fully held, the action is considered active.
        return assignedKeys.Item1.All(currentState.IsKeyDown) || assignedKeys.Item2.All(currentState.IsKeyDown);
    }
    
    /// <summary>
    /// Determines whether an action is currently inactive for at least one of its bindings.
    /// </summary>
    /// <param name="action">The action identifier.</param>
    /// <returns><c>true</c> if one of the action bindings is fully up; otherwise, <c>false</c>.</returns>
    public bool IsActionUp(string action)
    {
        var assignedKeys = GetKeys(action);
        return assignedKeys.Item1.All(currentState.IsKeyUp) || assignedKeys.Item2.All(currentState.IsKeyUp);
    }

    /// <summary>
    /// Determines whether an action was newly pressed on the current frame.
    /// </summary>
    /// <param name="action">The action identifier.</param>
    /// <returns><c>true</c> if one binding became active this frame; otherwise, <c>false</c>.</returns>
    public bool IsActionPressed(string action)
    {
        var assignedKeys = GetKeys(action);

        // An action is newly pressed when every key in one binding is now down
        // and at least one of those keys was up on the previous frame.
        return (assignedKeys.Item1.All(currentState.IsKeyDown) && assignedKeys.Item1.Any(previousState.IsKeyUp)) ||
            (assignedKeys.Item2.All(currentState.IsKeyDown) && assignedKeys.Item2.Any(previousState.IsKeyUp));
    }

    /// <summary>
    /// Gets the configured key bindings for an action.
    /// </summary>
    /// <param name="action">The action identifier.</param>
    /// <returns>A pair containing the primary and alternate bindings.</returns>
    public (Keys[], Keys[]) GetKeys(string action)
    {
        // Lazily seed config entries so new actions pick up defaults without a
        // separate migration step.
        InitAction(action);
        return Config.Instance.Keymap[action];
    }

    /// <summary>
    /// Gets a display string for the primary shortcut assigned to an action.
    /// </summary>
    /// <param name="action">The action identifier.</param>
    /// <returns>A human-readable shortcut string.</returns>
    public string GetShortcut(string action)
    {
        // The UI currently renders only the primary binding.
        return string.Join('+', GetKeys(action).Item1);
    }

    /// <summary>
    /// Converts an action identifier into a simple UI label.
    /// </summary>
    /// <param name="action">The snake_case action identifier.</param>
    /// <returns>A spaced title-style label.</returns>
    public string PrettyName(string action)
    {
        // Convert persisted snake_case action ids into simple UI labels.
        return string.Join(' ', action.Split('_').Select(s => char.ToUpper(s[0]) + s[1..]));
    }
    
    private void InitAction(string action)
    {
        if (Config.Instance.Keymap.ContainsKey(action))
        {
            return;
        }

        // Store defaults on first access so user config files only receive
        // entries for actions known to the current build.
        var defaultKey = GetDefault(action);
        if (defaultKey != (NotAssigned, NotAssigned))
        {
            Config.Instance.Keymap[action] = defaultKey;
        }

        // Persist immediately so the settings file stays in sync with any newly
        // introduced actions.
        Config.Save();
    }
    
    private (Keys[],Keys[]) GetDefault(string action)
    {
        // Defaults favor WASD with arrow-key alternatives where that makes sense.
        return action switch
        {
            MoveUp => ([Keys.W], [Keys.Up]),
            MoveDown => ([Keys.S], [Keys.Down]),
            MoveLeft => ([Keys.A], [Keys.Left]),
            MoveRight => ([Keys.D], [Keys.Right]),
            ToggleAnimatedStatics => ([Keys.LeftControl, Keys.A], NotAssigned),
            Minimap => ([Keys.M], NotAssigned),
            _ => (NotAssigned, NotAssigned)
        };
    }

    /// <summary>
    /// Sorts shortcut keys so modifiers appear before letter and digit keys.
    /// </summary>
    public class LetterLastComparer : IComparer<Keys>
    {
        /// <summary>
        /// Compares two keys for display ordering.
        /// </summary>
        /// <param name="k1">The first key.</param>
        /// <param name="k2">The second key.</param>
        /// <returns>A signed value indicating their relative sort order.</returns>
        public int Compare(Keys k1, Keys k2)
        {
            // Sort modifiers ahead of letter/digit keys so shortcuts render as
            // Ctrl+Shift+X instead of X+Ctrl+Shift.
            if (k1 is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9)
            {
                return 1;
            }
            if (k2 is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9)
            {
                return -1;
            }
            return 0;
        }
    }
}
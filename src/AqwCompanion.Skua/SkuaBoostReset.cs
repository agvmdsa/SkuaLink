namespace AqwCompanion.Skua;

/// <summary>
/// Decides when the boost sampler must forget everything: another character logged in, or the character stayed logged out for
/// <see cref="LoggedOutPolls"/> polls in a row. One poll that sees "logged out" (a map change does that) resets nothing. A first
/// poll of a plugin loaded mid-session never resets: the login was not observed. Worker thread only.
/// </summary>
public sealed class SkuaBoostReset
{
    public const int LoggedOutPolls = 3;

    private string? _character;
    private int _loggedOut;

    /// <summary>True when the sampler (and what hangs on it) must be reset now.</summary>
    public bool Observe(bool loggedIn, string? character)
    {
        if (!loggedIn)
        {
            _loggedOut++;
            if (_loggedOut != LoggedOutPolls)
            {
                return false;
            }

            _character = null;
            return true;
        }

        _loggedOut = 0;
        var changed = _character is not null && !string.Equals(_character, character, StringComparison.OrdinalIgnoreCase);
        _character = character;
        return changed;
    }
}

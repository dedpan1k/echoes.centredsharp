using CentrED.Network;
using CentrED.Server.Config;

namespace CentrED.Server;

/// <summary>
/// Provides convenience accessors for account-derived information on server network sessions.
/// </summary>
public static class ServerNetState
{
    /// <summary>
    /// Determines whether the session's account meets the requested access level.
    /// </summary>
    /// <param name="ns">The client session to check.</param>
    /// <param name="accessLevel">The minimum required access level.</param>
    /// <returns><see langword="true"/> when the session is authorized; otherwise, <see langword="false"/>.</returns>
    public static bool ValidateAccess(this NetState<CEDServer> ns, AccessLevel accessLevel)
    {
        return ns.AccessLevel() >= accessLevel;
    }
    
    /// <summary>
    /// Gets the persisted account associated with the session.
    /// </summary>
    /// <param name="ns">The authenticated client session.</param>
    /// <returns>The matching account record.</returns>
    public static Account Account(this NetState<CEDServer> ns)
    {
        return ns.Parent.GetAccount(ns)!;
    }

    /// <summary>
    /// Gets the access level granted to the session's account.
    /// </summary>
    /// <param name="ns">The client session.</param>
    /// <returns>The account access level.</returns>
    public static AccessLevel AccessLevel(this NetState<CEDServer> ns)
    {
        return ns.Account().AccessLevel;
    }

    /// <summary>
    /// Gets the last successful login timestamp recorded for the session's account.
    /// </summary>
    /// <param name="ns">The client session.</param>
    /// <returns>The last login timestamp.</returns>
    public static DateTime LastLogon(this NetState<CEDServer> ns)
    {
        return ns.Account().LastLogon;
    }
}
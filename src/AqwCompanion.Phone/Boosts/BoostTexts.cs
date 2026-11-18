namespace AqwCompanion.Phone.Boosts;

/// <summary>The texts shared by the guards, the service, the API and the view model.</summary>
public static class BoostTexts
{
    public const string NotSupported = "Update the plugin DLL to use boosts.";
    public const string NotLoggedIn = "The character is not logged in.";
    public const string NotInBag = "That boost is not in the bag.";
    public const string UnknownType = "That boost is not a gold, class, reputation or XP boost.";
    public const string AlreadyLoggedIn = "The character is already logged in.";
    public const string Disconnected = "Skua is disconnected.";
    public const string UnknownSkua = "SkuaLink no longer knows this Skua.";
    public const string NoAnswer = "Skua did not answer.";
    public const string Refused = "Skua refused the command.";
    public const string LoginStarted = "Login started";

    public static string AlreadyActive(string type) => $"A {BoostTypes.Display(type)} boost is already active.";
}

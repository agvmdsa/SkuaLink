namespace AqwCompanion.Phone.Boosts;

public static class BoostTypes
{
    public static string Display(string type) => type switch { "gold" => "gold", "class" => "class", "rep" => "reputation", "xp" => "XP", _ => "boost" };

    public static string Heading(string type) => type switch { "gold" => "Gold", "class" => "Class", "rep" => "Reputation", "xp" => "XP", _ => "Boost" };
}

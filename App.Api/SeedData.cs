namespace App.Api;

// Sample rows for a person trying the product. The app calls Run at startup, after the tables exist, only when the setting
// Seed:Demo is true (the factory's run files set it; tests never do). Add rows to a table only when that table is empty.
public static class SeedData
{
    public static void Run(AppDb db)
    {
    }
}

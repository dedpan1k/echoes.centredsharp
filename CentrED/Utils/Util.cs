namespace CentrED.Utils;

/// <summary>
/// Provides small shared utility helpers.
/// </summary>
public static class Util
{
    /// <summary>
    /// Returns a random value from the collection, or <c>null</c> when the collection is empty.
    /// </summary>
    /// <typeparam name="T">The value type stored in the collection.</typeparam>
    /// <param name="collection">The source collection.</param>
    /// <returns>A randomly selected value, or <c>null</c> when no values are available.</returns>
    public static T? GetRandom<T>(this ICollection<T> collection) where T : struct
    {
        // Empty collections map to null so callers can branch without catching exceptions.
        if (collection.Count == 0)
            return null;
        return collection.ElementAt(Random.Shared.Next(collection.Count));
    }
}
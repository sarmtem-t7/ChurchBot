namespace ChurchBot;

/// <summary>
/// In-memory storage for transient per-user state (selected category).
/// Message mappings and deduplication have been moved to PersistentStorage.
/// </summary>
public class UserStateStorage
{
    private readonly Dictionary<long, string> _pendingCategories = new();

    public void SetCategory(long userId, string category)
    {
        lock (_pendingCategories)
            _pendingCategories[userId] = category;
    }

    public string? GetCategory(long userId)
    {
        lock (_pendingCategories)
            return _pendingCategories.TryGetValue(userId, out var c) ? c : null;
    }

    public void ClearCategory(long userId)
    {
        lock (_pendingCategories)
            _pendingCategories.Remove(userId);
    }
}

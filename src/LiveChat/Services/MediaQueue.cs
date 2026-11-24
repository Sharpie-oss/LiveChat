using LiveChat.Models;

namespace LiveChat.Services;

/// <summary>
/// File d'attente bornée des médias à afficher. Utilisée uniquement depuis le thread de l'interface,
/// elle n'a donc pas besoin de verrou.
/// </summary>
public sealed class MediaQueue
{
    private readonly Queue<MediaItem> _items = new();
    private int _capacity = 20;

    public int Capacity
    {
        get => _capacity;
        set => _capacity = Math.Max(1, value);
    }

    public int Count => _items.Count;

    /// <summary>Ajoute les éléments tant qu'il reste de la place.</summary>
    /// <returns>Nombre d'éléments ignorés car la file était pleine.</returns>
    public int Enqueue(IEnumerable<MediaItem> items)
    {
        var dropped = 0;
        foreach (var item in items)
        {
            if (_items.Count >= _capacity)
            {
                dropped++;
                continue;
            }

            _items.Enqueue(item);
        }

        return dropped;
    }

    public bool TryDequeue(out MediaItem item) => _items.TryDequeue(out item!);

    public void Clear() => _items.Clear();
}

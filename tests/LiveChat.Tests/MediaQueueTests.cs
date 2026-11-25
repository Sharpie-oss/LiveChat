using LiveChat.Models;
using LiveChat.Services;

namespace LiveChat.Tests;

public class MediaQueueTests
{
    private static MediaItem Item(string name) => new(MediaKind.Image, name) { Url = $"https://cdn/{name}.png" };

    [Fact]
    public void Enqueue_KeepsOrder()
    {
        var queue = new MediaQueue();
        queue.Enqueue([Item("a"), Item("b"), Item("c")]);

        Assert.True(queue.TryDequeue(out var first));
        Assert.True(queue.TryDequeue(out var second));
        Assert.Equal("a", first.Author);
        Assert.Equal("b", second.Author);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Enqueue_DropsItemsBeyondCapacity()
    {
        var queue = new MediaQueue { Capacity = 2 };

        var dropped = queue.Enqueue([Item("a"), Item("b"), Item("c"), Item("d")]);

        Assert.Equal(2, dropped);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void Clear_EmptiesQueue()
    {
        var queue = new MediaQueue();
        queue.Enqueue([Item("a"), Item("b")]);

        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void RemoveWhere_RemovesMatchingItemsAndKeepsOrder()
    {
        var queue = new MediaQueue();
        queue.Enqueue([Item("a") with { MessageId = 1 }, Item("b") with { MessageId = 2 }, Item("c") with { MessageId = 1 }, Item("d") with { MessageId = 3 }]);

        var removed = queue.RemoveWhere(i => i.MessageId == 1);

        Assert.Equal(2, removed);
        Assert.True(queue.TryDequeue(out var first));
        Assert.True(queue.TryDequeue(out var second));
        Assert.Equal(["b", "d"], [first.Author, second.Author]);
    }

    [Fact]
    public void Capacity_IsAtLeastOne()
    {
        var queue = new MediaQueue { Capacity = 0 };

        Assert.Equal(1, queue.Capacity);
    }
}

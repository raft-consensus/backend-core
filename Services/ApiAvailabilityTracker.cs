using System.Collections.Concurrent;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public class ApiAvailabilityTracker : IApiAvailabilityTracker
{
    private readonly ConcurrentQueue<(DateTime TimestampUtc, bool Available)> _samples = new();
    private readonly TimeSpan _window = TimeSpan.FromHours(24);

    public void RecordResponse(int statusCode)
    {
        var available = statusCode < StatusCodes.Status500InternalServerError;
        var now = DateTime.UtcNow;

        _samples.Enqueue((now, available));
        Trim(now);
    }

    public decimal GetAvailabilityPercent()
    {
        var now = DateTime.UtcNow;
        Trim(now);

        var snapshot = _samples.ToArray();
        if (snapshot.Length == 0)
        {
            return 100.0m;
        }

        var availableCount = snapshot.Count(sample => sample.Available);
        return decimal.Round((decimal)availableCount / snapshot.Length * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private void Trim(DateTime nowUtc)
    {
        while (_samples.TryPeek(out var sample) && nowUtc - sample.TimestampUtc > _window)
        {
            _samples.TryDequeue(out _);
        }
    }
}

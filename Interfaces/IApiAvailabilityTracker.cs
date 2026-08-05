namespace raft_backend.Interfaces;

public interface IApiAvailabilityTracker
{
    void RecordResponse(int statusCode);

    decimal GetAvailabilityPercent();
}

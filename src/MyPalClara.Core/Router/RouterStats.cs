namespace MyPalClara.Core.Router;

public record RouterStats(
    int ActiveChannels,
    int TotalQueued,
    int DebouncingChannels,
    Dictionary<RequestStatus, int> ByStatus);

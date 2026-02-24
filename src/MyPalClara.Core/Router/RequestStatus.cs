namespace MyPalClara.Core.Router;

public enum RequestStatus
{
    Pending,
    Queued,
    Active,
    Completed,
    Cancelled,
    Failed,
    Debounce
}

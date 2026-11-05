namespace MoonBooks.Core.Domain;

public enum BookFormat
{
    Fb2 = 0,
    Epub = 1,
}

public enum BookStatus
{
    NotReading = 0,
    Reading = 1,
    Finished = 2,
}

public enum CategoryKind
{
    Genre = 0,
    Collection = 1,
    Tag = 2,
}

public enum CategorySource
{
    Auto = 0,
    Manual = 1,
}

public enum SyncState
{
    Ready = 0,
    Syncing = 1,
    Error = 2,
    Disabled = 3,
    Offline = 4,
}

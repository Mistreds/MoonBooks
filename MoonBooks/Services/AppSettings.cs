using System;
using System.Collections.Generic;

namespace MoonBooks.Services;

public enum LibraryViewMode
{
    Shelves,
    Grid,
    List,
}

public enum SyncProviderKind
{
    None,
    WebDav,
    GoogleDrive,
}

public enum SyncMode
{
    ReadingStatus,
    StatusAndBooks,
}

public enum LineSpacingPreset
{
    Tight,
    Normal,
    Relaxed,
}

public enum ReaderThemeKind
{
    White,
    Sepia,
    Gray,
    Black,
}

public enum ReaderAlignment
{
    Left,
    Justify,
}

public sealed class ReaderSettings
{
    public double FontSize { get; set; } = 18;
    public LineSpacingPreset LineSpacing { get; set; } = LineSpacingPreset.Normal;
    public ReaderThemeKind Theme { get; set; } = ReaderThemeKind.Sepia;
    public ReaderAlignment Alignment { get; set; } = ReaderAlignment.Justify;
    public bool Hyphenation { get; set; }
    public bool PageAnimation { get; set; } = true;
    public bool ScrollMode { get; set; }
    public string FontFamily { get; set; } = "PT Serif";
    public bool Bionic { get; set; }

    public double LineHeightFactor => LineSpacing switch
    {
        LineSpacingPreset.Tight => 1.35,
        LineSpacingPreset.Relaxed => 1.8,
        _ => 1.55,
    };
}

public sealed class LocalBookmark
{
    public long BookId { get; set; }
    public int ChapterIndex { get; set; }
    public int CharOffset { get; set; }
    public long CreatedAtUtc { get; set; }
}

public sealed class AppSettings
{
    public string LibraryPath { get; set; } = string.Empty;
    public ReaderSettings Reader { get; set; } = new();
    public LibraryViewMode ViewMode { get; set; } = LibraryViewMode.Grid;
    public SyncProviderKind SyncProvider { get; set; } = SyncProviderKind.None;
    public SyncMode SyncMode { get; set; } = SyncMode.ReadingStatus;
    public Guid DeviceId { get; set; } = Guid.NewGuid();
    public bool WatchLibrary { get; set; } = true;
    public bool AutoAcceptRemotePosition { get; set; }
    public int SyncIntervalMinutes { get; set; } = 10;
    public string MoonPlusBooksFolder { get; set; } = "Books";
    public string WebDavUrl { get; set; } = string.Empty;
    public string WebDavUser { get; set; } = string.Empty;
    public string GoogleClientId { get; set; } = string.Empty;
    public string GoogleDriveFolderId { get; set; } = string.Empty;
    public List<LocalBookmark> Bookmarks { get; set; } = [];
}

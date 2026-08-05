namespace Sweeft.Gui;

/// <summary>A single node rendered in the treemap.</summary>
public sealed record TreemapItem(string Name, string Path, long SizeBytes, bool IsDirectory);

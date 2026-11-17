namespace SchoolLibrary.Services.OnlineLibraries;

/// <summary>What an administrator types in the online library form.</summary>
public record OnlineLibraryInput(string Name, string? Description, string Url, string? Icon, int SortOrder);

/// <summary>An icon the administrator can pick for a card.</summary>
public record LibraryIconOption(string Class, string Label);

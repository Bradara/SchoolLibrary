namespace SchoolLibrary.Data.Enums;

/// <summary>Which section of the library a resource belongs to. The numbers are stored in the database.</summary>
public enum ResourceKind
{
    /// <summary>A teacher's own material, shared and embedded in the platform. Also the default for older resources.</summary>
    Lesson = 0,

    /// <summary>A freely licensed material from the web (direct PDF links, static pages).</summary>
    External = 1
}

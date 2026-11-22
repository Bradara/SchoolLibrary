namespace SchoolLibrary.Data.Enums;

/// <summary>
/// Who may see a resource besides its owner and the administrators. The values are bit flags stored in
/// the database, so any combination is allowed: for example Students | Teachers.
/// </summary>
[Flags]
public enum ResourceAudience
{
    /// <summary>Nobody else: only the owner and the administrators.</summary>
    None = 0,

    /// <summary>Visitors who are not signed in.</summary>
    Guests = 1,

    Students = 2,

    Parents = 4,

    Teachers = 8,

    /// <summary>Signed-in users of every role.</summary>
    SignedIn = Students | Parents | Teachers,

    Everyone = Guests | SignedIn
}

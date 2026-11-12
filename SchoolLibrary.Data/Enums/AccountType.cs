namespace SchoolLibrary.Data.Enums;

/// <summary>
/// What the person chooses at registration. A teacher account is only a request until an
/// administrator confirms it, so nobody can give themselves teacher rights.
/// </summary>
public enum AccountType
{
    Student = 1,
    Parent = 2,
    Teacher = 3
}

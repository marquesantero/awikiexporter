namespace ExportAzureWiki.Models.Authentication
{
    public enum AuthenticationProvider
    {
        None = 0,
        AzureAD = 1,
        MicrosoftAccount = 2,
        GitHub = 3,
        Google = 4,
        Local = 5
    }

    public enum IdentityType
    {
        User = 0,
        AzureADGroup = 1,
        WindowsGroup = 2,
        GitHubOrganization = 3,
        GitHubTeam = 4,
        Custom = 5
    }

    public enum PermissionLevel
    {
        None = 0,
        Read = 1,
        Write = 2,
        Admin = 3,
        Owner = 4
    }
}

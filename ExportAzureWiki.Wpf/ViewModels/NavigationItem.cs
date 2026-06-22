namespace ExportAzureWiki.Wpf.ViewModels;

public enum AppSection
{
    Login,
    Workspace,
    WikiManagement,
    UsersAndGroups,
    Permissions,
    Providers,
    AiCenter,
    ExportCenter
}

public sealed class NavigationItem
{
    public required AppSection Section { get; init; }
    public required string Label { get; init; }
}

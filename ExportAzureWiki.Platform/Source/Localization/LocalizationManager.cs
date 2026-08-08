using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using ExportAzureWiki.Data;

namespace ExportAzureWiki.Localization;

public static partial class LocalizationManager
{
    private const string LanguageSettingKey = "ui.language";

    // Dicionário de frases exatas ajustado para padrões de UI em Inglês
    private static readonly Dictionary<string, string> ExactPtToEn = new(StringComparer.Ordinal)
    {
        ["Wiki"] = "Wiki",
        ["Segurança"] = "Security",
        ["Gerenciar Wikis..."] = "Manage Wikis...",
        ["Sair"] = "Exit",
        ["Login..."] = "Login...",
        ["Logout"] = "Logout",
        ["Configurar Provedores..."] = "Configure Providers...",
        ["Configurar IA..."] = "Configure AI...",
        ["Gerar Token de Conexão..."] = "Generate Connection Token...",
        ["Gerenciar Permissões..."] = "Manage Permissions...",
        ["Informações do Usuário"] = "User Information",
        ["Não autenticado"] = "Not authenticated",
        ["Autenticação não configurada"] = "Authentication not configured",
        ["Logado como:"] = "Logged in as:",
        ["Modo Escuro"] = "Dark Mode",
        ["Tema de Código"] = "Code Theme",
        ["Exportar para Word"] = "Export to Word",
        ["Exportar para PDF"] = "Export to PDF",
        ["Visualizar Páginas"] = "View Pages",
        ["Página Atual"] = "Current Page",
        ["Todas as Páginas"] = "All Pages",
        ["Atualizar Páginas de Cache"] = "Refresh Cache",
        ["Processando..."] = "Processing...",
        ["Carregando wiki..."] = "Loading wiki...",
        ["Migrando dados locais..."] = "Migrating local data...",
        ["Erro"] = "Error",
        ["Aviso"] = "Warning",
        ["Sucesso"] = "Success",
        ["Informação"] = "Information",
        ["Confirmação"] = "Confirmation",
        ["Sim"] = "Yes",
        ["Não"] = "No",
        ["OK"] = "OK",
        ["Cancelar"] = "Cancel",
        ["Fechar"] = "Close",
        ["Salvar"] = "Save",
        ["Editar"] = "Edit",
        ["Excluir"] = "Delete",
        ["Remover"] = "Remove",
        ["Adicionar"] = "Add",
        ["Visualizar"] = "View",
        ["Idioma"] = "Language",
        ["Português"] = "Portuguese",
        ["Inglês"] = "English",
        ["Usuários"] = "Users",
        ["Grupos"] = "Groups",
        ["Buscar:"] = "Search:",
        ["Provedor:"] = "Provider:",
        ["Origem:"] = "Source:",
        ["Adicionar Usuário"] = "Add User",
        ["Adicionar Grupo"] = "Add Group",
        ["Alterar Senha"] = "Change Password",
        ["Gerenciar Grupos"] = "Manage Groups",
        ["Gerenciar Membros"] = "Manage Members",
        ["Gerenciamento de Usuários e Grupos"] = "Users and Groups Management",
        ["Autenticação ExportAzureWiki"] = "ExportAzureWiki Authentication",
        ["Entrar na aplicação"] = "Sign In",
        ["Escolha o provedor de autenticação para continuar"] = "Choose an authentication provider to continue",
        ["Manter conectado"] = "Keep me signed in",
        ["Sempre pedir login ao abrir o app"] = "Always prompt for login on startup",
        ["Login - AWikiExport"] = "Login - AWikiExport",
        ["Configuração de Provedores OAuth"] = "OAuth Providers Configuration",
        ["Selecione o Provedor"] = "Select Provider",
        ["Selecione um provedor para continuar."] = "Select a provider to continue.",
        ["Seleção obrigatória"] = "Selection required",
        ["Nenhum template de provedor está disponível."] = "No provider templates available.",
        ["Selecione um provedor para editar."] = "Select a provider to edit.",
        ["Selecione um provedor para excluir."] = "Select a provider to delete.",
        ["Provedor não encontrado."] = "Provider not found.",
        ["Template do provedor não encontrado."] = "Provider template not found.",
        ["Confirmar Exclusão"] = "Confirm Deletion",
        ["Tem certeza que deseja excluir este provedor?"] = "Are you sure you want to delete this provider?",
        ["Não foi possível carregar o arquivo markdown selecionado."] = "Could not load the selected markdown file.",
        ["Comentários não estão disponíveis para arquivo markdown local."] = "Comments are not available for local markdown files.",
        ["Nenhuma página foi selecionada."] = "No page selected.",
        ["Nenhuma página carregada."] = "No page loaded.",
        ["Nenhum comentário disponível para esta página."] = "No comments available for this page.",
        ["Confirmar Logout"] = "Confirm Logout",
        ["Deseja realmente fazer logout?"] = "Are you sure you want to log out?",
        ["Logout realizado com sucesso!"] = "Logout successful!",
        ["Serviço de migração não disponível."] = "Migration service unavailable.",
        ["Serviço de autenticação não disponível."] = "Authentication service unavailable.",
        ["Serviços de autorização não disponíveis."] = "Authorization services unavailable.",
        ["Você não está autenticado."] = "You are not authenticated.",
        ["Conexão estabelecida com sucesso!"] = "Connection successful!",
        ["Teste de Conexão"] = "Connection Test",
        ["Não foi possível conectar à wiki. Verifique as configurações."] = "Could not connect to the wiki. Please check your settings.",
        ["Validação"] = "Validation",
        ["Todos"] = "All",
        ["Todas"] = "All",
        ["Outro"] = "Other",
        ["Migração"] = "Migration",
        ["Tema"] = "Theme",
        ["Configuração"] = "Configuration",
        ["Configurações"] = "Settings",
        ["Informações"] = "Information",
        ["Wiki:"] = "Wiki:",
        ["Nível de Permissão:"] = "Permission Level:",
        ["Tipo de Identidade:"] = "Identity Type:",
        ["Informações Básicas"] = "Basic Information",
        ["Identidade"] = "Identity",
        ["Adicionar Permissão"] = "Add Permission",
        ["Editar Permissão"] = "Edit Permission",
        ["Nível de Permissão"] = "Permission Level",
        ["Novo nível de permissão:"] = "New permission level:",
        ["Permissões de Wikis"] = "Wiki Permissions",
        ["Grupos de Usuários"] = "User Groups",
        ["Grupos de Wikis"] = "Wiki Groups",
        ["Gerenciamento de Permissões"] = "Permissions Management",
        ["Filtrar Wiki:"] = "Filter Wiki:",
        ["ID da Identidade"] = "Identity ID",
        ["Tipo"] = "Type",
        ["Nível"] = "Level",
        ["Ativa"] = "Active",
        ["Criada em"] = "Created at",
        ["Nome"] = "Name",
        ["Membros"] = "Members",
        ["ID Externo"] = "External ID",
        ["Ativo"] = "Active",
        ["Descrição"] = "Description",
        ["Wikis"] = "Wikis",
        ["Buscar"] = "Search",
        ["Verificar"] = "Verify",
        ["Buscando..."] = "Searching...",
        ["Verificando..."] = "Verifying...",
        ["Escolha um tipo de provedor para configurar:"] = "Choose a provider type to configure:",
        ["Selecione um provedor para editar ou adicione um novo."] = "Select a provider to edit or add a new one.",
        ["Adicionar Provedor"] = "Add Provider",
        ["Ativar/Desativar"] = "Enable/Disable",
        ["Provedor Ativo"] = "Active Provider",
        ["Nome de Exibição:"] = "Display Name:",
        ["Para obter as credenciais:"] = "To obtain credentials:",
        ["Acessar página de registro"] = "Open registration page",
        ["Configuração:"] = "Configuration:",
        ["Escopos (Scopes):"] = "Scopes:",
        ["ID do Usuário:"] = "User ID:",
        ["Email (opcional):"] = "Email (optional):",
        ["ID do Grupo do Azure AD:"] = "Azure AD Group ID:",
        ["Nome do Grupo:"] = "Group Name:",
        ["SID do Grupo do Windows:"] = "Windows Group SID:",
        ["Nome da Organização GitHub:"] = "GitHub Organization Name:",
        ["Organização:"] = "Organization:",
        ["Nome do Time:"] = "Team Name:",
        ["ID da Identidade:"] = "Identity ID:",
        ["Crie uma identidade customizada para uso específico da aplicação."] = "Create a custom identity for app-specific use.",
        ["Grupo encontrado (implemente busca real)"] = "Group found (implement actual lookup)",
        ["Usuário"] = "User",
        ["Senha"] = "Password",
        ["Mostrar senha"] = "Show password",
        ["Entrar"] = "Sign In",
        ["Ou entre com um provedor externo"] = "Or sign in with an external provider",
        ["Entrar com Azure AD"] = "Sign in with Azure AD",
        ["Entrar com Microsoft"] = "Sign in with Microsoft",
        ["Entrar com GitHub"] = "Sign in with GitHub",
        ["Entrar com Google"] = "Sign in with Google",
        ["Entrar com usuário e senha"] = "Sign in with username and password",
        ["Perfil de Exportação"] = "Export Profile",
        ["Rápido"] = "Quick",
        ["Fiel"] = "Faithful",
        ["Cliente"] = "Client",
        ["Modo Offline"] = "Offline Mode",
        ["Gerar Diagnóstico"] = "Generate Diagnostics",
        ["Diagnóstico"] = "Diagnostics",
        ["Gerando diagnóstico..."] = "Generating diagnostics...",
        ["Pacote de diagnóstico gerado em:\n{0}"] = "Diagnostics bundle generated at:\n{0}",
        ["Erro ao gerar diagnóstico: {0}"] = "Error generating diagnostics: {0}",
        ["Documento Word exportado com sucesso!\nRelatório: {0}"] = "Word document exported successfully!\nReport: {0}",
        ["Documento PDF exportado com sucesso!\nRelatório: {0}"] = "PDF exported successfully!\nReport: {0}",
        ["Cache: -"] = "Cache: -",
        ["Cache: H {0} | M {1} | R {2}"] = "Cache: H {0} | M {1} | R {2}",
        ["Offline miss: {0}"] = "Offline miss: {0}",
        ["[Offline]"] = "[Offline]"
    };

    private static readonly Dictionary<string, string> TokenPtToEn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["arquivo"] = "file",
        ["arquivos"] = "files",
        ["página"] = "page",
        ["páginas"] = "pages",
        ["usuário"] = "user",
        ["usuários"] = "users",
        ["grupo"] = "group",
        ["grupos"] = "groups",
        ["provedor"] = "provider",
        ["provedores"] = "providers",
        ["autenticação"] = "authentication",
        ["permissão"] = "permission",
        ["permissões"] = "permissions",
        ["configuração"] = "configuration",
        ["configurações"] = "settings",
        ["conexão"] = "connection",
        ["banco"] = "database",
        ["dados"] = "data",
        ["carregar"] = "load",
        ["carregado"] = "loaded",
        ["carregada"] = "loaded",
        ["salvar"] = "save",
        ["salvo"] = "saved",
        ["salva"] = "saved",
        ["falha"] = "failure",
        ["erro"] = "error",
        ["sucesso"] = "success",
        ["selecione"] = "select",
        ["selecionar"] = "select",
        ["nenhum"] = "no",
        ["nenhuma"] = "no",
        ["todos"] = "all",
        ["todas"] = "all",
        ["abrir"] = "open",
        ["fechar"] = "close",
        ["voltar"] = "back",
        ["avançar"] = "next",
        ["comentários"] = "comments",
        ["exportar"] = "export",
        ["word"] = "Word",
        ["pdf"] = "PDF",
        ["cache"] = "cache",
        ["tema"] = "theme",
        ["escuro"] = "dark",
        ["claro"] = "light",
        ["local"] = "local",
        ["administrador"] = "administrator",
        ["senha"] = "password",
        ["nome"] = "name",
        ["descrição"] = "description",
        ["ativo"] = "active",
        ["inativo"] = "inactive",
        ["status"] = "status",
        ["obrigatório"] = "required",
        ["outro"] = "other",
        ["outra"] = "other",
        ["migração"] = "migration",
        ["buscar"] = "search",
        ["verificar"] = "verify",
        ["digite"] = "enter",
        ["visualizar"] = "view",
        ["remover"] = "remove",
        ["adicionar"] = "add",
        ["editar"] = "edit",
        ["wiki"] = "wiki",
        ["nível"] = "level",
        ["identidade"] = "identity",
        ["básicas"] = "basic",
        ["informações"] = "information",
        ["time"] = "team",
        ["origem"] = "source",
        ["comentário"] = "comment",
        ["selecionada"] = "selected",
        ["selecionado"] = "selected"
    };

    private static readonly (Regex Pattern, Func<Match, string> Map)[] PtToEnPatterns =
    [
        (new Regex(@"^Erro ao carregar\s+(.+?):?\s+(.+)$", RegexOptions.IgnoreCase), m => $"Error loading {TranslateByTokens(m.Groups[1].Value)}: {m.Groups[2].Value}"),
        (new Regex(@"^Erro ao salvar\s+(.+?):?\s+(.+)$", RegexOptions.IgnoreCase), m => $"Error saving {TranslateByTokens(m.Groups[1].Value)}: {m.Groups[2].Value}"),
        (new Regex(@"^Erro ao excluir\s+(.+?):?\s+(.+)$", RegexOptions.IgnoreCase), m => $"Error deleting {TranslateByTokens(m.Groups[1].Value)}: {m.Groups[2].Value}"),
        (new Regex(@"^Erro ao adicionar\s+(.+?):?\s+(.+)$", RegexOptions.IgnoreCase), m => $"Error adding {TranslateByTokens(m.Groups[1].Value)}: {m.Groups[2].Value}"),
        (new Regex(@"^Erro ao editar\s+(.+?):?\s+(.+)$", RegexOptions.IgnoreCase), m => $"Error editing {TranslateByTokens(m.Groups[1].Value)}: {m.Groups[2].Value}"),
        (new Regex(@"^Erro ao atualizar\s+(.+?):?\s+(.+)$", RegexOptions.IgnoreCase), m => $"Error updating {TranslateByTokens(m.Groups[1].Value)}: {m.Groups[2].Value}"),
        (new Regex(@"^Erro ao abrir\s+(.+?):?\s+(.+)$", RegexOptions.IgnoreCase), m => $"Error opening {TranslateByTokens(m.Groups[1].Value)}: {m.Groups[2].Value}"),
        (new Regex(@"^Erro ao conectar:\s+(.+)$", RegexOptions.IgnoreCase), m => $"Connection error: {m.Groups[1].Value}"),
        (new Regex(@"^Erro de configuração:\s+(.+)$", RegexOptions.IgnoreCase), m => $"Configuration error: {m.Groups[1].Value}"),
        (new Regex(@"^Erro durante a exportação:\s+(.+)$", RegexOptions.IgnoreCase), m => $"Error during export: {m.Groups[1].Value}"),
        (new Regex(@"^Selecione\s+(.+?)\.?$", RegexOptions.IgnoreCase), m => $"Select {TranslateByTokens(m.Groups[1].Value)}."),
        (new Regex(@"^Digite\s+(.+?)\.?$", RegexOptions.IgnoreCase), m => $"Enter {TranslateByTokens(m.Groups[1].Value)}."),
        (new Regex(@"^Tem certeza que deseja\s+(.+?)\?$", RegexOptions.IgnoreCase), m => $"Are you sure you want to {TranslateByTokens(m.Groups[1].Value)}?"),
        (new Regex(@"^Deseja\s+(.+?)\?$", RegexOptions.IgnoreCase), m => $"Do you want to {TranslateByTokens(m.Groups[1].Value)}?"),
        (new Regex(@"^Nenhuma opção de exportação foi selecionada\.?$", RegexOptions.IgnoreCase), _ => "No export option was selected."),
        (new Regex(@"^Nenhuma página carregada para exportação\.?$", RegexOptions.IgnoreCase), _ => "No page loaded for export."),
        (new Regex(@"^Nenhum conteúdo HTML disponível para exportar\.?$", RegexOptions.IgnoreCase), _ => "No HTML content available for export."),
        (new Regex(@"^Documento Word exportado com sucesso!?$", RegexOptions.IgnoreCase), _ => "Word document exported successfully!"),
        (new Regex(@"^Documento PDF exportado com sucesso!?$", RegexOptions.IgnoreCase), _ => "PDF document exported successfully!")
    ];

    public static SupportedLanguage CurrentLanguage { get; private set; } = DetermineDefaultLanguage(CultureInfo.CurrentUICulture);
    public static event EventHandler? LanguageChanged;

    /// <summary>Test seam: the Portuguese semantic dictionary keys.</summary>
    internal static IReadOnlyCollection<string> SemanticPtKeys => SemanticPt.Keys;

    /// <summary>Test seam: the English semantic dictionary keys.</summary>
    internal static IReadOnlyCollection<string> SemanticEnKeys => SemanticEn.Keys;

    public static string S(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var current = CurrentLanguage == SupportedLanguage.English ? SemanticEn : SemanticPt;
        if (current.TryGetValue(key, out var translated))
        {
            return translated;
        }

        if (SemanticPt.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    public static string Sf(string key, params object[] args)
    {
        var template = S(key);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    public static string S(string key, string fallback)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback ?? string.Empty;
        }

        var current = CurrentLanguage == SupportedLanguage.English ? SemanticEn : SemanticPt;
        if (current.TryGetValue(key, out var translated))
        {
            return translated;
        }

        return CurrentLanguage == SupportedLanguage.English ? T(fallback) : fallback;
    }

    public static void Initialize()
    {
        CurrentLanguage = LoadLanguage();
    }

    public static bool HasPersistedLanguageSelection()
    {
        try
        {
            var dbFactory = new DbConnectionFactory();
            using var connection = dbFactory.CreateConnectionAsync().GetAwaiter().GetResult();
            var dbType = dbFactory.GetDatabaseType();
            var table = dbType == DatabaseType.SqlServer ? "[dbo].[ApplicationSettings]" : "application_settings";
            var sql = dbType switch
            {
                DatabaseType.SqlServer => $"SELECT [Value] FROM {table} WHERE [Key] = @Key",
                DatabaseType.MySQL => $"SELECT value FROM {table} WHERE `key` = @Key",
                _ => $"SELECT value FROM {table} WHERE key = @Key"
            };

            return connection.QueryFirstOrDefault<string>(sql, new { Key = LanguageSettingKey }) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetLanguage(SupportedLanguage language)
    {
        if (CurrentLanguage == language) return;

        CurrentLanguage = language;
        SaveLanguage(language);
        // WPF (and any future UI) reacts to LanguageChanged to refresh
        // bindings. WinForms-specific re-localization was removed in
        // Fase 3.1; the UI shell owns its own refresh strategy.
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string T(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || CurrentLanguage == SupportedLanguage.Portuguese)
            return text ?? string.Empty;

        var normalized = NormalizeLookupText(NormalizeBrokenAccents(text));
        if (string.IsNullOrWhiteSpace(normalized)) return text ?? string.Empty;

        if (ExactPtToEn.TryGetValue(normalized, out var exact)) return exact;

        foreach (var (pattern, map) in PtToEnPatterns)
        {
            var match = pattern.Match(normalized);
            if (match.Success) return map(match);
        }

        return TranslateByTokens(normalized);
    }

    // WinForms tree-walking re-localization removed in Fase 3.1. WPF
    // ViewModels subscribe to LanguageChanged and refresh their bindings;
    // CLI is non-interactive. Restoring this behaviour for a future toolkit
    // belongs in the UI project for that toolkit, not in Platform.

    private static SupportedLanguage LoadLanguage()
    {
        try
        {
            var dbFactory = new DbConnectionFactory();
            using var connection = dbFactory.CreateConnectionAsync().GetAwaiter().GetResult();
            var dbType = dbFactory.GetDatabaseType();
            var table = dbType == DatabaseType.SqlServer ? "[dbo].[ApplicationSettings]" : "application_settings";
            var sql = dbType switch
            {
                DatabaseType.SqlServer => $"SELECT [Value] FROM {table} WHERE [Key] = @Key",
                DatabaseType.MySQL => $"SELECT value FROM {table} WHERE `key` = @Key",
                _ => $"SELECT value FROM {table} WHERE key = @Key"
            };

            var value = connection.QueryFirstOrDefault<string>(sql, new { Key = LanguageSettingKey });
            if (string.IsNullOrWhiteSpace(value))
            {
                return DetermineDefaultLanguage(CultureInfo.CurrentUICulture);
            }

            var persisted = JsonSerializer.Deserialize<PersistedLanguage>(value);
            return persisted?.Language ?? DetermineDefaultLanguage(CultureInfo.CurrentUICulture);
        }
        catch { return DetermineDefaultLanguage(CultureInfo.CurrentUICulture); }
    }

    internal static SupportedLanguage DetermineDefaultLanguage(CultureInfo culture)
        => string.Equals(culture.TwoLetterISOLanguageName, "pt", StringComparison.OrdinalIgnoreCase)
            ? SupportedLanguage.Portuguese
            : SupportedLanguage.English;

    private static void SaveLanguage(SupportedLanguage language)
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new PersistedLanguage { Language = language },
                new JsonSerializerOptions { WriteIndented = true });

            using var connection = new DbConnectionFactory().CreateConnectionAsync().GetAwaiter().GetResult();
            var dbType = new DbConnectionFactory().GetDatabaseType();
            var table = dbType == DatabaseType.SqlServer ? "[dbo].[ApplicationSettings]" : "application_settings";

            if (dbType == DatabaseType.SqlServer)
            {
                connection.Execute(
                    $"""
                     MERGE {table} AS target
                     USING (SELECT @Key AS [Key]) AS source
                     ON target.[Key] = source.[Key]
                     WHEN MATCHED THEN
                         UPDATE SET [Value] = @Value, [IsEncrypted] = 0, [LastModifiedAt] = GETDATE()
                     WHEN NOT MATCHED THEN
                         INSERT ([Key], [Value], [IsEncrypted], [LastModifiedAt])
                         VALUES (@Key, @Value, 0, GETDATE());
                     """,
                    new { Key = LanguageSettingKey, Value = json });
            }
            else if (dbType == DatabaseType.MySQL)
            {
                connection.Execute(
                    $"""
                     INSERT INTO {table} (`key`, value, is_encrypted, last_modified_at)
                     VALUES (@Key, @Value, 0, CURRENT_TIMESTAMP)
                     ON DUPLICATE KEY UPDATE
                         value = VALUES(value),
                         is_encrypted = 0,
                         last_modified_at = CURRENT_TIMESTAMP
                     """,
                    new { Key = LanguageSettingKey, Value = json });
            }
            else
            {
                connection.Execute(
                    $"""
                     INSERT INTO {table} (key, value, is_encrypted, last_modified_at)
                     VALUES (@Key, @Value, 0, CURRENT_TIMESTAMP)
                     ON CONFLICT(key) DO UPDATE SET
                         value = excluded.value,
                         is_encrypted = 0,
                         last_modified_at = CURRENT_TIMESTAMP
                     """,
                    new { Key = LanguageSettingKey, Value = json });
            }
        }
        catch { }
    }

    private static string TranslateByTokens(string source)
    {
        var tokens = Regex.Split(source, @"(\W+)");
        var sb = new StringBuilder(source.Length + 16);

        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token)) continue;

            if (Regex.IsMatch(token, @"^\w+$") && TokenPtToEn.TryGetValue(token, out var mapped))
                sb.Append(ApplyCasing(token, mapped));
            else
                sb.Append(token);
        }

        return sb.ToString();
    }

    private static string NormalizeBrokenAccents(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input
            .Replace("pgina", "página", StringComparison.Ordinal)
            .Replace("Pgina", "Página", StringComparison.Ordinal)
            .Replace("comentrio", "comentário", StringComparison.Ordinal)
            .Replace("Comentrio", "Comentário", StringComparison.Ordinal)
            .Replace("Informao", "Informação", StringComparison.Ordinal)
            .Replace("comentrios", "comentários", StringComparison.Ordinal)
            .Replace("Comentrios", "Comentários", StringComparison.Ordinal);
    }

    private static string NormalizeLookupText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var normalized = input.Replace("&", string.Empty, StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"^[\s\p{So}\p{Sk}\uE000-\uF8FF]+", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        return normalized;
    }

    private static string ApplyCasing(string source, string translated)
    {
        if (string.IsNullOrEmpty(source)) return translated;
        if (source.All(char.IsUpper)) return translated.ToUpperInvariant();
        if (char.IsUpper(source[0])) return char.ToUpperInvariant(translated[0]) + translated[1..];
        return translated;
    }

    private sealed class PersistedLanguage
    {
        public SupportedLanguage Language { get; set; } = SupportedLanguage.Portuguese;
    }
}

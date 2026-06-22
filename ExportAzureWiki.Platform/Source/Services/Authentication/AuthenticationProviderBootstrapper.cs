using ExportAzureWiki.Data;
using ExportAzureWiki.Models;
using ExportAzureWiki.Services.Authentication.Providers;

namespace ExportAzureWiki.Services.Authentication;

public sealed class AuthenticationProviderBootstrapper
{
    private readonly AuthenticationService _authenticationService;
    private readonly AuthenticationConfigService _authenticationConfigService;
    private readonly PasswordHashingService _passwordHashingService;
    private readonly OAuthProviderFactoryService _oauthProviderFactoryService;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AuthenticationProviderBootstrapper(
        AuthenticationService authenticationService,
        AuthenticationConfigService authenticationConfigService,
        PasswordHashingService passwordHashingService,
        OAuthProviderFactoryService oauthProviderFactoryService,
        IDbConnectionFactory dbConnectionFactory)
    {
        _authenticationService = authenticationService;
        _authenticationConfigService = authenticationConfigService;
        _passwordHashingService = passwordHashingService;
        _oauthProviderFactoryService = oauthProviderFactoryService;
        _dbConnectionFactory = dbConnectionFactory;
    }

    public void LoadProvidersFromDatabase()
    {
        var authMethodConfig = _authenticationConfigService.GetConfiguration();
        var localAuthAllowed = authMethodConfig.PrimaryMethod == AuthenticationMethod.Local || authMethodConfig.AllowLocalAuth;

        if (localAuthAllowed)
        {
            _authenticationService.RegisterProvider(new LocalCredentialsProvider(
                new UnitOfWork(_dbConnectionFactory),
                _passwordHashingService,
                _authenticationConfigService));
        }

        using var unitOfWork = new UnitOfWork(_dbConnectionFactory);
        var providers = unitOfWork.OAuthProviders.GetEnabledProvidersAsync().GetAwaiter().GetResult();
        foreach (var providerConfig in providers)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(providerConfig.ClientId))
                {
                    continue;
                }

                var provider = _oauthProviderFactoryService.CreateProvider(providerConfig);
                _authenticationService.RegisterProvider(provider);
            }
            catch
            {
                // Ignore invalid provider configuration and continue loading the rest.
            }
        }
    }
}


using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Core.Localization;
using ExportAzureWiki.Services;
using ExportAzureWiki.Wpf.Commands;
using System.Text.Json;

namespace ExportAzureWiki.Wpf.Views.Dialogs;

public partial class AiProviderEditDialog : Window
{
    private readonly AiProviderEditModel _model;

    public AiProviderEditDialog(AiProvider source, bool isNew, IAiProviderProbeService probe)
    {
        InitializeComponent();
        _model = new AiProviderEditModel(source, isNew, probe);
        DataContext = _model;
    }

    public AiProvider Result => _model.ToAiProvider();

    private void BtnSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_model.DisplayName) || string.IsNullOrWhiteSpace(_model.ProviderName))
        {
            MessageBox.Show(
                AppText.S("wpf.providers.status.validation_ai", "Provider Name and Display Name are required."),
                AppText.S("common.validation", "Validation"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class AiProviderEditModel : INotifyPropertyChanged
{
    private readonly int _id;
    private readonly DateTime _createdAt;
    private readonly IAiProviderProbeService _probe;
    private readonly bool _initializing;
    private string _displayName;
    private string _providerName;
    private AiProviderPreset? _selectedPreset;
    private string _endpointUrl;
    private string _apiKey;
    private string _modelName;
    private string _apiVersion;
    private string _organizationId;
    private string _configurationJson;
    private string _temperatureValue;
    private string _maxTokensValue;
    private string _topPValue;
    private string _timeoutSecondsValue;
    private string _probeStatus = string.Empty;
    private bool _isProbing;
    private bool _isEnabled;
    private bool _isDefault;

    public AiProviderEditModel(AiProvider source, bool isNew, IAiProviderProbeService probe)
    {
        _initializing = true;
        _probe = probe;
        _id = source.Id;
        _createdAt = source.CreatedAt;
        _displayName = source.DisplayName;
        _providerName = string.IsNullOrWhiteSpace(source.ProviderName) ? "OpenAICompatible" : source.ProviderName;
        _endpointUrl = source.EndpointUrl ?? string.Empty;
        _apiKey = source.ApiKey ?? string.Empty;
        _modelName = source.ModelName ?? string.Empty;
        _apiVersion = source.ApiVersion ?? string.Empty;
        _organizationId = source.OrganizationId ?? string.Empty;
        _configurationJson = source.ConfigurationJson ?? string.Empty;
        (_temperatureValue, _maxTokensValue, _topPValue, _timeoutSecondsValue) = ParseRuntimeOptions(source.ConfigurationJson);
        _isEnabled = source.IsEnabled || isNew;
        _isDefault = source.IsDefault;
        DialogTitle = isNew
            ? AppText.S("wpf.providers.ai.dialog.new.title", "New AI Provider")
            : AppText.S("wpf.providers.ai.dialog.edit.title", "Edit AI Provider");

        if (isNew && string.IsNullOrWhiteSpace(source.ProviderName))
        {
            _providerName = "OpenAI";
            _displayName = AppText.S("wpf.providers.ai.default_display.new", "New AI Provider");
            _temperatureValue = "0.20";
            _maxTokensValue = "2000";
            _topPValue = "1.00";
            _timeoutSecondsValue = "120";
        }

        _selectedPreset = AiProviderCatalog.Find(_providerName) ?? AiProviderCatalog.Find("OpenAICompatible");
        _providerName = _selectedPreset?.Key ?? "OpenAICompatible";

        LoadModelsCommand = new RelayCommand(async () => await LoadModelsAsync(), () => !IsProbing);
        TestCommand = new RelayCommand(async () => await TestConnectionAsync(), () => !IsProbing);

        _initializing = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DialogTitle { get; }
    public string DisplayNameText => AppText.S("wpf.providers.ai.display_name", "Display name");
    public string ProviderNameText => AppText.S("wpf.providers.ai.provider_name", "Provider");
    public string EndpointText => AppText.S("wpf.providers.ai.endpoint", "Endpoint URL");
    public string ApiKeyText => AppText.S("wpf.providers.ai.api_key", "API key");
    public string ModelText => AppText.S("wpf.providers.ai.model", "Model");
    public string ApiVersionText => AppText.S("wpf.providers.ai.api_version", "API version");
    public string OrganizationText => AppText.S("wpf.providers.ai.organization", "Organization ID");
    public string TemperatureText => AppText.S("wpf.providers.ai.temperature", "Temperature");
    public string MaxTokensText => AppText.S("wpf.providers.ai.max_tokens", "Max tokens");
    public string TopPText => AppText.S("wpf.providers.ai.top_p", "Top P");
    public string TimeoutSecondsText => AppText.S("wpf.providers.ai.timeout_seconds", "Timeout (s)");
    public string EnabledText => AppText.S("common.enabled", "Enabled");
    public string DefaultText => AppText.S("common.default", "Default");
    public string SaveText => AppText.S("common.save", "Save");
    public string CancelText => AppText.S("common.cancel", "Cancel");
    public string LoadModelsText => AppText.S("wpf.providers.ai.load_models", "Load models");
    public string TestText => AppText.S("wpf.providers.ai.test", "Test connection");
    public string HelpText => AppText.S("wpf.providers.ai.help", "Pick a provider to pre-fill the endpoint, set the API key, then Load models / Test. Local servers (Ollama, LM Studio) need no key. The list is a starting point -- use Custom for any OpenAI-compatible endpoint.");

    public IReadOnlyList<AiProviderPreset> ProviderPresets { get; } = AiProviderCatalog.Presets;
    public ObservableCollection<string> AvailableModels { get; } = [];

    public ICommand LoadModelsCommand { get; }
    public ICommand TestCommand { get; }

    public AiProviderPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!Set(ref _selectedPreset, value) || value == null)
            {
                return;
            }

            ProviderName = value.Key;

            // Picking a preset pre-fills the endpoint (explicit user action).
            // During initialization we keep whatever the stored provider had.
            if (!_initializing)
            {
                EndpointUrl = value.ChatEndpoint;
                if (value.IsAzure && string.IsNullOrWhiteSpace(ApiVersion))
                {
                    ApiVersion = "2024-10-21";
                }
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set => Set(ref _displayName, value);
    }

    public string ProviderName
    {
        get => _providerName;
        set => Set(ref _providerName, value);
    }

    public string EndpointUrl
    {
        get => _endpointUrl;
        set => Set(ref _endpointUrl, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => Set(ref _apiKey, value);
    }

    public string ModelName
    {
        get => _modelName;
        set => Set(ref _modelName, value);
    }

    public string ApiVersion
    {
        get => _apiVersion;
        set => Set(ref _apiVersion, value);
    }

    public string OrganizationId
    {
        get => _organizationId;
        set => Set(ref _organizationId, value);
    }

    public string TemperatureValue
    {
        get => _temperatureValue;
        set => Set(ref _temperatureValue, value);
    }

    public string MaxTokensValue
    {
        get => _maxTokensValue;
        set => Set(ref _maxTokensValue, value);
    }

    public string TopPValue
    {
        get => _topPValue;
        set => Set(ref _topPValue, value);
    }

    public string TimeoutSecondsValue
    {
        get => _timeoutSecondsValue;
        set => Set(ref _timeoutSecondsValue, value);
    }

    public string ProbeStatus
    {
        get => _probeStatus;
        private set => Set(ref _probeStatus, value);
    }

    public bool IsProbing
    {
        get => _isProbing;
        private set
        {
            if (Set(ref _isProbing, value))
            {
                (LoadModelsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (TestCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }

    public bool IsDefault
    {
        get => _isDefault;
        set => Set(ref _isDefault, value);
    }

    private async Task LoadModelsAsync()
    {
        if (IsProbing)
        {
            return;
        }

        try
        {
            IsProbing = true;
            ProbeStatus = AppText.S("wpf.providers.ai.status.loading_models", "Loading models...");
            var models = await _probe.ListModelsAsync(ToAiProvider());
            PopulateModels(models);
            ProbeStatus = string.Format(
                AppText.S("wpf.providers.ai.status.models_loaded", "{0} model(s) loaded."),
                models.Count);
            if (string.IsNullOrWhiteSpace(ModelName) && models.Count > 0)
            {
                ModelName = models[0];
            }
        }
        catch (Exception ex)
        {
            ProbeStatus = string.Format(AppText.S("wpf.providers.ai.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            IsProbing = false;
        }
    }

    private async Task TestConnectionAsync()
    {
        if (IsProbing)
        {
            return;
        }

        try
        {
            IsProbing = true;
            ProbeStatus = AppText.S("wpf.providers.ai.status.testing", "Testing connection...");
            var result = await _probe.TestAsync(ToAiProvider());
            ProbeStatus = result.Message;
            if (result.Models.Count > 0)
            {
                PopulateModels(result.Models);
            }
        }
        catch (Exception ex)
        {
            ProbeStatus = string.Format(AppText.S("wpf.providers.ai.status.error", "Error: {0}"), ex.Message);
        }
        finally
        {
            IsProbing = false;
        }
    }

    private void PopulateModels(IReadOnlyList<string> models)
    {
        AvailableModels.Clear();
        foreach (var model in models)
        {
            AvailableModels.Add(model);
        }
    }

    public AiProvider ToAiProvider()
    {
        return new AiProvider
        {
            Id = _id,
            DisplayName = DisplayName?.Trim() ?? string.Empty,
            ProviderName = ProviderName?.Trim() ?? string.Empty,
            EndpointUrl = EndpointUrl?.Trim(),
            ApiKey = ApiKey?.Trim(),
            ModelName = ModelName?.Trim(),
            ApiVersion = ApiVersion?.Trim(),
            OrganizationId = OrganizationId?.Trim(),
            ConfigurationJson = BuildRuntimeOptionsJson(),
            IsEnabled = IsEnabled,
            IsDefault = IsDefault,
            CreatedAt = _createdAt == default ? DateTime.Now : _createdAt,
            LastModifiedAt = DateTime.Now
        };
    }

    private static (string temperature, string maxTokens, string topP, string timeoutSeconds) ParseRuntimeOptions(string? configurationJson)
    {
        var temperature = "0.20";
        var maxTokens = "2000";
        var topP = "1.00";
        var timeoutSeconds = "120";
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return (temperature, maxTokens, topP, timeoutSeconds);
        }

        try
        {
            using var doc = JsonDocument.Parse(configurationJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("temperature", out var temp) && temp.TryGetDouble(out var t))
            {
                temperature = t.ToString("0.##");
            }

            if (root.TryGetProperty("max_tokens", out var max) && max.TryGetInt32(out var m))
            {
                maxTokens = m.ToString();
            }

            if (root.TryGetProperty("top_p", out var top) && top.TryGetDouble(out var p))
            {
                topP = p.ToString("0.##");
            }

            if (root.TryGetProperty("timeout_seconds", out var timeout) && timeout.TryGetInt32(out var s))
            {
                timeoutSeconds = s.ToString();
            }
        }
        catch
        {
            // Keep defaults for invalid payloads.
        }

        return (temperature, maxTokens, topP, timeoutSeconds);
    }

    private string BuildRuntimeOptionsJson()
    {
        var temperature = ParseDoubleOrDefault(TemperatureValue, 0.20, 0, 2);
        var maxTokens = ParseIntOrDefault(MaxTokensValue, 2000, 1, 32768);
        var topP = ParseDoubleOrDefault(TopPValue, 1.00, 0, 1);
        var timeoutSeconds = ParseIntOrDefault(TimeoutSecondsValue, 120, 10, 600);

        return JsonSerializer.Serialize(new
        {
            temperature,
            max_tokens = maxTokens,
            top_p = topP,
            timeout_seconds = timeoutSeconds
        });
    }

    private static double ParseDoubleOrDefault(string? value, double fallback, double min, double max)
    {
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            if (!double.TryParse(value, out parsed))
            {
                return fallback;
            }
        }

        return Math.Clamp(parsed, min, max);
    }

    private static int ParseIntOrDefault(string? value, int fallback, int min, int max)
    {
        if (!int.TryParse(value, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

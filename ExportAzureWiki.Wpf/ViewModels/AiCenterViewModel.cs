using System.Windows.Input;
using System.Linq;
using ExportAzureWiki.Core.Services;
using ExportAzureWiki.Wpf.Commands;

namespace ExportAzureWiki.Wpf.ViewModels;

public sealed class AiCenterViewModel : ViewModelBase
{
    private readonly WorkspaceViewModel _workspace;
    private readonly IAiTextGenerationService _service;
    private readonly IAdminCatalogService _adminCatalogService;
    private string _status = AppText.S("wpf.ai.status.ready", "Ready");
    private string _resultMarkdown = string.Empty;
    private bool _isBusy;
    private bool _hasConfiguredProvider;
    private int _directQuestions = 3;
    private int _multipleChoiceQuestions = 5;
    private string _suggestedTitle = string.Empty;
    private string _question = string.Empty;

    public AiCenterViewModel(
        WorkspaceViewModel workspace,
        IAiTextGenerationService service,
        IAdminCatalogService adminCatalogService)
    {
        _workspace = workspace;
        _service = service;
        _adminCatalogService = adminCatalogService;
        _workspace.PropertyChanged += WorkspaceOnPropertyChanged;

        GenerateSummaryCurrentCommand = new RelayCommand(
            async () => await GenerateSummaryAsync(AiOperationScope.CurrentPage),
            () => CanRunAiActions);
        GenerateSummaryAllCommand = new RelayCommand(
            async () => await GenerateSummaryAsync(AiOperationScope.AllPagesSingle),
            () => CanRunAiActions);
        GenerateIndexCurrentCommand = new RelayCommand(
            async () => await GenerateIndexAsync(AiOperationScope.CurrentPage),
            () => CanRunAiActions);
        GenerateIndexAllCommand = new RelayCommand(
            async () => await GenerateIndexAsync(AiOperationScope.AllPagesSingle),
            () => CanRunAiActions);
        GenerateQuizCurrentCommand = new RelayCommand(
            async () => await GenerateQuizAsync(AiOperationScope.CurrentPage),
            () => CanRunAiActions);
        GenerateQuizAllCommand = new RelayCommand(
            async () => await GenerateQuizAsync(AiOperationScope.AllPagesSingle),
            () => CanRunAiActions);
        AskQuestionCurrentCommand = new RelayCommand(
            async () => await AskQuestionAsync(AiOperationScope.CurrentPage),
            () => CanAsk);
        AskQuestionAllCommand = new RelayCommand(
            async () => await AskQuestionAsync(AiOperationScope.AllPagesSingle),
            () => CanAsk);
    }

    public event EventHandler<AiResultReadyEventArgs>? ResultReady;

    public string Title => AppText.S("wpf.ai.title", "AI Center");
    public string SummaryText => AppText.S("wpf.ai.summary", "Generate Summary");
    public string IndexText => AppText.S("wpf.ai.index", "Generate Index");
    public string QuizText => AppText.S("wpf.ai.quiz", "Generate Quiz");
    public string ScopeCurrentText => AppText.S("wpf.ai.scope.current", "Current page");
    public string ScopeAllSingleText => AppText.S("wpf.ai.scope.all_single", "All pages (single)");
    public string DirectQuestionsText => AppText.S("wpf.ai.direct_questions", "Direct:");
    public string MultipleChoiceQuestionsText => AppText.S("wpf.ai.multiple_questions", "Multiple Choice:");
    public string SuggestedTitleText => AppText.S("wpf.ai.suggested_title", "Extra page title:");
    public bool HasConfiguredProvider
    {
        get => _hasConfiguredProvider;
        private set
        {
            _hasConfiguredProvider = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRunAiActions));
            RaiseAiCommandStates();
        }
    }

    public bool CanRunAiActions => HasConfiguredProvider && _workspace.HasLoadedPage && !IsBusy;

    /// <summary>"Ask the pages" also requires a non-empty question.</summary>
    public bool CanAsk => CanRunAiActions && !string.IsNullOrWhiteSpace(Question);

    public string QuestionLabelText => AppText.S("wpf.ai.question", "Question about the loaded pages:");
    public string AskText => AppText.S("wpf.ai.ask", "Ask");

    public string Question
    {
        get => _question;
        set
        {
            _question = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAsk));
            RaiseAiCommandStates();
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public string ResultMarkdown
    {
        get => _resultMarkdown;
        private set
        {
            _resultMarkdown = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRunAiActions));
            RaiseAiCommandStates();
        }
    }

    public int DirectQuestions
    {
        get => _directQuestions;
        set
        {
            _directQuestions = Math.Max(0, value);
            OnPropertyChanged();
        }
    }

    public int MultipleChoiceQuestions
    {
        get => _multipleChoiceQuestions;
        set
        {
            _multipleChoiceQuestions = Math.Max(0, value);
            OnPropertyChanged();
        }
    }

    public string SuggestedTitle
    {
        get => _suggestedTitle;
        set
        {
            _suggestedTitle = value;
            OnPropertyChanged();
        }
    }

    public ICommand GenerateSummaryCurrentCommand { get; }
    public ICommand GenerateSummaryAllCommand { get; }
    public ICommand GenerateIndexCurrentCommand { get; }
    public ICommand GenerateIndexAllCommand { get; }
    public ICommand GenerateQuizCurrentCommand { get; }
    public ICommand GenerateQuizAllCommand { get; }
    public ICommand AskQuestionCurrentCommand { get; }
    public ICommand AskQuestionAllCommand { get; }

    public async Task RefreshProviderAvailabilityAsync()
    {
        try
        {
            var providers = await _adminCatalogService.LoadAiProvidersAsync();
            HasConfiguredProvider = providers.Any(p =>
                p.IsEnabled &&
                !string.IsNullOrWhiteSpace(p.ApiKey) &&
                !string.IsNullOrWhiteSpace(p.ModelName));
        }
        catch
        {
            HasConfiguredProvider = false;
        }

        RaiseAiCommandStates();
    }

    private void WorkspaceOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.HasLoadedPage)
            or nameof(WorkspaceViewModel.CurrentPageMarkdown)
            or nameof(WorkspaceViewModel.CurrentPageHtml))
        {
            OnPropertyChanged(nameof(CanRunAiActions));
            OnPropertyChanged(nameof(CanAsk));
            RaiseAiCommandStates();
        }
    }

    private void RaiseAiCommandStates()
    {
        (GenerateSummaryCurrentCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (GenerateSummaryAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (GenerateIndexCurrentCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (GenerateIndexAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (GenerateQuizCurrentCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (GenerateQuizAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AskQuestionCurrentCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (AskQuestionAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task GenerateSummaryAsync(AiOperationScope scope)
    {
        var sources = await ResolveSourcesAsync(scope);
        if (sources.Count == 0)
        {
            return;
        }

        var content = scope == AiOperationScope.CurrentPage
            ? sources[0].Content
            : string.Join("\n\n---\n\n", sources.Select(s => $"# {s.Title}\n\n{s.Content}"));

        await RunAndPresentAsync(
            async () => await _service.GenerateSummaryAsync(content),
            AppText.S("wpf.ai.status.generating_summary", "Generating summary..."),
            AppText.S("wpf.ai.suggested_title.summary", "AI Summary"));
    }

    private async Task GenerateIndexAsync(AiOperationScope scope)
    {
        var sources = await ResolveSourcesAsync(scope);
        if (sources.Count == 0)
        {
            return;
        }

        // For index, avoid injecting per-page heading noise.
        var content = scope == AiOperationScope.CurrentPage
            ? sources[0].Content
            : string.Join("\n\n---\n\n", sources.Select(s => s.Content));

        await RunAndPresentAsync(
            async () => await _service.GenerateIndexAsync(content),
            AppText.S("wpf.ai.status.generating_index", "Generating index..."),
            AppText.S("wpf.ai.suggested_title.index", "AI Index"));
    }

    private async Task GenerateQuizAsync(AiOperationScope scope)
    {
        if (DirectQuestions + MultipleChoiceQuestions <= 0)
        {
            Status = AppText.S("wpf.ai.status.quiz_requires_questions", "Set at least one question.");
            return;
        }

        var sources = await ResolveSourcesAsync(scope);
        if (sources.Count == 0)
        {
            return;
        }

        var content = scope == AiOperationScope.CurrentPage
            ? sources[0].Content
            : string.Join("\n\n---\n\n", sources.Select(s => s.Content));

        await RunAndPresentAsync(
            async () => await _service.GenerateQuizAsync(content, DirectQuestions, MultipleChoiceQuestions),
            AppText.S("wpf.ai.status.generating_quiz", "Generating quiz..."),
            AppText.S("wpf.ai.suggested_title.quiz", "AI Quiz"));
    }

    private async Task AskQuestionAsync(AiOperationScope scope)
    {
        var question = Question?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(question))
        {
            Status = AppText.S("wpf.ai.status.question_required", "Type a question first.");
            return;
        }

        var sources = await ResolveSourcesAsync(scope);
        if (sources.Count == 0)
        {
            return;
        }

        // Always include page titles so the model can cite its sources.
        var content = string.Join("\n\n---\n\n", sources.Select(s => $"# {s.Title}\n\n{s.Content}"));

        await RunAndPresentAsync(
            async () => await _service.AnswerQuestionAsync(question, content),
            AppText.S("wpf.ai.status.answering", "Answering..."),
            AppText.S("wpf.ai.suggested_title.answer", "AI Answer"));
    }

    private async Task<IReadOnlyList<AiSourceItem>> ResolveSourcesAsync(AiOperationScope scope)
    {
        if (scope == AiOperationScope.CurrentPage)
        {
            var content = _workspace.GetCurrentAiSourceContent();
            if (string.IsNullOrWhiteSpace(content))
            {
                Status = AppText.S("wpf.ai.status.no_content", "Load a page in Workspace first.");
                return [];
            }

            return [new AiSourceItem(_workspace.CurrentDocumentTitleOrFallback(), content)];
        }

        var all = await _workspace.GetAllLoadedAiSourceContentAsync();
        if (all.Count == 0)
        {
            Status = AppText.S("wpf.ai.status.no_content", "Load a page in Workspace first.");
            return [];
        }

        return all;
    }

    private async Task RunAndPresentAsync(Func<Task<string>> operation, string runningStatus, string suggestedTitle)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            Status = runningStatus;
            _workspace.SetExternalBusy(runningStatus);
            _workspace.SetExternalStatus(runningStatus);
            var result = await operation();
            ResultMarkdown = result;
            SuggestedTitle = suggestedTitle;

            if (string.IsNullOrWhiteSpace(result))
            {
                Status = AppText.S("wpf.ai.status.no_result", "Generate content first.");
                _workspace.SetExternalStatus(Status);
                return;
            }

            Status = AppText.S("wpf.ai.status.done", "Done.");
            _workspace.SetExternalStatus(Status);
            ResultReady?.Invoke(this, new AiResultReadyEventArgs(SuggestedTitle, ResultMarkdown));
        }
        catch (Exception ex)
        {
            Status = string.Format(AppText.S("wpf.ai.status.error", "Error: {0}"), ex.Message);
            _workspace.SetExternalStatus(Status);
        }
        finally
        {
            IsBusy = false;
            _workspace.ClearExternalBusy();
        }
    }
}

public enum AiOperationScope
{
    CurrentPage = 0,
    AllPagesSingle = 1
}

public sealed record AiSourceItem(string Title, string Content);

public sealed record AiResultReadyEventArgs(string SuggestedTitle, string MarkdownContent);

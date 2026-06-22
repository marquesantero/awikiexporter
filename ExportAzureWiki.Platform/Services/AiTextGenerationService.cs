using ExportAzureWiki.Platform.Backend;
using ExportAzureWiki.Core.Services;

namespace ExportAzureWiki.Platform.Services;

public sealed class AiTextGenerationService : IAiTextGenerationService
{
    private readonly IAiBackend _backend;

    public AiTextGenerationService()
        : this(new AiBackend())
    {
    }

    internal AiTextGenerationService(IAiBackend backend)
    {
        _backend = backend;
    }

    public Task<string> GenerateSummaryAsync(string sourceContent)
    {
        return _backend.GenerateSummaryAsync(sourceContent);
    }

    public Task<string> GenerateIndexAsync(string sourceContent)
    {
        return _backend.GenerateIndexAsync(sourceContent);
    }

    public Task<string> GenerateQuizAsync(string sourceContent, int directQuestions, int multipleChoiceQuestions)
    {
        return _backend.GenerateQuizAsync(sourceContent, directQuestions, multipleChoiceQuestions);
    }

    public Task<string> AnswerQuestionAsync(string question, string sourceContent)
    {
        return _backend.AnswerQuestionAsync(question, sourceContent);
    }
}








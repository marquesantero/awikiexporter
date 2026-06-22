using ExportAzureWiki.Data;
using ExportAzureWiki.Services;

namespace ExportAzureWiki.Platform.Backend;

internal sealed class AiBackend : IAiBackend
{
    private readonly AiTextOperationsService _service;

    public AiBackend()
        : this(new DbConnectionFactory())
    {
    }

    internal AiBackend(IDbConnectionFactory dbConnectionFactory)
        : this(CreateDefaultService(dbConnectionFactory))
    {
    }

    internal AiBackend(AiTextOperationsService service)
    {
        _service = service;
    }

    private static AiTextOperationsService CreateDefaultService(IDbConnectionFactory dbConnectionFactory)
    {
        var unitOfWork = new UnitOfWork(dbConnectionFactory);
        var providerService = new AiProviderService(unitOfWork);
        return new AiTextOperationsService(providerService);
    }

    public Task<string> GenerateSummaryAsync(string sourceContent)
    {
        return _service.GenerateSummaryAsync(sourceContent);
    }

    public Task<string> GenerateIndexAsync(string sourceContent)
    {
        return _service.GenerateIndexAsync(sourceContent);
    }

    public Task<string> GenerateQuizAsync(string sourceContent, int directQuestions, int multipleChoiceQuestions)
    {
        return _service.GenerateQuizAsync(sourceContent, directQuestions, multipleChoiceQuestions);
    }

    public Task<string> AnswerQuestionAsync(string question, string sourceContent)
    {
        return _service.AnswerQuestionAsync(question, sourceContent);
    }
}







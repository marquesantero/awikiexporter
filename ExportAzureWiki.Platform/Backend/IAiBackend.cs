namespace ExportAzureWiki.Platform.Backend;

internal interface IAiBackend
{
    Task<string> GenerateSummaryAsync(string sourceContent);
    Task<string> GenerateIndexAsync(string sourceContent);
    Task<string> GenerateQuizAsync(string sourceContent, int directQuestions, int multipleChoiceQuestions);
    Task<string> AnswerQuestionAsync(string question, string sourceContent);
}









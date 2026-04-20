namespace DevPulse.Core.Services;

public static class PollErrorClassifier
{
    public static string Classify(Exception ex)
    {
        if (ex is HttpRequestException hre && hre.StatusCode.HasValue)
        {
            var code = (int)hre.StatusCode.Value;
            return code switch
            {
                401 => $"Authentication failure ({code})",
                403 => $"Authorization failure ({code})",
                429 => $"Rate limited ({code})",
                >= 500 => $"Server error ({code})",
                _ => ex.Message
            };
        }
        return ex.Message;
    }
}

namespace CodebaseExplorer.Application.AI;

/// <summary>Error codes of the optional AI features (AI_ARCHITECTURE.md).</summary>
public static class AiErrorCodes
{
    /// <summary>AI is switched off (the default); nothing is sent anywhere.</summary>
    public const string Disabled = "ai.disabled";

    /// <summary>The configured provider id is not known.</summary>
    public const string ProviderUnknown = "ai.provider-unknown";

    /// <summary>The endpoint is not an absolute http(s) URL without credentials.</summary>
    public const string EndpointInvalid = "ai.endpoint-invalid";

    /// <summary>No model is selected.</summary>
    public const string ModelMissing = "ai.model-missing";

    /// <summary>A setting is outside its allowed range.</summary>
    public const string SettingInvalid = "ai.setting-invalid";

    /// <summary>The endpoint is not on this machine and the repository has no consent to send context there.</summary>
    public const string ConsentRequired = "ai.consent-required";

    /// <summary>The provider could not be reached or answered with an error.</summary>
    public const string ProviderUnavailable = "ai.provider-unavailable";

    /// <summary>The selected model is not available at the provider.</summary>
    public const string ModelNotFound = "ai.model-not-found";

    /// <summary>The model request failed or timed out.</summary>
    public const string RequestFailed = "ai.request-failed";

    /// <summary>The question is empty or too long.</summary>
    public const string QuestionInvalid = "ai.question-invalid";

    /// <summary>The conversation does not exist (any more).</summary>
    public const string ConversationNotFound = "ai.conversation-not-found";
}

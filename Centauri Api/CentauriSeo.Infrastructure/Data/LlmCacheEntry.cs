using System;

namespace CentauriSeo.Infrastructure.Data;

public class LlmCacheEntry
{
    public int Id { get; set; }
    // SHA256 of request payload + model/type
    public string RequestKey { get; set; } = "";
    // Original request payload string (for debugging)
    public string RequestText { get; set; } = "";
    // Raw response text returned from the LLM API
    public string ResponseText { get; set; } = "";
    // Which model/provider produced this (e.g. "openai:gpt-4", "gemini:pro", "perplexity:sonar-pro")
    public string Provider { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
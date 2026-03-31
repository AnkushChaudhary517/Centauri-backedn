using CentauriSeo.Core.Models.Output;
using CentauriSeo.Core.Models.Outputs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.Services;

/// <summary>
/// Service for loading and managing mock data for testing and development.
/// Allows simulation of API responses without calling actual LLM services.
/// </summary>
public interface IMockDataService
{
    /// <summary>
    /// Check if mock mode is enabled.
    /// </summary>
    bool IsMockModeEnabled { get; }

    /// <summary>
    /// Load mock analysis response data (SeoResponse).
    /// </summary>
    /// <returns>Mock SeoResponse or null if load fails</returns>
    Task<SeoResponse> GetMockAnalysisResponseAsync();

    /// <summary>
    /// Load mock recommendations response data (RecommendationResponseDTO).
    /// </summary>
    /// <returns>Mock RecommendationResponseDTO or null if load fails</returns>
    Task<RecommendationResponseDTO> GetMockRecommendationsResponseAsync();
}

public class MockDataService : IMockDataService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MockDataService> _logger;
    private SeoResponse _cachedAnalysisResponse;
    private RecommendationResponseDTO _cachedRecommendationsResponse;
    private readonly object _lockAnalysis = new object();
    private readonly object _lockRecommendations = new object();

    public bool IsMockModeEnabled { get; }

    public MockDataService(IConfiguration configuration, ILogger<MockDataService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Read mock mode setting from appsettings
        IsMockModeEnabled = _configuration.GetValue<bool>("MockMode:Enabled", false);
        
        if (IsMockModeEnabled)
        {
            _logger.LogInformation("✓ Mock Mode is ENABLED - API will return mock data");
        }
        else
        {
            _logger.LogInformation("✓ Mock Mode is DISABLED - API will call real services");
        }
    }

    /// <summary>
    /// Load mock analysis response from JSON file.
    /// Uses in-memory caching after first load.
    /// </summary>
    public async Task<SeoResponse> GetMockAnalysisResponseAsync()
    {
        if (!IsMockModeEnabled)
        {
            _logger.LogWarning("Mock mode is disabled, cannot load mock analysis data");
            return null;
        }

        try
        {
            // Return cached response if available
            if (_cachedAnalysisResponse != null)
            {
                _logger.LogDebug("Returning cached mock analysis response");
                return _cachedAnalysisResponse;
            }

            lock (_lockAnalysis)
            {
                // Double-check after acquiring lock
                if (_cachedAnalysisResponse != null)
                    return _cachedAnalysisResponse;

                var filePath = GetMockDataFilePath("MockMode:AnalysisResponsePath", "Data/Response.json");
                
                if (!File.Exists(filePath))
                {
                    _logger.LogError($"Mock analysis data file not found at: {filePath}");
                    return null;
                }

                _logger.LogDebug($"Loading mock analysis response from: {filePath}");

                var jsonContent = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
                };

                _cachedAnalysisResponse = JsonSerializer.Deserialize<SeoResponse>(jsonContent, options);

                if (_cachedAnalysisResponse != null)
                {
                    _logger.LogInformation($"✓ Mock analysis response loaded successfully ({jsonContent.Length} bytes)");
                }
                else
                {
                    _logger.LogError("Failed to deserialize mock analysis response");
                }

                return _cachedAnalysisResponse;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading mock analysis response");
            return null;
        }
    }

    /// <summary>
    /// Load mock recommendations response from JSON file.
    /// Uses in-memory caching after first load.
    /// </summary>
    public async Task<RecommendationResponseDTO> GetMockRecommendationsResponseAsync()
    {
        if (!IsMockModeEnabled)
        {
            _logger.LogWarning("Mock mode is disabled, cannot load mock recommendations data");
            return null;
        }

        try
        {
            // Return cached response if available
            if (_cachedRecommendationsResponse != null)
            {
                _logger.LogDebug("Returning cached mock recommendations response");
                return _cachedRecommendationsResponse;
            }

            lock (_lockRecommendations)
            {
                // Double-check after acquiring lock
                if (_cachedRecommendationsResponse != null)
                    return _cachedRecommendationsResponse;

                var filePath = GetMockDataFilePath("MockMode:RecommendationsResponsePath", "Data/Recommendations.json");

                if (!File.Exists(filePath))
                {
                    _logger.LogError($"Mock recommendations data file not found at: {filePath}");
                    return null;
                }

                _logger.LogDebug($"Loading mock recommendations response from: {filePath}");

                var jsonContent = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                };

                var mockResponse = JsonSerializer.Deserialize<RecommendationResponseDTO>(jsonContent, options);

                if (mockResponse != null)
                {
                    // Ensure status is set correctly
                    mockResponse.Status = "Completed";
                    mockResponse.RequestId = Guid.NewGuid().ToString();
                    _cachedRecommendationsResponse = mockResponse;
                    _logger.LogInformation($"✓ Mock recommendations response loaded successfully ({jsonContent.Length} bytes)");
                }
                else
                {
                    _logger.LogError("Failed to deserialize mock recommendations response");
                }

                return _cachedRecommendationsResponse;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading mock recommendations response");
            return null;
        }
    }

    /// <summary>
    /// Get the full file path for mock data, with fallback to default.
    /// </summary>
    private string GetMockDataFilePath(string configKey, string defaultPath)
    {
        // Try to get path from configuration
        var configPath = _configuration.GetValue<string>(configKey);
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return configPath;
        }

        // Use default path, combining with base directory if relative
        if (Path.IsPathRooted(defaultPath))
        {
            return defaultPath;
        }

        // Combine with application base directory
        var baseDirectory = AppContext.BaseDirectory;
        return Path.Combine(baseDirectory, defaultPath);
    }
}

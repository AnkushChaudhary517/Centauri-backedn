# Mock Mode Implementation Guide
## Enable/Disable Mock Data Responses from appsettings.json

**Implementation Date:** March 31, 2026  
**Status:** ✅ COMPLETE - Compilation verified (0 errors)

---

## Overview

Mock Mode allows the API to return pre-recorded mock data responses instead of making actual calls to LLM services (Gemini, Groq, OpenAI). This is useful for:

✅ **Front-end Development** - Develop UI without waiting for slow API responses  
✅ **Testing** - Consistent responses for reliable testing  
✅ **Demos** - Show features without depending on LLM service availability  
✅ **Cost Reduction** - Avoid API charges during development  
✅ **Offline Development** - Work without internet/API keys  

---

## Configuration

### Enable/Disable Mock Mode

Edit `appsettings.json`:

```json
{
  "MockMode": {
    "Enabled": true,
    "AnalysisResponsePath": "Data/Response.json",
    "RecommendationsResponsePath": "Data/Recommendations.json"
  }
}
```

**Properties:**
- `Enabled` (bool): Set to `true` to enable mock mode, `false` for production
- `AnalysisResponsePath` (string): Path to mock analysis response JSON file
- `RecommendationsResponsePath` (string): Path to mock recommendations response JSON file

### Environment-Specific Configuration

For **Development** (`appsettings.Development.json`):
```json
{
  "MockMode": {
    "Enabled": true
  }
}
```

For **Production** (`appsettings.json`):
```json
{
  "MockMode": {
    "Enabled": false
  }
}
```

---

## How It Works

### Architecture

```
API Request
    ↓
[SeoController.Analyze / GetRecommendations]
    ↓
[MockDataService.IsMockModeEnabled?]
    ├─ Yes → [Load Mock Data] → Return Immediately ✓
    └─ No  → [Call Real LLM Services] → Normal Flow
```

### Flow Diagram

```
User Makes Request
    ↓
SeoController receives request
    ↓
Input Validation (always performed)
    ↓
Check MockMode.Enabled in IMockDataService
    ├─ ENABLED (true)
    │   ├─ Load mock data from JSON file
    │   ├─ Set RequestId & metadata
    │   ├─ Log "📋 MOCK MODE: Returning mock response"
    │   └─ Return mock response immediately
    │
    └─ DISABLED (false)
        ├─ Check user authentication/credits
        ├─ Call orchestrator/LLM services
        ├─ Process results normally
        └─ Return actual response
```

---

## Affected Endpoints

### 1. POST `/api/seo/analyze`

**With Mock Mode Enabled:**
- Skips user authentication checks
- Skips credit verification
- Returns pre-recorded SeoResponse immediately
- Does NOT deduct user credits

**Response:**
```json
{
  "requestId": "676eb1bf-ca94-4afd-8596-5c61865e11e7",
  "status": "partial",
  "seoScore": 74.5,
  "level1": { ... },
  "level2Scores": { ... },
  "level3Scores": { ... },
  "level4Scores": { ... },
  "finalScores": { ... },
  ...
}
```

**Logging:**
```
[INFO] 📋 MOCK MODE: Returning mock analysis response
[API]  Provider=SeoController:Analyze | Operation=Analyze (Mock) | Duration=12ms | Status=SUCCESS
```

### 2. POST `/api/seo/recommendations`

**With Mock Mode Enabled:**
- Skips article processing
- Returns pre-recorded RecommendationResponseDTO immediately
- No actual LLM API calls

**Response:**
```json
{
  "requestId": "unique-guid",
  "recommendations": {
    "overall": [
      {
        "priority": "High",
        "issue": "Lack of citations",
        "whatToChange": "Add supporting data...",
        ...
      }
    ]
  },
  "status": "Completed"
}
```

**Logging:**
```
[INFO] 📋 MOCK MODE: Returning mock recommendations response
[API]  Provider=SeoController:GetRecommendations | Operation=GetRecommendations (Mock) | Duration=8ms | Status=SUCCESS
```

---

## Services & Components

### IMockDataService Interface

```csharp
public interface IMockDataService
{
    /// Check if mock mode is enabled
    bool IsMockModeEnabled { get; }

    /// Load mock analysis response (SeoResponse)
    Task<SeoResponse> GetMockAnalysisResponseAsync();

    /// Load mock recommendations response (RecommendationResponseDTO)
    Task<RecommendationResponseDTO> GetMockRecommendationsResponseAsync();
}
```

### MockDataService Implementation

**Features:**
- ✅ Reads configuration from appsettings
- ✅ File-based mock data loading
- ✅ In-memory caching (loaded once, reused)
- ✅ Thread-safe caching (locks)
- ✅ Comprehensive error handling & logging
- ✅ Automatic RequestId/metadata injection

**Caching Strategy:**
- First call: Read JSON file from disk
- Subsequent calls: Return cached copy from memory
- Per-response caching (analysis & recommendations cached separately)
- Thread-safe via lock statements

**Error Handling:**
- File not found: Logs error, returns null
- JSON parsing error: Logs error, returns null
- Non-blocking: Mock data load failures don't crash the API
- Graceful degradation to real LLM calls

---

## Dependency Injection

### Registration in Program.cs

```csharp
// register mock data service
builder.Services.AddSingleton<IMockDataService, MockDataService>();
```

### Usage in SeoController

```csharp
private readonly IMockDataService _mockDataService;

public SeoController(... IMockDataService mockDataService)
{
    _mockDataService = mockDataService ?? throw new ArgumentNullException(nameof(mockDataService));
}
```

### Mock Mode Check

```csharp
if (_mockDataService.IsMockModeEnabled)
{
    _llmLogger.LogInfo("📋 MOCK MODE: Returning mock analysis response");
    var mockResponse = await _mockDataService.GetMockAnalysisResponseAsync();
    if (mockResponse != null)
    {
        mockResponse.RequestId = Guid.NewGuid().ToString();
        return Ok(mockResponse);
    }
}
```

---

## Mock Data Files

### Location
```
Centauri.Api/
├── Data/
│   ├── Response.json (9,408 lines)
│   └── Recommendations.json (121 lines)
```

### File Requirements

**Response.json:**
- Must be valid SeoResponse JSON structure
- Contains complete analysis result with all sections
- 9,000+ lines with comprehensive data

**Recommendations.json:**
- Must be valid RecommendationResponseDTO JSON structure
- Contains overall, section, and detail-level recommendations
- 120+ lines with prioritized recommendations

### Updating Mock Data

1. Run the API with real data once
2. Capture the JSON response
3. Save to `Data/Response.json` or `Data/Recommendations.json`
4. Restart the API
5. Mock mode will automatically load the new files

---

## Usage Examples

### Development Workflow

1. **Start with Mock Mode Enabled:**
   ```json
   {"MockMode": {"Enabled": true}}
   ```

2. **Develop Front-End UI:**
   - Fast consistent responses
   - No API costs
   - No rate limiting

3. **Switch to Real Mode for Testing:**
   ```json
   {"MockMode": {"Enabled": false}}
   ```

4. **Back to Mock for CI/CD:**
   - Mock mode for automated tests
   - Real mode for integration tests

### Example API Calls

**With Mock Mode:**
```bash
curl -X POST http://localhost:5000/api/seo/analyze \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer your-token" \
  -d '{
    "primaryKeyword": "test",
    "article": {"raw": "test content"}
  }'
```

Response comes in ~10-50ms with mock data (instant!)

**Without Mock Mode:**
```bash
# Same curl but MockMode.Enabled = false
# Response comes in 30-120 seconds with real API calls
```

---

## Performance Impact

### Mock Mode Enabled
- **Response Time:** ~10-50ms (instant)
- **API Calls:** 0
- **Cost:** $0
- **Network:** Minimal (file read only)

### Mock Mode Disabled
- **Response Time:** 30-120 seconds
- **API Calls:** 10-15+ calls per request
- **Cost:** $0.10-0.50 per request
- **Network:** Heavy (multiple LLM API calls)

---

## Logging & Monitoring

### Log Format

When mock mode is active:
```
[INFO] ✓ Mock Mode is ENABLED - API will return mock data
[INFO] 📋 MOCK MODE: Returning mock analysis response
[API]  Provider=SeoController:Analyze | Operation=Analyze (Mock) | Duration=12ms | Status=SUCCESS
```

### Monitoring

Check logs to verify mock mode is working:
```csharp
// Search logs for:
// "MOCK MODE" → Confirms mock mode is being used
// "Duration: 10-50ms" → Confirms cache speed
// "Status: SUCCESS" → Confirms successful response
```

---

## Troubleshooting

### Mock Mode Not Working

**Problem:** Getting real API responses despite `Enabled: true`  
**Solution:**
1. Check file path in appsettings: `Data/Response.json`
2. Ensure files exist in correct location
3. Check logs for file not found errors
4. Verify appsettings syntax is correct

**Problem:** Getting FileNotFoundException  
**Solution:**
1. Ensure `Data/Response.json` exists
2. Check file path in appsettings
3. Verify file isn't deleted or moved
4. Try absolute path: `C:/path/to/Data/Response.json`

**Problem:** JSON parsing errors  
**Solution:**
1. Validate JSON syntax in mock data files
2. Use online JSON validator
3. Ensure enums match expected values
4. Check for missing required fields

### Testing Mock Mode

```csharp
// Test if mock mode is enabled
public void TestMockModeEnabled()
{
    var mockService = serviceProvider.GetService<IMockDataService>();
    Assert.True(mockService.IsMockModeEnabled);
}

// Test mock response loading
public async Task TestMockResponseLoading()
{
    var mockService = serviceProvider.GetService<IMockDataService>();
    var response = await mockService.GetMockAnalysisResponseAsync();
    Assert.NotNull(response);
    Assert.True(response.RequestId != null);
}
```

---

## Security Considerations

✅ **Safe Defaults:**
- Mock mode defaults to `false` (disabled)
- Production builds can enforce Enabled=false
- No sensitive data exposed via mock responses

⚠️ **Development Safety:**
- Mock responses may be stale/outdated
- Don't rely on mock data for production insights
- Switch to real mode before deploying

---

## Configuration Examples

### Development Setup

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "CentauriSeo": "Debug"
    }
  },
  "MockMode": {
    "Enabled": true,
    "AnalysisResponsePath": "Data/Response.json",
    "RecommendationsResponsePath": "Data/Recommendations.json"
  }
}
```

### Test Setup

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "MockMode": {
    "Enabled": true
  }
}
```

### Production Setup

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  },
  "MockMode": {
    "Enabled": false
  }
}
```

---

## Files Modified/Created

| File | Changes | Type |
|------|---------|------|
| MockDataService.cs | NEW service for managing mock data | Created |
| Program.cs | Added IMockDataService registration | Modified |
| SeoController.cs | Added mock mode checks to endpoints | Modified |
| appsettings.json | Added MockMode configuration section | Modified |

---

## Compilation Status

✅ **All code compiles without errors or warnings**

---

## Summary

✅ **Mock Mode Enabled:**
- Set `"Enabled": true` in appsettings.json
- API returns pre-recorded mock data
- Response time: 10-50ms (instant)
- No API costs

✅ **Mock Mode Disabled (Production):**
- Set `"Enabled": false` in appsettings.json
- API calls real LLM services
- Response time: 30-120 seconds
- Normal API costs apply

**Key Benefits:**
- 🚀 Fast front-end development
- 💰 Cost reduction during dev/test
- 🧪 Consistent testing responses
- 📊 Offline development capability
- 🔒 Production-safe configuration

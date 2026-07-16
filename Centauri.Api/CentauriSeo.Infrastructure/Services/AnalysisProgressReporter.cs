using Microsoft.Extensions.Caching.Memory;
using System;

namespace CentauriSeo.Infrastructure.Services
{
 public interface IAnalysisProgressReporter
 {
 void Report(string requestId, int percentage, string stage, string message, bool isCompleted = false, bool isError = false, string errorDetail = null);
 AnalysisProgressModel Get(string requestId);
 }

 public class AnalysisProgressModel
 {
 public string RequestId { get; set; }
 public int Percentage { get; set; }
 public string Stage { get; set; }
 public string Message { get; set; }
 public bool IsCompleted { get; set; }
 public bool IsError { get; set; }
 public string ErrorDetail { get; set; }
 public DateTime TimestampUtc { get; set; }
 }

 public class MemoryAnalysisProgressReporter : IAnalysisProgressReporter
 {
 private readonly IMemoryCache _cache;
 public MemoryAnalysisProgressReporter(IMemoryCache cache)
 {
 _cache = cache;
 }

 public void Report(string requestId, int percentage, string stage, string message, bool isCompleted = false, bool isError = false, string errorDetail = null)
 {
 if (string.IsNullOrWhiteSpace(requestId)) return;
 try
 {
 var key = $"progress__{requestId}";
 var p = new AnalysisProgressModel
 {
 RequestId = requestId,
 Percentage = Math.Clamp(percentage,0,100),
 Stage = stage,
 Message = message,
 IsCompleted = isCompleted,
 IsError = isError,
 ErrorDetail = errorDetail,
 TimestampUtc = DateTime.UtcNow
 };
 _cache.Set(key, p, TimeSpan.FromHours(1));
 }
 catch { /* best-effort */ }
 }

 public AnalysisProgressModel Get(string requestId)
 {
 if (string.IsNullOrWhiteSpace(requestId)) return null;
 try
 {
 var key = $"progress__{requestId}";
 return _cache.Get<AnalysisProgressModel>(key);
 }
 catch
 {
 return null;
 }
 }
 }
}

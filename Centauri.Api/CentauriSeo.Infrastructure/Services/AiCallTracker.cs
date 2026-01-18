using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.Services
{
    using CentauriSeo.Core.Diagnostic;
    using Microsoft.AspNetCore.Http;
    using System.Diagnostics;
    using System.Text.Json;

    public class AiCallTracker
    {
        private readonly AiUsageRepository _repo;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AiCallTracker(AiUsageRepository repo, IHttpContextAccessor httpContextAccessor)
        {
            _repo = repo;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<T> TrackAsync<T>(
            object requestObj,
            Func<Task<(T result, object usage)>> apiCall,
            string provider = "gemini"
        )
        {
            var id = Guid.NewGuid().ToString();
            var sw = Stopwatch.StartNew();
            var ctx = _httpContextAccessor.HttpContext;

            var userId = ctx?.Items["UserId"]?.ToString()??Guid.NewGuid().ToString();
            var correlationId = ctx?.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
            var endpoint = ctx?.Request?.Path.ToString() ?? "";
            try
            {
               
                var (result, usage) = await apiCall();

                sw.Stop();

                await _repo.SaveAsync(new AiUsageRow
                {
                    Id = id,
                    CorrelationId = correlationId,
                    UserId = userId,
                    Provider = provider,
                    Endpoint = endpoint,

                    Usage = JsonSerializer.Serialize(usage),
                    Request = JsonSerializer.Serialize(requestObj),
                    Response = JsonSerializer.Serialize(result),

                    TimeTakenMs = sw.ElapsedMilliseconds,
                    Timestamp = DateTime.UtcNow
                });

                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();

                await _repo.SaveAsync(new AiUsageRow
                {
                    Id = id,
                    CorrelationId = correlationId,
                    UserId = userId,
                    Provider = provider,
                    Endpoint = endpoint,

                    Usage = "{}",
                    Request = JsonSerializer.Serialize(requestObj),
                    Response = JsonSerializer.Serialize(new { error = ex.Message }),

                    TimeTakenMs = sw.ElapsedMilliseconds,
                    Timestamp = DateTime.UtcNow
                });

                throw;
            }
        }

        //public async Task<T> TrackAsync<T>(
        //    object requestObj,
        //    Func<Task<T>> apiCall,
        //    string provider = "gemini"
        //)
        //{
        //    var id = Guid.NewGuid().ToString();
        //    var sw = Stopwatch.StartNew();
        //    var ctx = _httpContextAccessor.HttpContext;

        //    var userId = ctx?.Items["UserId"]?.ToString() ?? Guid.NewGuid().ToString();
        //    var correlationId = ctx?.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
        //    var endpoint = ctx?.Request?.Path.ToString() ?? "";
        //    try
        //    {

        //        var result = await apiCall();

        //        sw.Stop();

        //        await _repo.SaveAsync(new AiUsageRow
        //        {
        //            Id = id,
        //            CorrelationId = correlationId,
        //            UserId = userId,
        //            Endpoint = endpoint,

        //            Usage = JsonSerializer.Serialize(usage),
        //            Request = JsonSerializer.Serialize(requestObj),
        //            Response = JsonSerializer.Serialize(result),

        //            TimeTakenMs = sw.ElapsedMilliseconds,
        //            Timestamp = DateTime.UtcNow
        //        });

        //        return result;
        //    }
        //    catch (Exception ex)
        //    {
        //        sw.Stop();

        //        await _repo.SaveAsync(new AiUsageRow
        //        {
        //            Id = id,
        //            CorrelationId = correlationId,
        //            UserId = userId,
        //            Endpoint = endpoint,

        //            //Usage = "{}",
        //            Request = JsonSerializer.Serialize(requestObj),
        //            Response = JsonSerializer.Serialize(new { error = ex.Message }),

        //            TimeTakenMs = sw.ElapsedMilliseconds,
        //            Timestamp = DateTime.UtcNow
        //        });

        //        throw;
        //    }
        //}
    }

}

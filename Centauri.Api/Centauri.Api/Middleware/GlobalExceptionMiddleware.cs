namespace Centauri_Api.Middleware
{
    using System.Net;
    using System.Text.Json;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Configuration;

    public sealed class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;
        private readonly IConfiguration _config;

        public GlobalExceptionMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionMiddleware> logger,
            IConfiguration config)
        {
            _next = next;
            _logger = logger;
            _config = config;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var isVerboseErrorMode = _config.GetValue<bool>("ErrorHandling:VerboseErrors", false);
            var (statusCode, errorCode, message) = exception switch
            {
                ArgumentException =>
                    (HttpStatusCode.BadRequest, "INVALID_ARGUMENT", isVerboseErrorMode ? exception.Message : "Invalid argument provided"),

                UnauthorizedAccessException =>
                    (HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access"),

                KeyNotFoundException =>
                    (HttpStatusCode.NotFound, "NOT_FOUND", isVerboseErrorMode ? exception.Message : "Resource not found"),

                TimeoutException =>
                    (HttpStatusCode.RequestTimeout, "TIMEOUT", "The request timed out"),

                _ =>
                    (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", isVerboseErrorMode ? exception.Message : "An unexpected error occurred")
            };

            _logger.LogError(exception, "Unhandled exception");

            context.Response.Clear();
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            var response = new ErrorResponse
            {
                StatusCode = context.Response.StatusCode,
                ErrorCode = errorCode,
                Message = message,
                TraceId = context.TraceIdentifier
            };

            var json = JsonSerializer.Serialize(response);

            await context.Response.WriteAsync(json);
        }
    }

    public sealed class ErrorResponse
    {
        public int StatusCode { get; init; }
        public string ErrorCode { get; init; } = default!;
        public string Message { get; init; } = default!;
        public string TraceId { get; init; } = default!;
    }

}

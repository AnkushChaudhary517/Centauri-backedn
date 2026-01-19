namespace Centauri_Api.Middleware
{
    using System.Net;
    using System.Text.Json;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;

    public sealed class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
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
            var (statusCode, errorCode, message) = exception switch
            {
                ArgumentException =>
                    (HttpStatusCode.BadRequest, "INVALID_ARGUMENT", exception.Message),

                UnauthorizedAccessException =>
                    (HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized access"),

                KeyNotFoundException =>
                    (HttpStatusCode.NotFound, "NOT_FOUND", exception.Message),

                TimeoutException =>
                    (HttpStatusCode.RequestTimeout, "TIMEOUT", "The request timed out"),

                _ =>
                    (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred")
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

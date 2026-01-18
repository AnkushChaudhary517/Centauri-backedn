namespace Centauri_Api.Middleware
{
    using System.Security.Claims;

    public class UserContextMiddleware
    {
        private readonly RequestDelegate _next;

        public const string UserIdKey = "UserId";
        public const string CorrelationIdKey = "CorrelationId";

        public UserContextMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            /* ---------- Extract UserId from JWT ---------- */

            var userId =
                context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User?.FindFirstValue("sub")
                ?? context.User?.FindFirstValue("userId");

            if (!string.IsNullOrEmpty(userId))
            {
                context.Items[UserIdKey] = userId;
            }

            /* ---------- CorrelationId for /seo/analyze ---------- */

            if (context.Request.Path.Value?
                .EndsWith("/seo/analyze", StringComparison.OrdinalIgnoreCase) == true)
            {
                var correlationId =
                    context.Request.Headers[CorrelationIdKey].FirstOrDefault()
                    ?? Guid.NewGuid().ToString();

                context.Items[CorrelationIdKey] = correlationId;

                // Add to request headers (downstream usage)
                context.Request.Headers[CorrelationIdKey] = correlationId;

                // Add to response headers
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers[CorrelationIdKey] = correlationId;
                    return Task.CompletedTask;
                });
            }

            await _next(context);
        }
    }

}

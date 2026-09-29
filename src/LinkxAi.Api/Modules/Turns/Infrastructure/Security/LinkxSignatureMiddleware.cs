namespace LinkxAi.Api.Modules.Turns.Infrastructure.Security;

public sealed class LinkxSignatureMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var secret = configuration["Linkx:Secret"];
        if (!string.IsNullOrEmpty(secret) && context.Request.Method == "POST" &&
            string.Equals(context.Request.Path.Value, "/api/v1/move", StringComparison.OrdinalIgnoreCase))
        {
            var timestamp = context.Request.Headers["X-Linkx-Timestamp"].ToString();
            var signature = context.Request.Headers["X-Linkx-Signature"].ToString();
            context.Request.EnableBuffering();
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            context.Request.Body.Position = 0;

            if (!LinkxSignature.IsValid(secret, timestamp, signature, body.ToArray(), DateTimeOffset.UtcNow))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await next(context);
    }
}

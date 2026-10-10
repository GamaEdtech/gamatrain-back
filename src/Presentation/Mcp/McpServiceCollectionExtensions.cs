namespace GamaEdtech.Presentation.Mcp
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Application.Interface;

    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;

    using ModelContextProtocol.AspNetCore.Authentication;

    /// <summary>
    /// The MCP presentation layer, next to the REST API: the MCP server AI assistants (ChatGPT, Claude, Codex...) connect to
    /// for importing past papers (<see cref="ExamImportTools"/>), stateless Streamable HTTP at <c>/mcp</c>, and the OAuth
    /// endpoints its clients sign in through (<see cref="McpController"/>). See docs/business/exams-and-content.md, "Exam
    /// import through the MCP connector", and docs/api/authentication.md, "MCP connector (OAuth)".
    /// </summary>
    public static class McpServiceCollectionExtensions
    {
        /// <summary>The MCP server, its tools, prompt and widget, the <c>McpToken</c> scheme and the OAuth controller. The SDK's
        /// scheme answers the 401 challenge with the protected resource metadata pointing at this API's OAuth endpoints.</summary>
        public static IServiceCollection AddGamaMcp(this IServiceCollection services, [NotNull] IConfiguration configuration)
        {
            _ = services.AddMcpServer(options =>
                {
                    options.ServerInfo = new() { Name = "gamatrain", Title = "Gamatrain", Version = "1.0.0" };
                    options.ServerInstructions = ExamImportTools.Instructions;
                })
                .WithHttpTransport(options => options.Stateless = true)
                .WithTools<ExamImportTools>()
                .WithPrompts<ExamImportTools>()
                .WithResources<ExamImportPreviewWidget>();

            _ = services.AddMvcCore().AddApplicationPart(typeof(McpController).Assembly);

            var publicUrl = configuration.GetValue<string?>("Mcp:PublicUrl")?.TrimEnd('/');
            _ = services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, McpTokenAuthenticationHandler>(McpTokenAuthenticationHandler.SchemeName,
                    options => options.ForwardChallenge = McpAuthenticationDefaults.AuthenticationScheme)
                .AddMcp(options =>
                {
                    // Behind the reverse proxy the request looks like http, so the advertised URL comes from Mcp:PublicUrl.
                    if (!string.IsNullOrEmpty(publicUrl))
                    {
                        options.ResourceMetadataUri = new($"{publicUrl}/.well-known/oauth-protected-resource/mcp");
                    }

                    options.Events.OnResourceMetadataRequest = context =>
                    {
                        var issuer = context.HttpContext.RequestServices.GetRequiredService<IMcpAuthorizationService>().GetMetadata().Issuer!;
                        context.ResourceMetadata = new()
                        {
                            Resource = $"{issuer}/mcp",
                            AuthorizationServers = [issuer],
                            ResourceName = "Gamatrain exam import",
                        };
                        return Task.CompletedTask;
                    };
                });

            return services;
        }

        /// <summary>The MCP endpoint <c>/mcp</c>, which only MCP access tokens open.</summary>
        public static IEndpointConventionBuilder MapGamaMcp(this IEndpointRouteBuilder endpoints)
        {
            // Stateless Streamable HTTP has no GET stream and no session to DELETE, so the SDK maps POST only. Answer
            // those with 405, as the MCP spec asks, instead of letting them fall through to the MVC route, which reads
            // "mcp" as a culture and redirects to /swagger.
            _ = endpoints.MapMethods("/mcp", [HttpMethods.Get, HttpMethods.Delete], (HttpContext context) =>
            {
                context.Response.Headers.Allow = HttpMethods.Post;
                return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
            });

            return endpoints.MapMcp("/mcp").RequireAuthorization(new AuthorizationPolicyBuilder(McpTokenAuthenticationHandler.SchemeName).RequireAuthenticatedUser().Build());
        }
    }
}

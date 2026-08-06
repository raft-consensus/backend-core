using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;
using raft_backend.Middleware;
using raft_backend.Services;

namespace raft_backend.Modules.Hosting;

public static class RaftHostingModule
{
    public static void UseRaftHostingModule(this WebApplication app)
    {
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
        };

        forwardedHeadersOptions.KnownIPNetworks.Clear();
        forwardedHeadersOptions.KnownProxies.Clear();

        app.UseForwardedHeaders(forwardedHeadersOptions);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.UseHttpsRedirection();
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        app.Use(async (context, next) =>
        {
            var availabilityTracker = context.RequestServices.GetRequiredService<IApiAvailabilityTracker>();

            try
            {
                await next();
            }
            finally
            {
                availabilityTracker.RecordResponse(context.Response.StatusCode);
            }
        });

        app.UseCors("Frontend");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
    }
}

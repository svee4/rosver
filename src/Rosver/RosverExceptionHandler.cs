using Microsoft.AspNetCore.Diagnostics;

namespace Rosver;

public sealed class RosverExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not RosverException e)
        {
            return false;
        }

        httpContext.Response.StatusCode = (int)e.Status;
        httpContext.Response.ContentType = "text/plain";
        await httpContext.Response.WriteAsync(e.Message, cancellationToken);
        return true;
    }
}
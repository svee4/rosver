using System.Net;

namespace Rosver;

public sealed class RosverException(
    string message,
    HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

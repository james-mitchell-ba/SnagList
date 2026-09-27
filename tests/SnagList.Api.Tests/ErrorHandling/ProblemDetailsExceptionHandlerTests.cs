namespace SnagList.Api.Tests.ErrorHandling;

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnagList.Api.ErrorHandling;
using SnagList.Application.Snags;
using Xunit;

public class ProblemDetailsExceptionHandlerTests
{
    private static async Task<(bool handled, int statusCode, ProblemDetails? body)> Handle(Exception exception)
    {
        var handler = new ProblemDetailsExceptionHandler();
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var handled = await handler.TryHandleAsync(context, exception, default);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = handled ? await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body) : null;
        return (handled, context.Response.StatusCode, body);
    }

    [Fact]
    public async Task Maps_SnagVersionConflictException_to_409_with_a_stable_type_uri()
    {
        var (handled, statusCode, body) = await Handle(new SnagVersionConflictException(Guid.NewGuid(), 2, 3));

        Assert.True(handled);
        Assert.Equal(409, statusCode);
        Assert.Equal("https://snaglist.example/errors/version-conflict", body!.Type);
    }

    [Fact]
    public async Task Maps_UnauthorizedSnagActionException_to_403()
    {
        var (handled, statusCode, _) = await Handle(new UnauthorizedSnagActionException("edit"));

        Assert.True(handled);
        Assert.Equal(403, statusCode);
    }

    [Fact]
    public async Task Maps_SnagNotFoundException_to_404()
    {
        var (handled, statusCode, _) = await Handle(new SnagNotFoundException(Guid.NewGuid()));

        Assert.True(handled);
        Assert.Equal(404, statusCode);
    }

    [Fact]
    public async Task Returns_false_for_an_unmapped_exception_so_the_default_handler_takes_over()
    {
        var (handled, _, _) = await Handle(new InvalidOperationException("unexpected"));

        Assert.False(handled);
    }
}

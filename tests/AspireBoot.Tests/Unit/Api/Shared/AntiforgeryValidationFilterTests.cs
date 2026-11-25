using AspireBoot.Api.Shared;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using NSubstitute;

namespace AspireBoot.Tests.Unit.Api.Shared;

[Trait("Category", "Unit")]
public sealed class AntiforgeryValidationFilterTests
{
    [Fact]
    public async Task InvokeAsync_UnsafeRequestWithInvalidToken_Returns400Problem()
    {
        var antiforgery = Substitute.For<IAntiforgery>();
        antiforgery.IsRequestValidAsync(Arg.Any<HttpContext>()).Returns(Task.FromResult(false));
        var filter = new AntiforgeryValidationFilter(antiforgery);
        var context = CreateContext(HttpMethods.Post);
        var nextCalled = false;

        var result = await filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(null);
        });

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("antiforgery.invalid", problem.ProblemDetails.Type);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_UnsafeRequestWithValidToken_CallsNext()
    {
        var antiforgery = Substitute.For<IAntiforgery>();
        antiforgery.IsRequestValidAsync(Arg.Any<HttpContext>()).Returns(Task.FromResult(true));
        var filter = new AntiforgeryValidationFilter(antiforgery);
        var context = CreateContext(HttpMethods.Post);

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Accepted()));

        Assert.IsType<Accepted>(result);
    }

    [Fact]
    public async Task InvokeAsync_SafeRequest_SkipsValidation()
    {
        var antiforgery = Substitute.For<IAntiforgery>();
        var filter = new AntiforgeryValidationFilter(antiforgery);
        var context = CreateContext(HttpMethods.Get);

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>(Results.Ok()));

        Assert.IsType<Ok>(result);
        await antiforgery.DidNotReceive().IsRequestValidAsync(Arg.Any<HttpContext>());
    }

    private static DefaultEndpointFilterInvocationContext CreateContext(string method)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }
}
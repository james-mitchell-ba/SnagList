namespace SnagList.Api.ErrorHandling;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnagList.Application.Locations;
using SnagList.Application.Snags;
using SnagList.Domain.Snags;

public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private const string BaseUri = "https://snaglist.example/errors";

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, type, title) = exception switch
        {
            SnagNotFoundException or LocationNotFoundException =>
                (StatusCodes.Status404NotFound, $"{BaseUri}/not-found", "Not found"),
            SnagVersionConflictException =>
                (StatusCodes.Status409Conflict, $"{BaseUri}/version-conflict", "Version conflict"),
            UnauthorizedSnagActionException =>
                (StatusCodes.Status403Forbidden, $"{BaseUri}/not-authorized", "Not authorized"),
            InvalidSnagStatusTransitionException =>
                (StatusCodes.Status409Conflict, $"{BaseUri}/invalid-status-transition", "Invalid status transition"),
            SnagNotEditableException =>
                (StatusCodes.Status409Conflict, $"{BaseUri}/not-editable", "Snag is not editable"),
            SnagPhotoLimitExceededException =>
                (StatusCodes.Status422UnprocessableEntity, $"{BaseUri}/photo-limit-exceeded", "Photo limit exceeded"),
            LocationNotActiveException =>
                (StatusCodes.Status422UnprocessableEntity, $"{BaseUri}/location-not-active", "Location is not active"),
            _ => (0, "", ""),
        };

        if (status == 0) return false;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = title,
            Detail = exception.Message,
        };
        if (exception is SnagVersionConflictException conflict)
        {
            problemDetails.Extensions["expectedVersion"] = conflict.ExpectedVersion;
            problemDetails.Extensions["actualVersion"] = conflict.ActualVersion;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problemDetails, ct);
        return true;
    }
}

using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ReverseGeocodeApi.Extensions;

public sealed class ProblemFactory
{
    private const string ProblemTypeBase = "https://api.reversegeocode.pt/problems/";

    public IActionResult CreateActionResult(
        HttpContext httpContext,
        int status,
        string title,
        string detail,
        string category,
        string code)
    {
        var problem = CreateProblemDetails(httpContext, status, title, detail, category, code);
        var result = new ContentResult
        {
            StatusCode = status,
            ContentType = "application/problem+json",
            Content = JsonSerializer.Serialize(problem)
        };

        return result;
    }

    public IResult CreateResult(
        HttpContext httpContext,
        int status,
        string title,
        string detail,
        string category,
        string code)
    {
        var extensions = CreateExtensions(httpContext, category, code);
        return Results.Problem(
            detail: detail,
            instance: httpContext.Request.Path,
            statusCode: status,
            title: title,
            type: $"{ProblemTypeBase}{code.Replace('_', '-')}",
            extensions: extensions);
    }

    public Task WriteAsync(
        HttpContext httpContext,
        int status,
        string title,
        string detail,
        string category,
        string code,
        CancellationToken cancellationToken = default)
    {
        var problem = CreateProblemDetails(httpContext, status, title, detail, category, code);
        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";
        var payload = JsonSerializer.Serialize(problem);
        return httpContext.Response.WriteAsync(payload, cancellationToken);
    }

    private static ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int status,
        string title,
        string detail,
        string category,
        string code)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        var problem = new ProblemDetails
        {
            Type = $"{ProblemTypeBase}{code.Replace('_', '-')}",
            Title = title,
            Status = status,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problem.Extensions["traceId"] = traceId;
        problem.Extensions["category"] = category;
        problem.Extensions["code"] = code;

        return problem;
    }

    private static Dictionary<string, object?> CreateExtensions(
        HttpContext httpContext,
        string category,
        string code)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        return new Dictionary<string, object?>
        {
            ["traceId"] = traceId,
            ["category"] = category,
            ["code"] = code
        };
    }
}

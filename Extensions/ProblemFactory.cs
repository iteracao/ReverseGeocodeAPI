using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ReverseGeocodeApi.Extensions;

public sealed class ProblemFactory
{
    private const string ProblemTypeBase = "https://api.reversegeocode.pt/problems/";

    public ObjectResult CreateActionResult(
        HttpContext httpContext,
        int status,
        string title,
        string detail,
        string category,
        string code)
    {
        var problem = CreateProblemDetails(httpContext, status, title, detail, category, code);

        var result = new ObjectResult(problem)
        {
            StatusCode = status
        };
        result.ContentTypes.Add("application/problem+json");

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
        var problem = CreateProblemDetails(httpContext, status, title, detail, category, code);
        return Results.Json(problem, statusCode: status, contentType: "application/problem+json");
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
        return httpContext.Response.WriteAsJsonAsync(problem, cancellationToken: cancellationToken);
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
}

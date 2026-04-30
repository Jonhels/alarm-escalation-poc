using System.Net;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Web.Filters;

public class ApiExceptionsFilter : IActionFilter, IOrderedFilter
{
    private readonly ProblemDetailsFactory _problemDetailsFactory;

    public ApiExceptionsFilter(ProblemDetailsFactory problemDetailsFactory)
    {
        _problemDetailsFactory = problemDetailsFactory;
    }

    public void OnActionExecuting(ActionExecutingContext context) { }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        switch (context.Exception)
        {
            case CallAttemptNotFoundException:
                MakeProblemDetailsResponse(context, (int)HttpStatusCode.NotFound, "The call attempt was not found");
                context.ExceptionHandled = true;
                return;

            case InvalidOperationException:
                MakeProblemDetailsResponse(context, (int)HttpStatusCode.InternalServerError, "An unexpected error occurred");
                context.ExceptionHandled = true;
                return;
        }
    }

    private void MakeProblemDetailsResponse(ActionExecutedContext context, int? statusCode = null, string? title = null)
    {
        var problemDetails = _problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            statusCode: statusCode,
            title: title
        );
        context.Result = new ObjectResult(problemDetails) { StatusCode = problemDetails.Status };
    }

    public int Order => int.MaxValue - 10;
}

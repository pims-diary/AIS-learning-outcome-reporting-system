using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AIS_LO_System.Filters
{
    /// <summary>
    /// Blocks write (POST/PUT/DELETE/PATCH) requests while an admin is impersonating
    /// another user. Impersonation sessions are read-only by design.
    /// </summary>
    public class ReadOnlyWhenImpersonatingFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var method = context.HttpContext.Request.Method;
            var isWrite = HttpMethods.IsPost(method)
                || HttpMethods.IsPut(method)
                || HttpMethods.IsDelete(method)
                || HttpMethods.IsPatch(method);

            if (!isWrite) return;

            var isImpersonating = context.HttpContext.User
                .FindFirst("ImpersonatedBy") != null;

            if (isImpersonating)
            {
                context.Result = new ObjectResult("Write operations are not permitted during impersonation.")
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
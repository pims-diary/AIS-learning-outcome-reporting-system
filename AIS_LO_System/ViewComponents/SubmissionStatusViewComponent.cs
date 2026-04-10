using AIS_LO_System.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIS_LO_System.ViewComponents
{
    public class SubmissionStatusViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke(CourseSubmission? submission)
        {
            return View(submission);
        }
    }
}
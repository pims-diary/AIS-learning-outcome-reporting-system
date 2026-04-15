using System.Text.Json;
using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.EntityFrameworkCore;

namespace AIS_LO_System.Services
{
    public class ModerationDraftService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _context;

        public ModerationDraftService(IWebHostEnvironment env, ApplicationDbContext context)
        {
            _env = env;
            _context = context;
        }

        public async Task SaveLearningOutcomeDraftAsync(
            int submissionId,
            string courseCode,
            int year,
            int trimester,
            List<string> outcomes)
        {
            var draft = new LearningOutcomeDraft
            {
                CourseCode = courseCode,
                Year = year,
                Trimester = trimester,
                Outcomes = outcomes ?? new List<string>()
            };

            await SaveDraftAsync(submissionId, draft);
        }

        public Task<LearningOutcomeDraft?> LoadLearningOutcomeDraftAsync(int submissionId)
            => LoadDraftAsync<LearningOutcomeDraft>(submissionId);

        public async Task SaveAssessmentDraftAsync(
            int submissionId,
            string courseCode,
            int year,
            int trimester,
            List<AssessmentDraftItem> assessments)
        {
            var draft = new AssessmentDraft
            {
                CourseCode = courseCode,
                Year = year,
                Trimester = trimester,
                Assessments = assessments ?? new List<AssessmentDraftItem>()
            };

            await SaveDraftAsync(submissionId, draft);
        }

        public Task<AssessmentDraft?> LoadAssessmentDraftAsync(int submissionId)
            => LoadDraftAsync<AssessmentDraft>(submissionId);

        public Task<bool> HasDraftAsync(int submissionId)
            => Task.FromResult(System.IO.File.Exists(GetDraftPath(submissionId)));

        public Task DeleteDraftAsync(int submissionId)
        {
            var path = GetDraftPath(submissionId);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);

            return Task.CompletedTask;
        }

        public async Task ApplyLearningOutcomeDraftAsync(int submissionId)
        {
            var draft = await LoadLearningOutcomeDraftAsync(submissionId)
                ?? throw new InvalidOperationException("The pending learning outcome draft could not be found.");

            SaveLearningOutcomesJson(draft.CourseCode, draft.Year, draft.Trimester, draft.Outcomes);
            SyncLearningOutcomesToDatabase(draft.CourseCode, draft.Outcomes);
        }

        public async Task ApplyAssessmentDraftAsync(int submissionId)
        {
            var draft = await LoadAssessmentDraftAsync(submissionId)
                ?? throw new InvalidOperationException("The pending assessment draft could not be found.");

            var existing = await _context.Assignments
                .Where(a => a.CourseCode == draft.CourseCode && a.Year == draft.Year && a.Trimester == draft.Trimester)
                .ToListAsync();

            var course = await _context.Courses
                .FirstOrDefaultAsync(c => c.Code == draft.CourseCode && c.Year == draft.Year && c.Trimester == draft.Trimester);

            var validLearningOutcomeIds = await _context.LearningOutcomes
                .Where(lo => lo.CourseCode == draft.CourseCode)
                .Select(lo => lo.Id)
                .ToListAsync();

            var validIds = validLearningOutcomeIds.ToHashSet();
            var keptExistingAssignmentIds = new HashSet<int>();

            foreach (var input in draft.Assessments ?? new List<AssessmentDraftItem>())
            {
                if (string.IsNullOrWhiteSpace(input.AssessmentName))
                    continue;

                var normalizedName = input.AssessmentName.Trim();
                var selectedIds = (input.SelectedLOIds ?? new List<int>())
                    .Where(validIds.Contains)
                    .Distinct()
                    .ToList();

                var selectedIdsStr = selectedIds.Any()
                    ? string.Join(",", selectedIds)
                    : null;

                if (input.Id == 0)
                {
                    _context.Assignments.Add(new Assignment
                    {
                        AssessmentName = normalizedName,
                        CourseCode = draft.CourseCode,
                        CourseTitle = course?.Title ?? string.Empty,
                        Year = draft.Year,
                        Trimester = draft.Trimester,
                        MarksPercentage = input.MarksPercentage,
                        SelectedLearningOutcomeIds = selectedIdsStr,
                        LOsLockedByOutline = false
                    });

                    continue;
                }

                var record = existing.FirstOrDefault(a => a.Id == input.Id);
                if (record == null)
                {
                    _context.Assignments.Add(new Assignment
                    {
                        AssessmentName = normalizedName,
                        CourseCode = draft.CourseCode,
                        CourseTitle = course?.Title ?? string.Empty,
                        Year = draft.Year,
                        Trimester = draft.Trimester,
                        MarksPercentage = input.MarksPercentage,
                        SelectedLearningOutcomeIds = selectedIdsStr,
                        LOsLockedByOutline = false
                    });

                    continue;
                }

                RenameAssessmentMarksIfNeeded(draft.CourseCode, record.AssessmentName, normalizedName);
                keptExistingAssignmentIds.Add(record.Id);

                record.AssessmentName = normalizedName;
                record.MarksPercentage = input.MarksPercentage;
                record.SelectedLearningOutcomeIds = selectedIdsStr;
                record.LOsLockedByOutline = false;
            }

            var assignmentsToRemove = existing
                .Where(a => !keptExistingAssignmentIds.Contains(a.Id))
                .ToList();

            foreach (var assignment in assignmentsToRemove)
            {
                RemoveAssignmentAndRelatedData(draft.CourseCode, draft.Year, draft.Trimester, assignment);
            }

            await _context.SaveChangesAsync();
        }

        private string GetDraftDirectory()
        {
            var dir = Path.Combine(_env.ContentRootPath, "App_Data", "moderation-drafts");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private string GetDraftPath(int submissionId)
            => Path.Combine(GetDraftDirectory(), $"{submissionId}.json");

        private async Task SaveDraftAsync<T>(int submissionId, T draft)
        {
            var path = GetDraftPath(submissionId);
            var json = JsonSerializer.Serialize(draft, new JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(path, json);
        }

        private async Task<T?> LoadDraftAsync<T>(int submissionId)
        {
            var path = GetDraftPath(submissionId);
            if (!System.IO.File.Exists(path))
                return default;

            var json = await System.IO.File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<T>(json);
        }

        private void SaveLearningOutcomesJson(string courseCode, int year, int trimester, List<string> outcomes)
        {
            var dir = Path.Combine(_env.ContentRootPath, "App_Data", "learning-outcomes");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"{courseCode}-{year}-T{trimester}.json");
            var json = JsonSerializer.Serialize(outcomes, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(path, json);
        }

        private void SyncLearningOutcomesToDatabase(string courseCode, List<string> outcomeTexts)
        {
            var existingLOs = _context.LearningOutcomes
                .Where(lo => lo.CourseCode == courseCode)
                .OrderBy(lo => lo.OrderNumber)
                .ToList();

            var existingByOrder = existingLOs.ToDictionary(lo => lo.OrderNumber);

            var orderNumber = 1;
            foreach (var outcomeText in outcomeTexts)
            {
                if (existingByOrder.TryGetValue(orderNumber, out var existing))
                {
                    existing.LearningOutcomeText = outcomeText;
                }
                else
                {
                    _context.LearningOutcomes.Add(new LearningOutcome
                    {
                        CourseCode = courseCode,
                        LearningOutcomeText = outcomeText,
                        OrderNumber = orderNumber
                    });
                }

                orderNumber++;
            }

            var extraLOs = existingLOs
                .Where(lo => lo.OrderNumber > outcomeTexts.Count)
                .ToList();

            if (extraLOs.Any())
                _context.LearningOutcomes.RemoveRange(extraLOs);

            _context.SaveChanges();
        }

        private void RenameAssessmentMarksIfNeeded(string courseCode, string? oldName, string? newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) ||
                string.IsNullOrWhiteSpace(newName) ||
                oldName.Equals(newName, StringComparison.Ordinal))
            {
                return;
            }

            var marksToRename = _context.StudentAssessmentMarks
                .Where(m => m.CourseCode == courseCode && m.AssessmentName == oldName)
                .ToList();

            foreach (var mark in marksToRename)
            {
                mark.AssessmentName = newName;
            }
        }

        private void RemoveAssignmentAndRelatedData(string courseCode, int year, int trimester, Assignment assignment)
        {
            var assignmentFiles = _context.AssignmentFiles
                .Where(f => f.AssignmentId == assignment.Id)
                .ToList();

            foreach (var file in assignmentFiles)
            {
                if (string.IsNullOrWhiteSpace(file.FilePath))
                    continue;

                var relativePath = file.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(_env.WebRootPath, relativePath));
                if (!fullPath.StartsWith(_env.WebRootPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (System.IO.File.Exists(fullPath))
                {
                    try
                    {
                        System.IO.File.Delete(fullPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to delete file {fullPath}: {ex.Message}");
                    }
                }
            }

            if (assignmentFiles.Any())
                _context.AssignmentFiles.RemoveRange(assignmentFiles);

            var rubricIds = _context.Rubrics
                .Where(r => r.AssignmentId == assignment.Id)
                .Select(r => r.Id)
                .ToList();

            var rubricCriterionIds = _context.RubricCriteria
                .Where(c => rubricIds.Contains(c.RubricId))
                .Select(c => c.Id)
                .ToList();

            var criterionMarks = _context.StudentCriterionMarks
                .Where(m => m.AssignmentId == assignment.Id || rubricCriterionIds.Contains(m.RubricCriterionId))
                .ToList();

            if (criterionMarks.Any())
                _context.StudentCriterionMarks.RemoveRange(criterionMarks);

            var loMappings = _context.CriterionLOMappings
                .Where(m => rubricCriterionIds.Contains(m.RubricCriterionId))
                .ToList();

            if (loMappings.Any())
                _context.CriterionLOMappings.RemoveRange(loMappings);

            var rubrics = _context.Rubrics
                .Where(r => r.AssignmentId == assignment.Id)
                .ToList();

            if (rubrics.Any())
                _context.Rubrics.RemoveRange(rubrics);

            var relatedSubmissions = _context.CourseSubmissions
                .Where(s =>
                    s.CourseCode == courseCode &&
                    s.Year == year &&
                    s.Trimester == trimester &&
                    (s.ItemRefId == assignment.Id || rubricIds.Contains(s.ItemRefId ?? 0)))
                .ToList();

            if (relatedSubmissions.Any())
                _context.CourseSubmissions.RemoveRange(relatedSubmissions);

            var relatedAssessmentMarks = _context.StudentAssessmentMarks
                .Where(m => m.CourseCode == courseCode && m.AssessmentName == assignment.AssessmentName)
                .ToList();

            if (relatedAssessmentMarks.Any())
                _context.StudentAssessmentMarks.RemoveRange(relatedAssessmentMarks);

            _context.Assignments.Remove(assignment);
        }
    }

    public class LearningOutcomeDraft
    {
        public string CourseCode { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Trimester { get; set; }
        public List<string> Outcomes { get; set; } = new();
    }

    public class AssessmentDraft
    {
        public string CourseCode { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Trimester { get; set; }
        public List<AssessmentDraftItem> Assessments { get; set; } = new();
    }

    public class AssessmentDraftItem
    {
        public int Id { get; set; }
        public string AssessmentName { get; set; } = string.Empty;
        public int MarksPercentage { get; set; }
        public List<int> SelectedLOIds { get; set; } = new();
    }
}

# AIS-learning-outcome-reporting-system
Learning Outcome Achievement Reporting System: rubric-based marking, LO mapping, and PDF reporting for academic quality assurance (AIS/NZQA-aligned).

# Learning Outcome Achievement Reporting System (Ongoing)

A .NET 8 web-based system for rubric-based marking and automated Learning Outcome (LO) achievement reporting (AIS/NZQA-aligned).

## Project Overview
This system supports:
- Course-level Learning Outcomes (LOs) managed by Admin
- Rubric management (create/edit/view) and performance levels (e.g., 0–4)
- LO mapping & weighting (criteria ↔ LOs + weights)
- Student marking using rubric criteria
- Automatic calculation of weighted scores and LO achievement %
- Report generation and PDF export

## Tech Stack
- Backend: .NET 8 (C#)
- Frontend: ASP.NET Core MVC / Blazor
- Database: SQL Server
- ORM: Entity Framework Core (Code-First), LINQ
- Auth: ASP.NET Identity (RBAC: Admin / Lecturer / Moderator)
- Document Processing: Open XML SDK (Word)
- PDF: QuestPDF / iTextSharp

## Local Setup (Developer)
### Prerequisites
- .NET SDK 8
- Visual Studio 2022 (or VS Code)
- SQL Server / SQL Server Express / LocalDB

### Run the Web App
From the repo root:
```bash
dotnet restore
dotnet run --project WebApp


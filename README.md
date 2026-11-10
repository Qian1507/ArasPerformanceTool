# Aras Performance Tool

A .NET-based performance benchmarking tool for Aras Innovator, designed to measure, analyze, and compare the execution performance of AML queries.

> **Status:** Work in Progress — Initial project structure established.

## Project Overview

The goal of this project is to develop a performance testing tool that helps evaluate different AML query approaches in Aras Innovator.

The tool is intended to support repeatable benchmark execution, performance measurement, statistical analysis, and result visualization.

## Planned Features

- Execute AML queries against Aras Innovator.
- Measure query execution times.
- Compare different AML query approaches.
- Run repeated benchmark tests.
- Calculate performance statistics.
- Generate performance reports.
- Visualize benchmark results through a web interface.

## Technology Stack

- **Language:** C#
- **Framework:** .NET / ASP.NET Core
- **PLM Platform:** Aras Innovator
- **Query Language:** AML (Adaptive Markup Language)
- **Version Control:** Git & GitHub

## Project Structure

```text
ArasPerformanceTool/
├── src/
│   ├── ArasPerformanceTool.Api/
│   └── ArasPerformanceTool.Engine/
├── tests/
├── ArasPerformanceTool.slnx
└── README.md
```

**ArasPerformanceTool.Api** — Intended to host the API, background jobs, and web interface.

**ArasPerformanceTool.Engine** — Intended to contain the Aras integration, benchmark execution logic, performance statistics, and reporting components.

**tests** — Reserved for automated tests.

## Development Status

The initial solution structure and project organization have been created.

Implementation of the benchmarking engine, AML execution, performance measurement, and reporting functionality is planned.

## Project Purpose

This project is developed as a practical learning and development exercise focused on .NET application architecture, Aras Innovator integration, and performance benchmarking.



## Third-Party Licenses

### QuestPDF

This project uses [QuestPDF](https://www.questpdf.com/) to generate PDF performance reports.

- **Configured license type:** Community.
- **Current usage:** Development, testing, and technical evaluation during the LIA project, subject to the applicable license terms.
- **Company use:** License eligibility must be confirmed before production or regular business use.

**Important:** Configuring the Community license type does not by itself establish license eligibility. Before production or regular business use, verify whether the company qualifies for the QuestPDF Community License. If not, an appropriate commercial license or an alternative PDF library is required. Any applicable evaluation time limit must also be observed.

For details, see the [QuestPDF License Selection Guide](https://www.questpdf.com/license/guide.html) and the [QuestPDF Licensing Documentation](https://www.questpdf.com/license/).

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
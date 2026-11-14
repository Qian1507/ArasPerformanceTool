using System;
using System.Collections.Generic;
using System.Linq;
using ArasPerformanceTool.Engine.Benchmarking;
using ArasPerformanceTool.Engine.Models;

namespace ArasPerformanceTool.Api.DTOs;

/// <summary>
/// Represents the lifecycle status of a benchmark job.
/// </summary>
public enum BenchmarkStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

/// <summary>
/// Contains details about a CRUD operation and its verification result.
/// </summary>
/// <param name="Operation">CRUD operation being verified.</param>
/// <param name="TargetId">Original Aras Item ID.</param>
/// <param name="UpdatedId">Item ID returned after an update, if different.</param>
/// <param name="PropertyName">Property involved in the operation.</param>
/// <param name="BeforeGeneration">Item generation before the operation.</param>
/// <param name="AfterGeneration">Item generation after the operation.</param>
/// <param name="BeforeValue">Property value before the operation.</param>
/// <param name="AfterValue">Property value after the operation.</param>
/// <param name="IsVerified">Whether the expected operation result was verified.</param>
/// <param name="VerifiedNotFound">Whether the target item was confirmed missing.</param>
/// <param name="VerificationNote">Additional verification information.</param>
/// <param name="CleanedUp">Whether temporary test data was cleaned up.</param>
public sealed record CrudVerification(
    string Operation,
    string? TargetId = null,
    string? UpdatedId = null,
    string? PropertyName = null,
    string? BeforeGeneration = null,
    string? AfterGeneration = null,
    string? BeforeValue = null,
    string? AfterValue = null,
    bool? IsVerified = null,
    bool? VerifiedNotFound = null,
    string? VerificationNote = null,
    bool? CleanedUp = null);


/// <summary>
/// Contains a sample XML response from a Custom AML query.
/// The response is captured during benchmark execution.
/// </summary>
public sealed record AmlQueryResult(
    string ResponseXml);

/// <summary>
/// Response containing benchmark execution status, performance metrics,
/// and optional CRUD verification details.
/// </summary>
/// <param name="JobId">Unique identifier for the benchmark run.</param>
/// <param name="Status">Current or final benchmark status.</param>
/// <param name="ItemType">Aras ItemType used in the benchmark.</param>
/// <param name="Iterations">Number of requested benchmark iterations.</param>
/// <param name="Concurrency">Maximum number of concurrent operations.</param>
/// <param name="CreatedAtUtc">UTC time when the benchmark was created.</param>
/// <param name="CompletedAtUtc">UTC time when execution finished, if available.</param>
/// <param name="Statistics">Aggregated performance metrics, if available.</param>
/// <param name="ErrorMessage">Error description if execution failed.</param>
/// <param name="Verification">Optional details of a verified CRUD operation.</param>
/// /// <param name="QueryResult">
/// Optional sample response from a Custom AML query.
/// </param>
public sealed record BenchmarkStatusResponse(
    Guid JobId,
    BenchmarkStatus Status,
    string ItemType,
    int Iterations,
    int Concurrency,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    BenchmarkStatistics? Statistics,
    string? ErrorMessage,
    CrudVerification? Verification = null,
    AmlQueryResult? QueryResult = null,
    CustomAmlCrudResult? ManagedCrudResult = null,
        bool RequiresLogin = false,
        bool ReportsAvailable = false);
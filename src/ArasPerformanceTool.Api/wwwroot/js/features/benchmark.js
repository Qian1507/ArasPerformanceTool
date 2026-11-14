import {
    appState,
    BENCHMARK_LIMITS,
    byId,
    numberText,
    displayValue,
    verificationText
} from "../core/state.js";

import { getCsrfToken } from "../core/api.js";
import { showAuthenticationExpired } from "./login.js";

import {
    getAmlPayloadValue,
    updateAmlStatus,
    renderAmlQueryResults
} from "./custom_aml.js";

import { syncReportButtons } from "./reports.js";

const form = byId("benchmark-form");
const benchmarkMode = byId("benchmarkMode");

const standardGroup = byId("standard-config-group");
const customGroup = byId("custom-aml-group");

const itemType = byId("itemType");
const operation = byId("operation");
const operationDescription = byId("operation-description");

const iterations = byId("iterations");
const concurrency = byId("concurrency");
const workloadName = byId("workloadName");

const executeBtn = byId("run-btn");
const executeBtnText = byId("btn-text");
const loadingSpinner = byId("btn-spinner");

const resultsSection = byId("results-section");
const statusBadge = byId("status-badge");
const errorBanner = byId("error-banner");

const metricThroughput = byId("metric-throughput");
const metricAvg = byId("metric-avg");
const metricP95 = byId("metric-p95");
const metricP99 = byId("metric-p99");
const metricMinMax = byId("metric-minmax");
const metricSuccessRate = byId("metric-success-rate");

const verificationPanel = byId("verification-panel");
const verificationContent = byId("verification-content");

function updateMode() {
    const isRawAml = benchmarkMode.value === "RawAml";

    standardGroup.classList.toggle("hidden", isRawAml);
    customGroup.classList.toggle("hidden", !isRawAml);
}

function updateOperationDescription() {
    const descriptions = {
        Create:
            "Creates temporary test items and cleans them up " +
            "after the benchmark.",

        Read:
            "Creates one temporary item, reads it repeatedly, " +
            "and cleans it up.",

        Update:
            "Creates one temporary item per iteration and updates " +
            "its name. Aras may create a new generation.",

        Delete:
            "Creates one temporary item per iteration, deletes " +
            "each item, and verifies the results."
    };

    if (operationDescription) {
        operationDescription.textContent =
            descriptions[operation.value] || "";
    }
}

function setBusy(busy) {
    appState.submitting = busy;

    executeBtn.disabled =
        busy || appState.requiresLogin;

    executeBtnText.textContent = busy
        ? "Running Benchmark..."
        : "Execute Benchmark";

    loadingSpinner.classList.toggle("hidden", !busy);

    // Keep the visible configuration aligned with the submitted request.
    benchmarkMode.disabled = busy;
    itemType.disabled = busy;
    operation.disabled = busy;
    iterations.disabled = busy;
    concurrency.disabled = busy;
    workloadName.disabled = busy;
}

function resetMetrics() {
    const metricElements = [
        metricThroughput,
        metricAvg,
        metricP95,
        metricP99,
        metricMinMax,
        metricSuccessRate
    ];

    for (const element of metricElements) {
        element.textContent = "—";
    }
}

export function clearResults() {
    appState.currentJobId = null;
    appState.reportsAvailable = false;

    appState.currentJobId = null;

    resultsSection.classList.add("hidden");

    errorBanner.classList.add("hidden");
    errorBanner.textContent = "";

    statusBadge.textContent = "Pending";
    statusBadge.className = "badge";

    resetMetrics();

    renderVerification(null);
    renderAmlQueryResults(null);

    syncReportButtons();
}

function renderVerification(verification) {
    if (!verificationPanel || !verificationContent) {
        return;
    }

    verificationContent.replaceChildren();

    if (!verification) {
        verificationPanel.classList.add("hidden");
        return;
    }

    const grid = document.createElement("div");
    grid.className = "verification-grid";

    function addDetail(
        label,
        value,
        verificationState = null,
        fullWidth = false
    ) {
        const item = document.createElement("div");

        item.className = fullWidth
            ? "v-item v-item-full"
            : "v-item";

        const labelElement = document.createElement("span");
        labelElement.className = "v-label";
        labelElement.textContent = label;

        const valueElement = document.createElement("span");
        valueElement.className = "v-val";
        valueElement.textContent = displayValue(value);

        if (verificationState === true) {
            valueElement.classList.add("v-status-ok");
        } else if (verificationState === false) {
            valueElement.classList.add("v-status-fail");
        }

        item.append(labelElement, valueElement);
        grid.appendChild(item);
    }

    switch (verification.operation) {
        case "Create":
            addDetail(
                "Sample Item ID",
                verification.targetId
            );

            addDetail(
                "Sample Name",
                verification.afterValue
            );

            addDetail(
                "Verification State",
                verificationText(
                    verification.isVerified,
                    "Created & Verified",
                    "Verification Failed"
                ),
                verification.isVerified
            );

            break;

        case "Read":
            addDetail(
                "Target ID Queried",
                verification.targetId
            );

            addDetail(
                "Retrieved Value",
                verification.afterValue
            );

            addDetail(
                "Verification State",
                verificationText(
                    verification.isVerified,
                    "Record Verified",
                    "Verification Failed"
                ),
                verification.isVerified
            );

            break;

        case "Update":
            addDetail(
                "Original Item ID",
                verification.targetId
            );

            addDetail(
                "Updated Item ID",
                verification.updatedId
            );

            addDetail(
                "Generation Change",
                `${displayValue(verification.beforeGeneration)} → ` +
                `${displayValue(verification.afterGeneration)}`
            );

            addDetail(
                `${displayValue(verification.propertyName)} Change`,
                `${displayValue(verification.beforeValue)} → ` +
                `${displayValue(verification.afterValue)}`
            );

            addDetail(
                "Verification State",
                verificationText(
                    verification.isVerified,
                    "Value Verified",
                    "Value Mismatch"
                ),
                verification.isVerified
            );

            break;

        case "Delete":
            addDetail(
                "Sample Deleted Target",
                verification.targetId
            );

            addDetail(
                "Standard ID Query",
                verificationText(
                    verification.verifiedNotFound,
                    "No Items Found (Confirmed Absent)",
                    "Deletion Not Confirmed"
                ),
                verification.verifiedNotFound
            );

            break;

        default:
            addDetail(
                "Operation",
                verification.operation
            );

            break;
    }

    addDetail(
        "Cleanup Status",
        verificationText(
            verification.cleanedUp,
            "Completed",
            "Failed"
        ),
        verification.cleanedUp
    );

    if (
        verification.verificationNote &&
        ["Update", "Delete"].includes(verification.operation)
    ) {
        addDetail(
            "Audit Note",
            verification.verificationNote,
            null,
            true
        );
    }

    verificationContent.appendChild(grid);
    verificationPanel.classList.remove("hidden");
}

function showError(message) {
    resultsSection.classList.remove("hidden");

    statusBadge.textContent = "Failed";
    statusBadge.className = "badge failed";

    errorBanner.textContent = message;
    errorBanner.classList.remove("hidden");
}

function renderResults(data, isOk) {
    appState.reportsAvailable =
    data.reportsAvailable === true;

    resultsSection.classList.remove("hidden");

    errorBanner.classList.add("hidden");
    errorBanner.textContent = "";

    const statusNames = [
        "Pending",
        "Running",
        "Completed",
        "Failed"
    ];

    const status = typeof data.status === "number"
        ? statusNames[data.status] ?? "Unknown"
        : data.status ?? "Unknown";

    const isSuccess =
        isOk &&
        String(status).toLowerCase() === "completed";

    statusBadge.textContent = status;

    statusBadge.className = isSuccess
        ? "badge"
        : "badge failed";

    if (!isSuccess) {
        errorBanner.textContent =
            data.errorMessage ||
            data.message ||
            data.title ||
            "Benchmark operation failed.";

        errorBanner.classList.remove("hidden");
    }

    resetMetrics();

    const stats = data.statistics;

    if (stats) {
        metricThroughput.textContent =
            numberText(stats.throughputRequestsPerSecond, 2);

        metricAvg.textContent =
            numberText(stats.averageMs, 2);

        metricP95.textContent =
            numberText(stats.p95Ms, 2);

        metricP99.textContent =
            numberText(stats.p99Ms, 2);

        metricMinMax.textContent =
            `${numberText(stats.minMs, 1)} / ` +
            `${numberText(stats.maxMs, 1)}`;

        metricSuccessRate.textContent =
            `${displayValue(stats.successCount)} / ` +
            `${displayValue(stats.totalRequests)}`;
    }

    renderVerification(data.verification);
    renderAmlQueryResults(data.queryResult);

    syncReportButtons();
}

function validateParameters(count, parallel) {
    return (
        Number.isInteger(count) &&
        count >= BENCHMARK_LIMITS.minIterations &&
        count <= BENCHMARK_LIMITS.maxIterations &&
        Number.isInteger(parallel) &&
        parallel >= BENCHMARK_LIMITS.minConcurrency &&
        parallel <= BENCHMARK_LIMITS.maxConcurrency
    );
}

async function handleBenchmarkSubmit(event) {
    event.preventDefault();

    if (appState.submitting || appState.requiresLogin) {
        return;
    }

    const count = Number(iterations.value);
    const parallel = Number(concurrency.value);

    if (!validateParameters(count, parallel)) {
        window.alert(
            "Iterations must be " +
            `${BENCHMARK_LIMITS.minIterations}–` +
            `${BENCHMARK_LIMITS.maxIterations} and ` +
            "Concurrency must be " +
            `${BENCHMARK_LIMITS.minConcurrency}–` +
            `${BENCHMARK_LIMITS.maxConcurrency}.`
        );

        return;
    }

    const payload = {
        iterations: count,
        concurrency: parallel,
        workloadName: workloadName.value.trim() || null
    };

    if (benchmarkMode.value === "RawAml") {
        const result = updateAmlStatus();

        if (!result.valid) {
            window.alert(result.message);
            return;
        }

        payload.aml = getAmlPayloadValue();
    } else {
        payload.itemType = itemType.value;
        payload.action = operation.value;
    }

    const authRevisionAtStart = appState.authUiRevision;

    clearResults();
    setBusy(true);

    try {
        const csrfToken = await getCsrfToken();

        // Do not submit the captured payload under a different login.
        if (
            authRevisionAtStart !== appState.authUiRevision ||
            appState.requiresLogin
        ) {
            showError(
                "Authentication changed before the benchmark was sent. " +
                "Review the configuration and submit again."
            );

            return;
        }

        const response = await fetch("/api/benchmark", {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/json",
                "X-CSRF-TOKEN": csrfToken
            },
            body: JSON.stringify(payload)
        });

        let data;

        try {
            data = await response.json();
        } catch {
            throw new Error(
                `Invalid server response (HTTP ${response.status}).`
            );
        }

        if (
            !data ||
            typeof data !== "object" ||
            Array.isArray(data)
        ) {
            throw new Error(
                `Invalid server response (HTTP ${response.status}).`
            );
        }

       if (
            response.status === 401 &&
            data.requiresLogin === true
        ) {
            // Preserve the old run's result and available report entry.
            appState.currentJobId = data.jobId || null;
            appState.reportsAvailable =
                data.reportsAvailable === true;

            if (appState.currentJobId) {
                renderResults(data, false);
            } else {
                showError(
                    data.errorMessage ||
                    data.message ||
                    "Aras authentication expired. Please sign in again."
                );

                syncReportButtons();
            }

            // This helper ignores responses from an older login revision.
            showAuthenticationExpired(authRevisionAtStart);

            return;
        }

        if (!response.ok && !data.jobId) {
            throw new Error(
                data.message ||
                data.title ||
                data.errorMessage ||
                `Benchmark request failed (HTTP ${response.status}).`
            );
        }

        appState.currentJobId = data.jobId || null;

        renderResults(data, response.ok);
    } catch (error) {
        showError(
            error instanceof Error
                ? error.message
                : "Unexpected error."
        );

        renderVerification(null);
        renderAmlQueryResults(null);
        syncReportButtons();
    } finally {
        setBusy(false);
    }
}

export function initBenchmarkFeature() {
    iterations.min =
        String(BENCHMARK_LIMITS.minIterations);

    iterations.max =
        String(BENCHMARK_LIMITS.maxIterations);

    concurrency.min =
        String(BENCHMARK_LIMITS.minConcurrency);

    concurrency.max =
        String(BENCHMARK_LIMITS.maxConcurrency);

    form.addEventListener(
        "submit",
        handleBenchmarkSubmit
    );

    benchmarkMode.addEventListener("change", () => {
        updateMode();
        clearResults();
    });

    operation.addEventListener(
        "change",
        updateOperationDescription
    );

    updateMode();
    updateOperationDescription();
    clearResults();
}
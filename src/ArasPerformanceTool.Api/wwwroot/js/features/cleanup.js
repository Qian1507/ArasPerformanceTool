import { appState, byId, displayValue } from "../core/state.js";
import { getCsrfToken } from "../core/api.js";
import { showAuthenticationExpired } from "./login.js";

const refreshPendingCleanupBtn = byId("refresh-pending-cleanup");
const pendingCleanupMessage = byId("pending-cleanup-message");
const pendingCleanupList = byId("pending-cleanup-list");
const pendingCleanupErrors = byId("pending-cleanup-errors");

const pendingCleanupPreview = byId("pending-cleanup-preview");
const pendingCleanupPreviewMessage = byId("pending-cleanup-preview-message");
const pendingCleanupItems = byId("pending-cleanup-items");
const cleanPendingRunBtn = byId("clean-pending-run");
const pendingCleanupResult = byId("pending-cleanup-result");

let pendingCleanupLoading = false;
let pendingCleanupRunning = false;
let selectedCleanupRunId = null;

// Used to ignore outdated cleanup preview responses.
let cleanupPreviewRequestId = 0;

function createCleanupTable(headers, rows) {
    const wrapper = document.createElement("div");
    wrapper.className = "aml-table-wrapper";

    const table = document.createElement("table");
    table.className = "aml-result-table";

    const thead = document.createElement("thead");
    const headerRow = document.createElement("tr");

    for (const label of headers) {
        const th = document.createElement("th");
        th.scope = "col";
        th.textContent = label;
        headerRow.appendChild(th);
    }

    thead.appendChild(headerRow);
    table.appendChild(thead);

    const tbody = document.createElement("tbody");

    for (const values of rows) {
        const tr = document.createElement("tr");

        for (const value of values) {
            const td = document.createElement("td");

            if (value instanceof Node) {
                td.appendChild(value);
            } else {
                td.textContent = displayValue(value);
            }

            tr.appendChild(td);
        }

        tbody.appendChild(tr);
    }

    table.appendChild(tbody);
    wrapper.appendChild(table);

    return wrapper;
}

function cleanupDateText(value) {
    if (!value) return "—";

    const date = new Date(value);

    return Number.isNaN(date.getTime())
        ? "—"
        : date.toLocaleString();
}

function renderPendingCleanupRuns(data) {
    pendingCleanupList.replaceChildren();
    pendingCleanupErrors.replaceChildren();

    const runs = data.pendingRuns;
    const errors = data.errors;

    pendingCleanupMessage.textContent = runs.length > 0
        ? `${runs.length} pending cleanup run(s).`
        : errors.length > 0
            ? "No pending runs were loaded. Some manifests could not be scanned."
            : "No pending cleanup runs.";

    if (runs.length > 0) {
        const rows = runs.map(run => {
            const previewBtn = document.createElement("button");
            previewBtn.type = "button";
            previewBtn.className = "btn btn-secondary";
            previewBtn.textContent = "Preview";

            previewBtn.addEventListener("click", () => {
                if (pendingCleanupRunning) return;

                selectedCleanupRunId = null;
                cleanPendingRunBtn.disabled = true;

                loadPendingCleanupRun(run.runId);
            });

            return [
                run.runId,
                cleanupDateText(run.createdAtUtc),
                run.remainingCount,
                previewBtn
            ];
        });

        pendingCleanupList.appendChild(
            createCleanupTable(
                ["Run ID", "Created At", "Remaining", "Action"],
                rows
            )
        );
    }

    if (errors.length > 0) {
        const notice = document.createElement("p");
        notice.textContent =
            `${errors.length} manifest file(s) could not be scanned. ` +
            "The list may be incomplete.";

        pendingCleanupErrors.appendChild(notice);
    }
}

function renderPendingCleanupItems(data) {
    pendingCleanupItems.replaceChildren();
    pendingCleanupPreview.classList.remove("hidden");

    pendingCleanupPreviewMessage.textContent =
        `Run ${data.runId} — ${data.remainingCount} unresolved item(s).`;

    if (data.items.length === 0) {
        const notice = document.createElement("p");
        notice.textContent =
            "This Run currently has no unresolved items.";

        pendingCleanupItems.appendChild(notice);
        return;
    }

    const rows = data.items.map(item => [
        item.itemType,
        item.itemId,
        item.status
    ]);

    pendingCleanupItems.appendChild(
        createCleanupTable(
            ["ItemType", "Item ID", "Status"],
            rows
        )
    );
}

async function loadPendingCleanupRun(runId) {
    const requestId = ++cleanupPreviewRequestId;

    pendingCleanupPreview.classList.remove("hidden");
    pendingCleanupPreviewMessage.textContent =
        `Loading Run ${runId}...`;
    pendingCleanupItems.replaceChildren();

    try {
        const response = await fetch(
            `/api/Benchmark/pending-cleanup/${encodeURIComponent(runId)}`,
            { cache: "no-store" }
        );

        // Ignore a response if the user selected another Run or refreshed.
        if (requestId !== cleanupPreviewRequestId) return;

        if (response.status === 404) {
            pendingCleanupPreviewMessage.textContent =
                "This Run is unavailable. Refresh the pending-run list.";
            return;
        }

        if (!response.ok) {
            throw new Error(
                `Unable to load Run preview (HTTP ${response.status}).`
            );
        }

        const data = await response.json();

        if (requestId !== cleanupPreviewRequestId) return;

        if (!data || !Array.isArray(data.items)) {
            throw new Error("The server returned an invalid Run preview.");
        }

        renderPendingCleanupItems(data);

        // Enable cleanup only for the successfully loaded run.
        if (
            data.runId &&
            data.runId.toLowerCase().replace(/-/g, "") ===
                runId.toLowerCase().replace(/-/g, "") &&
            data.remainingCount > 0
        ) {
            selectedCleanupRunId = runId;
            cleanPendingRunBtn.disabled = false;
        }
    } catch (error) {
        if (requestId !== cleanupPreviewRequestId) return;

        selectedCleanupRunId = null;
        cleanPendingRunBtn.disabled = true;

        pendingCleanupPreviewMessage.textContent =
            error instanceof Error
                ? error.message
                : "Unable to load the Run preview.";
    }
}

export async function loadPendingCleanupRuns() {
    if (pendingCleanupLoading || pendingCleanupRunning) return;

    pendingCleanupLoading = true;
    refreshPendingCleanupBtn.disabled = true;

    pendingCleanupMessage.textContent = "Loading pending cleanup runs...";
    pendingCleanupList.replaceChildren();
    pendingCleanupErrors.replaceChildren();

    // Invalidate any older preview request.
    cleanupPreviewRequestId++;

    selectedCleanupRunId = null;
    cleanPendingRunBtn.disabled = true;

    pendingCleanupPreview.classList.add("hidden");
    pendingCleanupPreviewMessage.textContent = "";
    pendingCleanupItems.replaceChildren();

    try {
        const response = await fetch(
            "/api/Benchmark/pending-cleanup",
            { cache: "no-store" }
        );

        if (!response.ok) {
            throw new Error(
                `Unable to load pending runs (HTTP ${response.status}).`
            );
        }

        const data = await response.json();

        if (
            !data ||
            !Array.isArray(data.pendingRuns) ||
            !Array.isArray(data.errors)
        ) {
            throw new Error("The server returned an invalid pending-run list.");
        }

        renderPendingCleanupRuns(data);
    } catch (error) {
        pendingCleanupMessage.textContent =
            error instanceof Error
                ? error.message
                : "Unable to load pending cleanup runs.";
    } finally {
        pendingCleanupLoading = false;
        refreshPendingCleanupBtn.disabled = false;
    }
}

async function cleanSelectedPendingRun() {
    if (
        pendingCleanupRunning ||
        pendingCleanupLoading ||
        !selectedCleanupRunId
    ) {
        return;
    }

    const runId = selectedCleanupRunId;
    const authRevisionAtStart = appState.authUiRevision;

    const confirmed = window.confirm(
        `Clean test data for Run ${runId}?\n\n` +
        "Only tracked test items will be processed."
    );

    if (!confirmed) return;

    pendingCleanupRunning = true;
    selectedCleanupRunId = null;
    cleanPendingRunBtn.disabled = true;
    refreshPendingCleanupBtn.disabled = true;

    // Prevent an in-flight preview from changing the selected Run.
    cleanupPreviewRequestId++;

    pendingCleanupResult.textContent = "Cleaning test data...";

    try {
        const csrfToken = await getCsrfToken();

        const response = await fetch(
            `/api/Benchmark/pending-cleanup/${encodeURIComponent(runId)}/cleanup`,
            {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "X-CSRF-TOKEN": csrfToken
                }
            }
        );

        const data = await response.json();

        if (response.status === 401 && data?.requiresLogin === true) {
            showAuthenticationExpired(authRevisionAtStart);

            pendingCleanupResult.textContent =
                "Authentication expired during cleanup. " +
                "Sign in again, then review the pending Run.";

            return;
        }

        if (!response.ok) {
            throw new Error(
                data?.message ||
                data?.error ||
                `Cleanup failed (HTTP ${response.status}).`
            );
        }

        pendingCleanupResult.textContent =
            `Cleanup result — ` +
            `Found: ${data.found ?? "-"}, ` +
            `Deleted: ${data.deleted ?? "-"}, ` +
            `Already missing: ${data.alreadyMissing ?? "-"}, ` +
            `Failed: ${data.failed ?? "-"}, ` +
            `Remaining: ${data.remaining ?? "-"}.`;

        // Hide stale preview after cleanup.
        pendingCleanupPreview.classList.add("hidden");
        pendingCleanupPreviewMessage.textContent = "";
        pendingCleanupItems.replaceChildren();

        selectedCleanupRunId = null;
        cleanPendingRunBtn.disabled = true;
    } catch (error) {
        pendingCleanupResult.textContent =
            error instanceof Error
                ? error.message
                : "Unable to complete cleanup.";
    } finally {
        pendingCleanupRunning = false;
        refreshPendingCleanupBtn.disabled = false;

        if (!appState.requiresLogin) {
            await loadPendingCleanupRuns();
        }
    }
}

export function initCleanupFeature() {
    refreshPendingCleanupBtn.addEventListener("click", loadPendingCleanupRuns);
    cleanPendingRunBtn.addEventListener("click", cleanSelectedPendingRun);

    loadPendingCleanupRuns();
}
import {
    appState,
    byId,
    displayValue
} from "../core/state.js";

import { getCsrfToken } from "../core/api.js";
import { showAuthenticationExpired } from "./login.js";

const refreshPendingCleanupBtn = byId("refresh-pending-cleanup");
const pendingCleanupMessage = byId("pending-cleanup-message");
const pendingCleanupList = byId("pending-cleanup-list");
const pendingCleanupErrors = byId("pending-cleanup-errors");

const pendingCleanupPreview = byId("pending-cleanup-preview");
const pendingCleanupPreviewMessage = byId(
    "pending-cleanup-preview-message"
);
const pendingCleanupItems = byId("pending-cleanup-items");
const cleanPendingRunBtn = byId("clean-pending-run");

const pendingCleanupResult = byId("pending-cleanup-result");

let pendingCleanupLoading = false;
let pendingCleanupRunning = false;
let selectedCleanupRunId = null;

// These IDs invalidate responses from older list/preview requests.
let cleanupListRequestId = 0;
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

function normalizeRunId(value) {
    return typeof value === "string"
        ? value.toLowerCase().replace(/-/g, "")
        : "";
}

function clearCleanupPreview() {
    selectedCleanupRunId = null;
    cleanPendingRunBtn.disabled = true;

    pendingCleanupPreview.classList.add("hidden");
    pendingCleanupPreviewMessage.textContent = "";
    pendingCleanupItems.replaceChildren();
}

export function resetCleanupState() {
    // Older list/preview requests must not restore previous data.
    cleanupListRequestId++;
    cleanupPreviewRequestId++;

    // A new environment may immediately start its own list request.
    // Older list requests cannot update this flag in their finally block.
    pendingCleanupLoading = false;

    pendingCleanupMessage.textContent = "";
    pendingCleanupList.replaceChildren();
    pendingCleanupErrors.replaceChildren();

    clearCleanupPreview();
    pendingCleanupResult.textContent = "";

    // Do not reset pendingCleanupRunning:
    // clearing the UI does not end an in-flight cleanup request.
    refreshPendingCleanupBtn.disabled =
        appState.requiresLogin || pendingCleanupRunning;
}

function renderPendingCleanupRuns(data) {
    pendingCleanupList.replaceChildren();
    pendingCleanupErrors.replaceChildren();

    const runs = data.pendingRuns;
    const errors = data.errors;

    pendingCleanupMessage.textContent = runs.length > 0
        ? `${runs.length} pending cleanup run(s).`
        : errors.length > 0
            ? "No pending runs were loaded. " +
              "Some manifests could not be scanned."
            : "No pending cleanup runs.";

    if (runs.length > 0) {
        const rows = runs.map(run => {
            const previewBtn = document.createElement("button");

            previewBtn.type = "button";
            previewBtn.className = "btn btn-secondary";
            previewBtn.textContent = "Preview";

            previewBtn.addEventListener("click", () => {
                if (
                    appState.requiresLogin ||
                    pendingCleanupLoading ||
                    pendingCleanupRunning
                ) {
                    return;
                }

                selectedCleanupRunId = null;
                cleanPendingRunBtn.disabled = true;

                void loadPendingCleanupRun(run.runId);
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
        `Run ${data.runId} — ` +
        `${data.remainingCount} unresolved item(s).`;

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
    if (
        appState.requiresLogin ||
        pendingCleanupLoading ||
        pendingCleanupRunning
    ) {
        return;
    }

    const requestId = ++cleanupPreviewRequestId;
    const authRevisionAtStart = appState.authUiRevision;

    const isCurrentRequest = () =>
        requestId === cleanupPreviewRequestId &&
        authRevisionAtStart === appState.authUiRevision &&
        !appState.requiresLogin;

    selectedCleanupRunId = null;
    cleanPendingRunBtn.disabled = true;

    pendingCleanupResult.textContent = "";

    pendingCleanupPreview.classList.remove("hidden");
    pendingCleanupPreviewMessage.textContent =
        `Loading Run ${runId}...`;

    pendingCleanupItems.replaceChildren();

    try {
        const response = await fetch(
            `/api/Benchmark/pending-cleanup/${encodeURIComponent(runId)}`,
            {
                credentials: "same-origin",
                cache: "no-store"
            }
        );

        if (!isCurrentRequest()) return;

        if (response.status === 404) {
            pendingCleanupPreviewMessage.textContent =
                "This Run is unavailable. " +
                "Refresh the pending-run list.";

            return;
        }

        if (!response.ok) {
            throw new Error(
                `Unable to load Run preview (HTTP ${response.status}).`
            );
        }

        const data = await response.json();

        if (!isCurrentRequest()) return;

        if (!data || !Array.isArray(data.items)) {
            throw new Error(
                "The server returned an invalid Run preview."
            );
        }

        if (
            !normalizeRunId(data.runId) ||
            normalizeRunId(data.runId) !== normalizeRunId(runId)
        ) {
            throw new Error(
                "The server returned a different Run preview."
            );
        }

        renderPendingCleanupItems(data);

        // Only a current, successfully loaded preview can enable cleanup.
        if (data.remainingCount > 0) {
            selectedCleanupRunId = runId;
            cleanPendingRunBtn.disabled = false;
        }
    } catch (error) {
        if (!isCurrentRequest()) return;

        selectedCleanupRunId = null;
        cleanPendingRunBtn.disabled = true;

        pendingCleanupPreviewMessage.textContent =
            error instanceof Error
                ? error.message
                : "Unable to load the Run preview.";
    }
}

export async function loadPendingCleanupRuns() {
    if (
        appState.requiresLogin ||
        pendingCleanupLoading ||
        pendingCleanupRunning
    ) {
        return;
    }

    const requestId = ++cleanupListRequestId;
    const authRevisionAtStart = appState.authUiRevision;

    const isCurrentRequest = () =>
        requestId === cleanupListRequestId &&
        authRevisionAtStart === appState.authUiRevision &&
        !appState.requiresLogin;

    pendingCleanupLoading = true;
    refreshPendingCleanupBtn.disabled = true;

    pendingCleanupMessage.textContent =
        "Loading pending cleanup runs...";

    pendingCleanupList.replaceChildren();
    pendingCleanupErrors.replaceChildren();

    // Refreshing the list invalidates an older preview request.
    cleanupPreviewRequestId++;
    clearCleanupPreview();

    try {
        const response = await fetch(
            "/api/Benchmark/pending-cleanup",
            {
                credentials: "same-origin",
                cache: "no-store"
            }
        );

        if (!isCurrentRequest()) return;

        if (!response.ok) {
            throw new Error(
                `Unable to load pending runs (HTTP ${response.status}).`
            );
        }

        const data = await response.json();

        if (!isCurrentRequest()) return;

        if (
            !data ||
            !Array.isArray(data.pendingRuns) ||
            !Array.isArray(data.errors)
        ) {
            throw new Error(
                "The server returned an invalid pending-run list."
            );
        }

        renderPendingCleanupRuns(data);
    } catch (error) {
        if (!isCurrentRequest()) return;

        pendingCleanupMessage.textContent =
            error instanceof Error
                ? error.message
                : "Unable to load pending cleanup runs.";
    } finally {
        // An older request must not reset a newer request's loading flag.
        if (requestId === cleanupListRequestId) {
            pendingCleanupLoading = false;

            refreshPendingCleanupBtn.disabled =
                appState.requiresLogin || pendingCleanupRunning;
        }
    }
}

async function cleanSelectedPendingRun() {
    if (
        appState.requiresLogin ||
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

    // Prevent an older preview from enabling cleanup again.
    cleanupPreviewRequestId++;

    pendingCleanupResult.textContent = "Cleaning test data...";

    try {
        const csrfToken = await getCsrfToken();

        // Do not submit if authentication changed while obtaining CSRF.
        if (
            authRevisionAtStart !== appState.authUiRevision ||
            appState.requiresLogin
        ) {
            return;
        }

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

        // An old cleanup response must not modify a newer login's UI.
        if (authRevisionAtStart !== appState.authUiRevision) {
            return;
        }

        if (response.status === 401 && data?.requiresLogin === true) {
            showAuthenticationExpired(authRevisionAtStart);

            pendingCleanupResult.textContent =
                "Authentication expired during cleanup. " +
                "Sign in again, then review the pending Run.";

            return;
        }

        // Another request may already have marked authentication expired.
        if (appState.requiresLogin) {
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
            "Cleanup result — " +
            `Found: ${data.found ?? "-"}, ` +
            `Deleted: ${data.deleted ?? "-"}, ` +
            `Already missing: ${data.alreadyMissing ?? "-"}, ` +
            `Failed: ${data.failed ?? "-"}, ` +
            `Remaining: ${data.remaining ?? "-"}.`;

        clearCleanupPreview();
    } catch (error) {
        if (
            authRevisionAtStart !== appState.authUiRevision ||
            appState.requiresLogin
        ) {
            return;
        }

        pendingCleanupResult.textContent =
            error instanceof Error
                ? error.message
                : "Unable to complete cleanup.";
    } finally {
        pendingCleanupRunning = false;

        refreshPendingCleanupBtn.disabled =
            appState.requiresLogin || pendingCleanupLoading;

        // Refresh only the current authenticated environment.
        // This is a read-only list refresh, not a cleanup retry.
        if (!appState.requiresLogin) {
            await loadPendingCleanupRuns();
        }
    }
}

export function initCleanupFeature() {
    refreshPendingCleanupBtn.addEventListener(
        "click",
        loadPendingCleanupRuns
    );

    cleanPendingRunBtn.addEventListener(
        "click",
        cleanSelectedPendingRun
    );

    // app.js loads pending runs after authentication is confirmed.
}
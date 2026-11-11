
"use strict";

document.addEventListener("DOMContentLoaded", () => {
// =====================================================
    // 1. DOM References
    // =====================================================

    const byId = id => document.getElementById(id);

    // Aras Login
    const loginForm = byId("login-form");
    const arasUsername = byId("arasUsername");
    const arasPassword = byId("arasPassword");
    const arasDatabase = byId("arasDatabase");
    const loginSection = byId("login-section");
    const dashboardSection = byId("dashboard-section");

    const reloadDatabasesBtn = byId("reload-databases-btn");
    const databaseStatus = byId("database-status");
    const loginStatus = byId("login-status");
    const loginBtn = byId("login-btn");

    const sessionInfo = byId("session-info");
    const logoutBtn = byId("logout-btn");
    const logoutStatus = byId("logout-status");

   
    const form = byId("benchmark-form");
    const benchmarkMode = byId("benchmarkMode");

    const standardGroup = byId("standard-config-group");
    const customGroup = byId("custom-aml-group");

    const itemType = byId("itemType");
    const operation = byId("operation");
    const operationDescription = byId("operation-description");

    const amlPayload = byId("amlPayload");
    const parseStatus = byId("aml-parse-status");

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

    const downloadJsonBtn = byId("download-json-btn");
    const downloadCsvBtn = byId("download-csv-btn");

    const clearAmlBtn = document.getElementById("clearAmlBtn");

    // Operation Details panel
    const vPanel = byId("verification-panel");
    const vContent = byId("verification-content");


    // Pending Cleanup panel
    const refreshPendingCleanupBtn = byId("refresh-pending-cleanup");
    const pendingCleanupMessage = byId("pending-cleanup-message");
    const pendingCleanupList = byId("pending-cleanup-list");
    const pendingCleanupErrors = byId("pending-cleanup-errors");

    const pendingCleanupPreview = byId("pending-cleanup-preview");
    const pendingCleanupPreviewMessage =
        byId("pending-cleanup-preview-message");
    const pendingCleanupItems = byId("pending-cleanup-items");
     const cleanPendingRunBtn = byId("clean-pending-run");
    const pendingCleanupResult = byId("pending-cleanup-result");

    // Authentication
    let databasesLoading = false;
    let loggingOut = false;
    // Benchmark execution
    let currentJobId = null;
    let submitting = false;
    
    // Pending cleanup
    let pendingCleanupLoading = false;
    let pendingCleanupRunning = false;
    let selectedCleanupRunId = null;

    // Used to ignore outdated cleanup preview responses.
    let cleanupPreviewRequestId = 0;



    // =====================================================
    // Aras Database Selection
    // =====================================================

    function setDatabasePlaceholder(text) {
        const option = document.createElement("option");

        option.value = "";
        option.textContent = text;
        option.selected = true;

        arasDatabase.replaceChildren(option);
    }

    async function loadDatabases() {
        if (databasesLoading) {
            return;
        }

        const previousDatabase = arasDatabase.value;

        databasesLoading = true;
        arasDatabase.disabled = true;
        reloadDatabasesBtn.disabled = true;
        loginBtn.disabled = true;

        databaseStatus.classList.remove("login-status-error");
        databaseStatus.textContent = "Loading Aras databases...";

        setDatabasePlaceholder("Loading databases...");

        try {
            const csrfToken = await getCsrfToken();
            const response = await fetch("/api/auth/databases", {
                cache: "no-store",
                headers: {
                    "Accept": "application/json"
                }
            });

            let data;

            try {
                data = await response.json();
            } catch {
                throw new Error(
                    `Invalid database list response (HTTP ${response.status}).`
                );
            }

            if (!response.ok) {
                throw new Error(
                    data?.message ||
                    `Unable to load databases (HTTP ${response.status}).`
                );
            }

            if (!Array.isArray(data?.databases)) {
                throw new Error(
                    "The server returned an invalid database list."
                );
            }

            const databases = [
                ...new Set(
                    data.databases
                        .filter(value => typeof value === "string")
                        .map(value => value.trim())
                        .filter(Boolean)
                )
            ];

            if (databases.length === 0) {
                throw new Error(
                    "No selectable Aras databases are available."
                );
            }

            arasDatabase.replaceChildren();

            // Multiple databases require an explicit selection.
            if (databases.length > 1) {
                const placeholder = document.createElement("option");

                placeholder.value = "";
                placeholder.textContent = "Select a database...";
                placeholder.selected = true;
                placeholder.disabled = true;

                arasDatabase.appendChild(placeholder);
            }

            for (const database of databases) {
                const option = document.createElement("option");

                option.value = database;
                option.textContent = database;

                arasDatabase.appendChild(option);
            }

            if (databases.includes(previousDatabase)) {
                arasDatabase.value = previousDatabase;
            } else if (databases.length === 1) {
                arasDatabase.value = databases[0];
            } else {
                arasDatabase.value = "";
            }

            arasDatabase.disabled = false;

            databaseStatus.textContent =
                databases.length === 1
                    ? "1 database available."
                    : `${databases.length} databases available.`;
        } catch (error) {
            setDatabasePlaceholder("Unable to load databases");

            databaseStatus.textContent =
                error instanceof Error
                    ? error.message
                    : "Unable to load Aras databases.";

            databaseStatus.classList.add("login-status-error");
        } finally {
            databasesLoading = false;
            reloadDatabasesBtn.disabled = false;

            // Enable login only when a valid database is selected.
            loginBtn.disabled = arasDatabase.disabled || !arasDatabase.value;
        }
    }

    reloadDatabasesBtn.addEventListener("click", loadDatabases);

    // Aras Login / Logout event handlers
    loginForm.addEventListener("submit", loginToAras);
    logoutBtn.addEventListener("click", logoutFromAras);
    
    arasDatabase.addEventListener("change", () => {
        loginBtn.disabled =
            databasesLoading ||
            arasDatabase.disabled ||
            !arasDatabase.value;
    });

    // =====================================================
    // Aras Login
    // =====================================================

    // Show only the appropriate section for the login state.
    function setAuthenticatedUI(authenticated) {
        loginSection.hidden = authenticated;
        dashboardSection.hidden = !authenticated;
    }

    async function getCsrfToken() {
        const response = await fetch("/api/auth/csrf", {
            credentials: "same-origin",
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error("Failed to get CSRF token.");
        }

        const data = await response.json();

        if (!data?.requestToken) {
            throw new Error("CSRF token is missing.");
        }

        return data.requestToken;
    }

    async function loginToAras(event) {
        event.preventDefault();

        if (databasesLoading || !arasDatabase.value) {
            return;
        }

        loginBtn.disabled = true;
        loginStatus.classList.remove("login-status-error");
        loginStatus.textContent = "Connecting to Aras...";

        try {
            const csrfToken = await getCsrfToken();

            const response = await fetch("/api/auth/login", {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    "X-CSRF-TOKEN": csrfToken
                },
                body: JSON.stringify({
                    username: arasUsername.value.trim(),
                    password: arasPassword.value,
                    database: arasDatabase.value
                })
            });

            let result;

            try {
                result = await response.json();
            } catch {
                throw new Error(
                    `Invalid login response (HTTP ${response.status}).`
                );
            }

            if (!response.ok) {
                throw new Error(
                    result?.message ||
                    `Aras login failed (HTTP ${response.status}).`
                );
            }

            if (result?.authenticated !== true) {
                throw new Error("Login was not confirmed by the server.");
            }

            loginStatus.textContent =
                `Connected to ${result.database} as ${result.username}.`;

            sessionInfo.textContent =
                `${result.username} · ${result.database}`;

            setAuthenticatedUI(true);

            // Dashboard switching will be implemented later.
        } catch (error) {
            loginStatus.textContent =
                error instanceof Error
                    ? error.message
                    : "Unable to connect to Aras.";

            loginStatus.classList.add("login-status-error");
        } finally {
            // Never keep the password after a login attempt.
            arasPassword.value = "";

            loginBtn.disabled =
                databasesLoading ||
                arasDatabase.disabled ||
                !arasDatabase.value;
        }
    }

    // =====================================================
    // Aras Logout
    // =====================================================

    async function logoutFromAras() {
        if (loggingOut) return;

        loggingOut = true;
        logoutBtn.disabled = true;
        logoutStatus.textContent = "Logging out...";
        logoutStatus.classList.remove("login-status-error");

        try {
            const csrfToken = await getCsrfToken();

            const response = await fetch("/api/auth/logout", {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "X-CSRF-TOKEN": csrfToken
                }
            });

            if (!response.ok) {
                throw new Error(`Logout failed (HTTP ${response.status}).`);
            }

            // Clear UI state after the server confirms logout.
            sessionInfo.textContent = "";
            loginStatus.textContent = "";
            logoutStatus.textContent = "";
            arasPassword.value = "";

            setAuthenticatedUI(false);

            // Refresh available databases for the next login.
            await loadDatabases();
        } catch (error) {
            logoutStatus.textContent =
                error instanceof Error
                    ? error.message
                    : "Unable to log out.";

            logoutStatus.classList.add("login-status-error");
        } finally {
            loggingOut = false;
            logoutBtn.disabled = false;
        }
    }
    async function restoreSession() {
    try {
        const response = await fetch("/api/auth/session", {
            credentials: "same-origin",
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error("Unable to check login session.");
        }

        const session = await response.json();
        const authenticated = session.authenticated === true;

        sessionInfo.textContent = authenticated
            ? `${session.username} · ${session.database}`
            : "";

        setAuthenticatedUI(authenticated);

    } catch (error) {
        console.error("Session check failed:", error);
        setAuthenticatedUI(false);
    }
}
    // =====================================================
    // 2. Benchmark Mode and Operation Description
    // =====================================================

    function updateMode() {
        const isRawAml = benchmarkMode.value === "RawAml";

        standardGroup.classList.toggle("hidden", isRawAml);
        customGroup.classList.toggle("hidden", !isRawAml);
    }

    function updateOperationDescription() {
        const descriptions = {
            Create:
                "Creates temporary test items and cleans them up after the benchmark.",

            Read:
                "Creates one temporary item, reads it repeatedly, and cleans it up.",

            Update:
                "Creates one temporary item per iteration and updates its name. Aras may create a new generation.",

            Delete:
                "Creates one temporary item per iteration, deletes each item, and verifies the results."
        };

        if (operationDescription) {
            operationDescription.textContent =
                descriptions[operation.value] || "";
        }
    }

        // Clear Custom AML input
        clearAmlBtn?.addEventListener("click", () => {
            const amlInput = document.getElementById("amlPayload");

            if (!amlInput) return;

            amlInput.value = "";
            amlInput.dispatchEvent(new Event("input", { bubbles: true }));
            amlInput.focus();
        });


    // =====================================================
    // 3. Custom AML Syntax Validation
    // =====================================================

    function inspectAml(raw) {
        if (!raw.trim()) {
            return {
                valid: false,
                empty: true,
                message: "Enter a read-only AML query."
            };
        }

        const doc = new DOMParser().parseFromString(
            raw,
            "application/xml"
        );

        if (doc.getElementsByTagName("parsererror").length > 0) {
            return {
                valid: false,
                message: "Invalid XML syntax."
            };
        }

        const root = doc.documentElement;

        if (!root || !["AML", "Item"].includes(root.localName)) {
            return {
                valid: false,
                message: "Root element must be <AML> or <Item>."
            };
        }

        const items = root.localName === "Item"
            ? [root]
            : Array.from(root.children).filter(
                el => el.localName === "Item"
            );

        if (items.length !== 1) {
            return {
                valid: false,
                message: "Enter exactly one top-level <Item> query."
            };
        }

        const type = items[0].getAttribute("type") || "unknown";
        const action = items[0].getAttribute("action") || "unknown";

        return {
            valid: true,
            message:
                `XML parsed: [${action}] on <${type}> ` +
                "(server validation required)"
        };
    }

    function updateAmlStatus() {
        const result = inspectAml(amlPayload.value);

        parseStatus.textContent = result.message;

        parseStatus.className =
            result.valid || result.empty
                ? "parse-status"
                : "parse-status parse-error";

        return result;
    }

    // =====================================================
    // 4. UI Helpers
    // =====================================================

    function setBusy(busy) {
        submitting = busy;

        executeBtn.disabled = busy;

        executeBtnText.textContent = busy
            ? "Running Benchmark..."
            : "Execute Benchmark";

        loadingSpinner.classList.toggle("hidden", !busy);
    }

    function numberText(value, decimals = 2) {
        if (value === null || value === undefined) {
            return "—";
        }

        const number = Number(value);

        return Number.isFinite(number)
            ? number.toFixed(decimals)
            : "—";
    }

    function displayValue(value) {
        return value === null ||
            value === undefined ||
            value === ""
            ? "—"
            : String(value);
    }

    function verificationText(value, success, failure) {
        if (value === true) return success;
        if (value === false) return failure;

        return "Not verified";
    }

    function clearResults() {
        currentJobId = null;

        resultsSection.classList.add("hidden");

        errorBanner.classList.add("hidden");
        errorBanner.textContent = "";

        statusBadge.textContent = "Pending";
        statusBadge.className = "badge";

        const metricElements = [
            metricThroughput,
            metricAvg,
            metricP95,
            metricP99,
            metricMinMax,
            metricSuccessRate
        ];

        metricElements.forEach(element => {
            element.textContent = "—";
        });

        renderVerification(null);

        downloadJsonBtn.disabled = true;
        downloadCsvBtn.disabled = true;
    }


    // =====================================================
    // 5. Custom AML Query Result Details
// =====================================================

function renderAmlQueryResults(queryResult) {
    const panel = document.getElementById("aml-result-panel");
    const content = document.getElementById("aml-result-content");
    const count = document.getElementById("aml-result-count");
    const xmlPreview = document.getElementById("aml-response-xml");

    if (!panel || !content || !count || !xmlPreview) return;

    content.replaceChildren();
    count.textContent = "";
    xmlPreview.textContent = "";

    if (!queryResult?.responseXml) {
        panel.classList.add("hidden");
        return;
    }

    const xml = queryResult.responseXml;
    const documentXml = new DOMParser().parseFromString(
        xml,
        "application/xml"
    );

    if (documentXml.getElementsByTagName("parsererror").length > 0) {
        panel.classList.remove("hidden");
        content.textContent = "Unable to parse the AML response XML.";
        xmlPreview.textContent = xml;
        return;
    }

    panel.classList.remove("hidden");
    xmlPreview.textContent = xml;

    const items = Array.from(
        documentXml.getElementsByTagName("*")
    ).filter(element => element.localName === "Item");

    count.textContent = `${items.length} Items`;

    if (items.length === 0) {
        content.textContent = "No items returned.";
        return;
    }

    const maxRows = 50;
    const visibleItems = items.slice(0, maxRows);

    // Collect fields dynamically from returned XML.
    const rows = visibleItems.map(item => {
        const row = {
            ItemType: item.getAttribute("type") || "Unknown",
            ID: item.getAttribute("id") || ""
        };

        for (const child of item.children) {
            if (child.localName === "Relationships") continue;
            
             // ID is already displayed from the Item attribute.
            if (child.localName.toLowerCase() === "id") continue;

            // Avoid flattening nested Item elements into text.
            if (Array.from(child.getElementsByTagName("*"))
                .some(element => element.localName === "Item")) {
                continue;
            }

            row[child.localName] = child.textContent?.trim() || "";
        }

        return row;
    });

    const columns = [
        "ItemType",
        "ID",
        ...new Set(rows.flatMap(row =>
            Object.keys(row).filter(key =>
                key !== "ItemType" && key !== "ID"
            )
        ))
    ];

    // Limit columns to keep the table readable.
    const visibleColumns = columns.slice(0, 14);

    const wrapper = document.createElement("div");
    wrapper.className = "aml-table-wrapper";

    const table = document.createElement("table");
    table.className = "aml-result-table";

    const thead = document.createElement("thead");
    const headerRow = document.createElement("tr");

    visibleColumns.forEach(column => {
        const th = document.createElement("th");
        th.textContent = column;
        headerRow.appendChild(th);
    });

    thead.appendChild(headerRow);
    table.appendChild(thead);

    const tbody = document.createElement("tbody");

    rows.forEach(row => {
        const tr = document.createElement("tr");

        visibleColumns.forEach(column => {
            const td = document.createElement("td");
            td.textContent = row[column] ?? "";
            tr.appendChild(td);
        });

        tbody.appendChild(tr);
    });

    table.appendChild(tbody);
    wrapper.appendChild(table);
    content.appendChild(wrapper);

    if (items.length > maxRows) {
        const notice = document.createElement("p");
        notice.className = "aml-result-notice";
        notice.textContent =
            `Showing the first ${maxRows} of ${items.length} returned Items.`;
        content.appendChild(notice);
    }
}
    // =====================================================
    // 6. Dynamic CRUD Verification Rendering
    // =====================================================

    function renderVerification(v) {
        if (!vPanel || !vContent) {
            return;
        }

        // Clear the previous benchmark details.
        vContent.replaceChildren();

        if (!v) {
            vPanel.classList.add("hidden");
            return;
        }

        const grid = document.createElement("div");
        grid.className = "verification-grid";

        // Create elements safely without HTML string injection.
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

        switch (v.operation) {

            // ---------------------------------------------
            // CREATE
            // ---------------------------------------------
            case "Create":
                addDetail(
                    "Sample Item ID",
                    v.targetId
                );
                addDetail(
                    "Sample Name",
                    v.afterValue
                );
                addDetail(
                    "Verification State",
                    verificationText(
                        v.isVerified,
                        "Created & Verified",
                        "Verification Failed"
                    ),
                    v.isVerified
                );
                break;

            // ---------------------------------------------
            // READ
            // ---------------------------------------------
            case "Read":
                addDetail(
                    "Target ID Queried",
                    v.targetId
                );

                addDetail(
                    "Retrieved Value",
                    v.afterValue
                );

                addDetail(
                    "Verification State",
                    verificationText(
                        v.isVerified,
                        "Record Verified",
                        "Verification Failed"
                    ),
                    v.isVerified
                );
                break;

            // ---------------------------------------------
            // UPDATE
            // ---------------------------------------------
            case "Update":
                addDetail(
                    "Original Item ID",
                    v.targetId
                );

                addDetail(
                    "Updated Item ID",
                    v.updatedId
                );

                addDetail(
                    "Generation Change",
                    `${displayValue(v.beforeGeneration)} → ` +
                    `${displayValue(v.afterGeneration)}`
                );

                addDetail(
                    `${displayValue(v.propertyName)} Change`,
                    `${displayValue(v.beforeValue)} → ` +
                    `${displayValue(v.afterValue)}`
                );

                addDetail(
                    "Verification State",
                    verificationText(
                        v.isVerified,
                        "Value Verified",
                        "Value Mismatch"
                    ),
                    v.isVerified
                );
                break;

            // ---------------------------------------------
            // DELETE
            // ---------------------------------------------
            case "Delete":
                addDetail(
                    "Sample Deleted Target",
                    v.targetId
                );

                addDetail(
                    "Standard ID Query",
                    verificationText(
                        v.verifiedNotFound,
                        "No Items Found (Confirmed Absent)",
                        "Deletion Not Confirmed"
                    ),
                    v.verifiedNotFound
                );
                break;

            default:
                addDetail(
                    "Operation",
                    v.operation
                );
                break;
        }

        // Cleanup is tracked separately from CRUD verification.
        addDetail(
            "Cleanup Status",
            verificationText(
                v.cleanedUp,
                "Completed",
                "Failed"
            ),
            v.cleanedUp
        );

       // Hide redundant audit notes for Create,delete.
        if (v.verificationNote && ["Update", "Delete"].includes(v.operation)) {
            addDetail(
                "Audit Note",
                v.verificationNote,
                null,
                true
            );
        }

        vContent.appendChild(grid);
        vPanel.classList.remove("hidden");
    }

    // =====================================================
    // 7. Render Benchmark Results
    // =====================================================

    function renderResults(data, isOk) {
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
            const errorMsg =
                data.errorMessage ||
                data.message ||
                data.title ||
                "Benchmark operation failed.";

            errorBanner.textContent = errorMsg;
            errorBanner.classList.remove("hidden");
        }

        // Performance metrics
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

        // Dynamic Operation Details
        renderVerification(data.verification);
        renderAmlQueryResults(data.queryResult);

        downloadJsonBtn.disabled = !currentJobId;
        downloadCsvBtn.disabled = !currentJobId;
    }

    // =====================================================
    // 8. Submit Benchmark Request
    // =====================================================

    form.addEventListener("submit", async event => {
        event.preventDefault();

        if (submitting) {
            return;
        }

        const count = Number(iterations.value);
        const parallel = Number(concurrency.value);

        if (
            !Number.isInteger(count) ||
            count < 1 ||
            count > 1000 ||
            !Number.isInteger(parallel) ||
            parallel < 1 ||
            parallel > 50
        ) {
            window.alert(
                "Iterations must be 1–1000 and " +
                "Concurrency must be 1–50."
            );
            return;
        }

        const payload = {
            iterations: count,
            concurrency: parallel,
            workloadName: workloadName.value.trim() || null
        };

        // Custom AML mode
        if (benchmarkMode.value === "RawAml") {
            const result = updateAmlStatus();

            if (!result.valid) {
                window.alert(result.message);
                return;
            }

            payload.aml = amlPayload.value.trim();
        }

        // Standard CRUD mode
        else {
            payload.itemType = itemType.value;
            payload.action = operation.value;
        }

       clearResults();
        setBusy(true);

        try {
            // Obtain a CSRF token before submitting the benchmark.
            const csrfToken = await getCsrfToken();

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

            // Validation errors generally have no JobId.
            if (!response.ok && !data.jobId) {
                throw new Error(
                    data.message ||
                    data.title ||
                    data.errorMessage ||
                    `Benchmark request failed (HTTP ${response.status}).`
                );
            }

            currentJobId = data.jobId || null;

            renderResults(data, response.ok);
        } catch (error) {
            resultsSection.classList.remove("hidden");

            statusBadge.textContent = "Failed";
            statusBadge.className = "badge failed";

            errorBanner.textContent =
                error instanceof Error
                    ? error.message
                    : "Unexpected error.";

            errorBanner.classList.remove("hidden");

            renderVerification(null);
        } finally {
            setBusy(false);
        }
    });

    // =====================================================
    // 9. Report Downloads
    // =====================================================

    function downloadReport(format) {
        if (!currentJobId) {
            return;
        }

        window.location.href =
            `/api/report/${encodeURIComponent(currentJobId)}/${format}`;
    }

    downloadJsonBtn.addEventListener("click", () => {
        downloadReport("summary-pdf");
    });

    downloadCsvBtn.addEventListener("click", () => {
        downloadReport("measurements-pdf");
    });


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

    async function loadPendingCleanupRuns() {
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
                { cache: "no-store" });

            if (!response.ok) {
                throw new Error(
                    `Unable to load pending runs (HTTP ${response.status}).`);
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
        const response = await fetch(
            `/api/Benchmark/pending-cleanup/${encodeURIComponent(runId)}/cleanup`,
            { method: "POST" }
        );

        const data = await response.json();

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

        await loadPendingCleanupRuns();
    }
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
                    rows));
        }

        if (errors.length > 0) {
            const notice = document.createElement("p");
            notice.textContent =
                `${errors.length} manifest file(s) could not be scanned. ` +
                "The list may be incomplete.";

            pendingCleanupErrors.appendChild(notice);
        }
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
                { cache: "no-store" });

            // Ignore a response if the user selected another Run or refreshed.
            if (requestId !== cleanupPreviewRequestId) return;

            if (response.status === 404) {
                pendingCleanupPreviewMessage.textContent =
                    "This Run is unavailable. Refresh the pending-run list.";
                return;
            }

            if (!response.ok) {
                throw new Error(
                    `Unable to load Run preview (HTTP ${response.status}).`);
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
                rows));
    }

    // Refresh pending cleanup runs when clicked.
    refreshPendingCleanupBtn.addEventListener(
        "click",
        loadPendingCleanupRuns
    );

    cleanPendingRunBtn.addEventListener(
        "click",
        cleanSelectedPendingRun
    );
    // Load pending cleanup runs when the page opens.
    loadPendingCleanupRuns();

    // =====================================================
    // 10. Event Listeners and Initialization
    // =====================================================

    benchmarkMode.addEventListener("change", updateMode);

    operation.addEventListener(
        "change",
        updateOperationDescription
    );

    amlPayload.addEventListener("input", updateAmlStatus);

     // =====================================================
    //  Initialization
    // =====================================================

    // Initialize benchmark form and reset previous results.
    updateMode();
    updateOperationDescription();
    updateAmlStatus();
    clearResults();

    // Initialize authentication.
    // Database loading and session restoration are independent.
    loadDatabases();
    restoreSession();

});

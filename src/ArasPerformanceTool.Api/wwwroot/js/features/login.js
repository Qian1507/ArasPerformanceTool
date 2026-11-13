import { appState, byId } from "../core/state.js";
import { getCsrfToken } from "../core/api.js";

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

const executeBtn = byId("run-btn");
const cleanPendingRunBtn = byId("clean-pending-run");

let databasesLoading = false;
let loggingOut = false;

function setDatabasePlaceholder(text) {
    const option = document.createElement("option");

    option.value = "";
    option.textContent = text;
    option.selected = true;

    arasDatabase.replaceChildren(option);
}

export async function loadDatabases() {
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
        await getCsrfToken();
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

export function setAuthenticatedUI(authenticated) {
    appState.requiresLogin = !authenticated;

    loginSection.hidden = authenticated;
    dashboardSection.hidden = !authenticated;

    executeBtn.disabled = appState.submitting || appState.requiresLogin;
}

export function showAuthenticationExpired(revision) {
    // An old request must not override a newer login/logout.
    if (revision !== appState.authUiRevision) {
        return;
    }

    appState.requiresLogin = true;

    // Keep existing benchmark results visible.
    loginSection.hidden = false;
    dashboardSection.hidden = false;

    sessionInfo.textContent = "";

    loginStatus.textContent =
        "Aras authentication expired. Please sign in again.";

    loginStatus.classList.add("login-status-error");

    executeBtn.disabled = true;
    cleanPendingRunBtn.disabled = true;
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

        appState.authUiRevision++;
        setAuthenticatedUI(true);
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

        appState.authUiRevision++;
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

export async function restoreSession() {
    const revisionAtStart = appState.authUiRevision;

    try {
        const response = await fetch("/api/auth/session", {
            credentials: "same-origin",
            cache: "no-store"
        });

        if (!response.ok) {
            throw new Error("Unable to check login session.");
        }

        const session = await response.json();

        // Ignore an outdated response after a newer login/logout.
        if (revisionAtStart !== appState.authUiRevision) {
            return;
        }

        const authenticated = session.authenticated === true;

        sessionInfo.textContent = authenticated
            ? `${session.username} · ${session.database}`
            : "";

        setAuthenticatedUI(authenticated);

        if (!authenticated && session.requiresLogin === true) {
            loginStatus.textContent =
                "Aras authentication expired. Please sign in again.";

            loginStatus.classList.add("login-status-error");
        }
    } catch (error) {
        // An outdated request must not change the current UI.
        if (revisionAtStart !== appState.authUiRevision) {
            return;
        }

        console.error("Session check failed:", error);
        setAuthenticatedUI(false);
    }
}

export function initLoginFeature() {
    reloadDatabasesBtn.addEventListener("click", loadDatabases);
    loginForm.addEventListener("submit", loginToAras);
    logoutBtn.addEventListener("click", logoutFromAras);

    arasDatabase.addEventListener("change", () => {
        loginBtn.disabled =
            databasesLoading ||
            arasDatabase.disabled ||
            !arasDatabase.value;
    });

    // Database loading and session restoration are independent.
    loadDatabases();
    restoreSession();
}
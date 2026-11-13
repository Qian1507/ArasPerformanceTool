// Shared application state across feature modules
export const appState = {
    requiresLogin: true,
    authUiRevision: 0,
    submitting: false,
    currentJobId: null
};

// Shared DOM & formatting utilities
export const byId = id => document.getElementById(id);

export function numberText(value, decimals = 2) {
    if (value === null || value === undefined) {
        return "—";
    }

    const number = Number(value);

    return Number.isFinite(number)
        ? number.toFixed(decimals)
        : "—";
}

export function displayValue(value) {
    return value === null ||
        value === undefined ||
        value === ""
        ? "—"
        : String(value);
}

export function verificationText(value, success, failure) {
    if (value === true) return success;
    if (value === false) return failure;

    return "Not verified";
}

export const BENCHMARK_LIMITS = {
    minIterations: 1,
    maxIterations: 1000,
    minConcurrency: 1,
    maxConcurrency: 50
};
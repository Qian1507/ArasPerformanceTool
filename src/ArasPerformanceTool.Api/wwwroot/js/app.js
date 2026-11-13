import { initLoginFeature } from "./features/login.js";
import { initCustomAmlFeature } from "./features/custom_aml.js";
import { initReportsFeature } from "./features/reports.js";
import { initBenchmarkFeature } from "./features/benchmark.js";
import { clearResults } from "./features/benchmark.js";

import {
    initCleanupFeature,
    loadPendingCleanupRuns,
    resetCleanupState
} from "./features/cleanup.js";

document.addEventListener("DOMContentLoaded", () => {
    initCustomAmlFeature();
    initReportsFeature();
    initBenchmarkFeature();
    initCleanupFeature();

    // Register before login initialization, which restores the session.
    document.addEventListener("aras:auth-changed", event => {
        // Clear previous environment data and invalidate old responses.
        resetCleanupState();

        if (event.detail?.authenticated === true) {
            void loadPendingCleanupRuns();
        } else  if (event.detail?.reason !== "expired") {
            clearResults();
        }
    });

    initLoginFeature();
});
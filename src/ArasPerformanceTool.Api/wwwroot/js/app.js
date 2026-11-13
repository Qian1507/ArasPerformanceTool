import { initLoginFeature } from "./features/login.js";
import { initCustomAmlFeature } from "./features/custom_aml.js";
import { initReportsFeature } from "./features/reports.js";
import { initBenchmarkFeature } from "./features/benchmark.js";
import { initCleanupFeature } from "./features/cleanup.js";

document.addEventListener("DOMContentLoaded", () => {
    initCustomAmlFeature();
    initReportsFeature();
    initBenchmarkFeature();
    initCleanupFeature();
    initLoginFeature();
});
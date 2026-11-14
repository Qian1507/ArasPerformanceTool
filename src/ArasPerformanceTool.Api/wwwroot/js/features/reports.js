import { appState, byId } from "../core/state.js";

const downloadJsonBtn = byId("download-json-btn");
const downloadCsvBtn = byId("download-csv-btn");

export function syncReportButtons() {
    const disabled =
        !appState.currentJobId ||
        !appState.reportsAvailable;

    downloadJsonBtn.disabled = disabled;
    downloadCsvBtn.disabled = disabled;
}

function downloadReport(format) {
    if (!appState.currentJobId) {
        return;
    }

    const jobId = encodeURIComponent(appState.currentJobId);

    window.location.href = `/api/report/${jobId}/${format}`;
}

export function initReportsFeature() {
    downloadJsonBtn.addEventListener("click", () => {
        downloadReport("summary-pdf");
    });

    downloadCsvBtn.addEventListener("click", () => {
        downloadReport("measurements-pdf");
    });

    syncReportButtons();
}
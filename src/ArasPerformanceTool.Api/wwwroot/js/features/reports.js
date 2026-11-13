import { appState, byId } from "../core/state.js";

const downloadJsonBtn = byId("download-json-btn");
const downloadCsvBtn = byId("download-csv-btn");

export function syncReportButtons() {
    const disabled = !appState.currentJobId;
    downloadJsonBtn.disabled = disabled;
    downloadCsvBtn.disabled = disabled;
}

function downloadReport(format) {
    if (!appState.currentJobId) {
        return;
    }

    window.location.href =
        `/api/report/${encodeURIComponent(appState.currentJobId)}/${format}`;
}

export function initReportsFeature() {
    downloadJsonBtn.addEventListener("click", () => {
        downloadReport("summary-pdf");
    });

    downloadCsvBtn.addEventListener("click", () => {
        downloadReport("measurements-pdf");
    });
}
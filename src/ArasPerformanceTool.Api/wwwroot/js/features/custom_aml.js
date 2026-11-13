import { byId } from "../core/state.js";

const amlPayload = byId("amlPayload");
const parseStatus = byId("aml-parse-status");
const clearAmlBtn = byId("clearAmlBtn");

export function getAmlPayloadValue() {
    return amlPayload.value.trim();
}

export function inspectAml(raw) {
    if (!raw.trim()) {
        return {
            valid: false,
            empty: true,
            message: "Enter an AML payload."
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
            element => element.localName === "Item"
        );

    if (items.length !== 1) {
        return {
            valid: false,
            message: "Enter exactly one top-level <Item>."
        };
    }

    const itemType = items[0].getAttribute("type") || "unknown";
    const action = items[0].getAttribute("action") || "unknown";

    return {
        valid: true,
        message:
            `XML parsed: [${action}] on <${itemType}> ` +
            "(server validation required)"
    };
}

export function updateAmlStatus() {
    const result = inspectAml(amlPayload.value);

    parseStatus.textContent = result.message;

    parseStatus.className =
        result.valid || result.empty
            ? "parse-status"
            : "parse-status parse-error";

    return result;
}

export function renderAmlQueryResults(queryResult) {
    const panel = byId("aml-result-panel");
    const content = byId("aml-result-content");
    const count = byId("aml-result-count");
    const xmlPreview = byId("aml-response-xml");

    if (!panel || !content || !count || !xmlPreview) {
        return;
    }

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

    panel.classList.remove("hidden");
    xmlPreview.textContent = xml;

    if (
        documentXml.getElementsByTagName("parsererror").length > 0
    ) {
        content.textContent =
            "Unable to parse the AML response XML.";

        return;
    }

    const items = Array.from(
        documentXml.getElementsByTagName("*")
    ).filter(element => element.localName === "Item");

    count.textContent = `${items.length} Items`;

    if (items.length === 0) {
        content.textContent = "No items returned.";
        return;
    }

    const maxRows = 50;
    const maxColumns = 14;
    const visibleItems = items.slice(0, maxRows);

    // Read property values without inserting response XML as HTML.
    const rows = visibleItems.map(item => {
        const row = Object.create(null);

        row.ItemType = item.getAttribute("type") || "Unknown";
        row.ID = item.getAttribute("id") || "";

        for (const child of item.children) {
            if (child.localName === "Relationships") {
                continue;
            }

            if (child.localName.toLowerCase() === "id") {
                continue;
            }

            // Do not flatten nested Items into property text.
            const containsNestedItem = Array.from(
                child.getElementsByTagName("*")
            ).some(element => element.localName === "Item");

            if (containsNestedItem) {
                continue;
            }

            row[child.localName] =
                child.textContent?.trim() || "";
        }

        return row;
    });

    const propertyColumns = [
        ...new Set(
            rows.flatMap(row =>
                Object.keys(row).filter(
                    key => key !== "ItemType" && key !== "ID"
                )
            )
        )
    ];

    const columns = [
        "ItemType",
        "ID",
        ...propertyColumns
    ];

    const visibleColumns = columns.slice(0, maxColumns);

    const wrapper = document.createElement("div");
    wrapper.className = "aml-table-wrapper";

    const table = document.createElement("table");
    table.className = "aml-result-table";

    const thead = document.createElement("thead");
    const headerRow = document.createElement("tr");

    for (const column of visibleColumns) {
        const th = document.createElement("th");

        th.scope = "col";
        th.textContent = column;

        headerRow.appendChild(th);
    }

    thead.appendChild(headerRow);
    table.appendChild(thead);

    const tbody = document.createElement("tbody");

    for (const row of rows) {
        const tr = document.createElement("tr");

        for (const column of visibleColumns) {
            const td = document.createElement("td");

            td.textContent = row[column] ?? "";

            tr.appendChild(td);
        }

        tbody.appendChild(tr);
    }

    table.appendChild(tbody);
    wrapper.appendChild(table);
    content.appendChild(wrapper);

    if (items.length > maxRows) {
        const notice = document.createElement("p");

        notice.className = "aml-result-notice";
        notice.textContent =
            `Showing the first ${maxRows} of ` +
            `${items.length} returned Items.`;

        content.appendChild(notice);
    }

    if (columns.length > maxColumns) {
        const notice = document.createElement("p");

        notice.className = "aml-result-notice";
        notice.textContent =
            `Showing the first ${maxColumns} of ` +
            `${columns.length} columns. ` +
            "View the raw XML response for all properties.";

        content.appendChild(notice);
    }
}

export function initCustomAmlFeature() {
    clearAmlBtn?.addEventListener("click", () => {
        amlPayload.value = "";

        amlPayload.dispatchEvent(
            new Event("input", { bubbles: true })
        );

        amlPayload.focus();
    });

    amlPayload.addEventListener("input", updateAmlStatus);

    updateAmlStatus();
}
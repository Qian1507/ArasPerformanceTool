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

export function initCustomAmlFeature() {
    clearAmlBtn?.addEventListener("click", () => {
        if (!amlPayload) return;

        amlPayload.value = "";
        amlPayload.dispatchEvent(new Event("input", { bubbles: true }));
        amlPayload.focus();
    });

    amlPayload.addEventListener("input", updateAmlStatus);
    updateAmlStatus();
}
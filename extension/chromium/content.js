(() => {
    const host = location.hostname.toLowerCase();

    const domainMatches = (domain) => {
        const normalized = domain.trim().toLowerCase();
        return normalized.length === 0 || host === normalized || host.endsWith("." + normalized);
    };

    const apply = (selectors) => {
        if (selectors.length === 0) {
            return;
        }

        const style = document.createElement("style");
        style.id = "fylax-cosmetic";
        style.textContent = selectors.map((selector) => `${selector}{display:none!important;}`).join("\n");
        (document.head || document.documentElement).appendChild(style);
    };

    chrome.storage.local.get({ enabled: true, cosmetic: [] }, ({ enabled, cosmetic }) => {
        if (!enabled) {
            return;
        }

        const selectors = [];
        for (const rule of cosmetic) {
            if (rule.domains.length === 0 || rule.domains.some(domainMatches)) {
                selectors.push(rule.selector);
            }
        }

        apply([...new Set(selectors)]);
    });
})();

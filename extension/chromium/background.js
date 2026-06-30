const DEFAULT_LISTS = [];
const MAX_NETWORK_RULES = 30000;
const CHUNK = 1000;
const UPDATE_ALARM = "fylax-update";
const UPDATE_PERIOD = 720;

const TYPE_MAP = {
    script: "script",
    image: "image",
    stylesheet: "stylesheet",
    object: "object",
    "object-subrequest": "object",
    xmlhttprequest: "xmlhttprequest",
    ping: "ping",
    subdocument: "sub_frame",
    document: "main_frame",
    media: "media",
    font: "font",
    websocket: "websocket",
    other: "other"
};

const isAscii = (value) => /^[\u0000-\u007F]+$/.test(value);

const parseNetwork = (line) => {
    let allow = false;
    let body = line;

    if (body.startsWith("@@")) {
        allow = true;
        body = body.slice(2);
    }

    if (body.startsWith("/") && body.endsWith("/")) {
        return null;
    }

    let pattern = body;
    let options = "";
    const dollar = body.lastIndexOf("$");

    if (dollar > 0) {
        pattern = body.slice(0, dollar);
        options = body.slice(dollar + 1);
    }

    if (!pattern || pattern.includes(" ") || !isAscii(pattern) || pattern.length > 500) {
        return null;
    }

    const condition = { urlFilter: pattern };

    if (options) {
        const resourceTypes = [];
        const excludedResourceTypes = [];
        const initiatorDomains = [];
        const excludedInitiatorDomains = [];
        let domainType = null;

        for (let option of options.split(",")) {
            option = option.trim();
            if (!option) {
                continue;
            }

            let negated = false;
            if (option.startsWith("~")) {
                negated = true;
                option = option.slice(1);
            }

            if (option === "third-party" || option === "3p") {
                domainType = negated ? "firstParty" : "thirdParty";
                continue;
            }

            if (option === "first-party" || option === "1p") {
                domainType = negated ? "thirdParty" : "firstParty";
                continue;
            }

            if (option.startsWith("domain=")) {
                for (let domain of option.slice(7).split("|")) {
                    domain = domain.trim();
                    if (!domain) {
                        continue;
                    }
                    if (domain.startsWith("~")) {
                        excludedInitiatorDomains.push(domain.slice(1));
                    } else {
                        initiatorDomains.push(domain);
                    }
                }
                continue;
            }

            const mapped = TYPE_MAP[option];
            if (mapped) {
                if (negated) {
                    excludedResourceTypes.push(mapped);
                } else {
                    resourceTypes.push(mapped);
                }
                continue;
            }

            return null;
        }

        if (resourceTypes.length) {
            condition.resourceTypes = [...new Set(resourceTypes)];
        }
        if (excludedResourceTypes.length) {
            condition.excludedResourceTypes = [...new Set(excludedResourceTypes)];
        }
        if (domainType) {
            condition.domainType = domainType;
        }
        if (initiatorDomains.length) {
            condition.initiatorDomains = initiatorDomains;
        }
        if (excludedInitiatorDomains.length) {
            condition.excludedInitiatorDomains = excludedInitiatorDomains;
        }
    }

    return { allow, condition };
};

const parseList = (text) => {
    const network = [];
    const cosmetic = [];

    for (const rawLine of text.split(/\r?\n/)) {
        const line = rawLine.trim();

        if (!line || line.startsWith("!") || line.startsWith("[")) {
            continue;
        }

        if (line.includes("#@#") || line.includes("#?#") || line.includes("#$#")) {
            continue;
        }

        const cosmeticIndex = line.indexOf("##");
        if (cosmeticIndex !== -1) {
            const selector = line.slice(cosmeticIndex + 2).trim();
            if (!selector) {
                continue;
            }
            const domainPart = line.slice(0, cosmeticIndex);
            const domains = domainPart ? domainPart.split(",").map((item) => item.trim()).filter(Boolean) : [];
            cosmetic.push({ domains, selector });
            continue;
        }

        const rule = parseNetwork(line);
        if (rule) {
            network.push(rule);
        }
    }

    return { network, cosmetic };
};

const buildRules = (network, cap) => {
    const rules = [];
    let id = 1;

    for (const item of network) {
        if (item.allow && rules.length < cap) {
            rules.push({ id: id++, priority: 2, action: { type: "allow" }, condition: item.condition });
        }
    }

    for (const item of network) {
        if (!item.allow && rules.length < cap) {
            rules.push({ id: id++, priority: 1, action: { type: "block" }, condition: item.condition });
        }
    }

    return rules;
};

const applyDynamicRules = async (rules) => {
    const existing = await chrome.declarativeNetRequest.getDynamicRules();
    await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds: existing.map((rule) => rule.id) });

    for (let index = 0; index < rules.length; index += CHUNK) {
        const slice = rules.slice(index, index + CHUNK);
        try {
            await chrome.declarativeNetRequest.updateDynamicRules({ addRules: slice });
        } catch {
        }
    }
};

const refreshAll = async () => {
    const stored = await chrome.storage.local.get({ lists: DEFAULT_LISTS, enabled: true });
    const lists = stored.lists;
    const enabled = stored.enabled;

    let network = [];
    let cosmetic = [];
    const status = [];

    for (const list of lists) {
        if (!list.enabled) {
            status.push({ url: list.url, ok: true, skipped: true });
            continue;
        }

        try {
            const response = await fetch(list.url, { cache: "no-cache" });
            const text = await response.text();
            const parsed = parseList(text);
            network = network.concat(parsed.network);
            cosmetic = cosmetic.concat(parsed.cosmetic);
            status.push({ url: list.url, ok: true, network: parsed.network.length, cosmetic: parsed.cosmetic.length });
        } catch {
            status.push({ url: list.url, ok: false });
        }
    }

    const rules = enabled ? buildRules(network, MAX_NETWORK_RULES) : [];
    await applyDynamicRules(rules);

    await chrome.storage.local.set({
        cosmetic,
        stats: { network: rules.length, cosmetic: cosmetic.length, updatedAt: Date.now(), status }
    });

    return { network: rules.length, cosmetic: cosmetic.length };
};

chrome.runtime.onInstalled.addListener(async () => {
    const stored = await chrome.storage.local.get({ lists: null, enabled: null });
    const update = {};

    if (stored.lists === null) {
        update.lists = DEFAULT_LISTS;
    }
    if (stored.enabled === null) {
        update.enabled = true;
    }
    if (Object.keys(update).length) {
        await chrome.storage.local.set(update);
    }

    chrome.alarms.create(UPDATE_ALARM, { periodInMinutes: UPDATE_PERIOD });
    await refreshAll();
});

chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === UPDATE_ALARM) {
        refreshAll();
    }
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message && message.type === "refresh") {
        refreshAll().then((result) => sendResponse(result));
        return true;
    }
    return false;
});

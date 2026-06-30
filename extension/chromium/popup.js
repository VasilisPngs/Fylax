const masterEl = document.getElementById("master");
const netCountEl = document.getElementById("net-count");
const cosCountEl = document.getElementById("cos-count");
const updatedEl = document.getElementById("updated");
const listEl = document.getElementById("list");
const addUrlEl = document.getElementById("add-url");
const addBtn = document.getElementById("add-btn");
const updateBtn = document.getElementById("update-btn");

const getState = () => chrome.storage.local.get({ lists: [], enabled: true, stats: null });

const formatTime = (value) => {
    if (!value) {
        return "Never updated";
    }
    return "Updated " + new Date(value).toLocaleString();
};

const renderStats = (stats) => {
    netCountEl.textContent = stats ? stats.network : 0;
    cosCountEl.textContent = stats ? stats.cosmetic : 0;
    updatedEl.textContent = formatTime(stats ? stats.updatedAt : null);
};

const renderLists = (lists) => {
    listEl.textContent = "";

    lists.forEach((list, index) => {
        const item = document.createElement("div");
        item.className = list.enabled ? "list-item" : "list-item disabled";

        const url = document.createElement("span");
        url.className = "list-url";
        url.textContent = list.url;
        url.title = list.url;

        const toggle = document.createElement("button");
        toggle.className = "icon-btn";
        toggle.textContent = list.enabled ? "\u25CF" : "\u25CB";
        toggle.addEventListener("click", () => setListEnabled(index, !list.enabled));

        const remove = document.createElement("button");
        remove.className = "icon-btn";
        remove.textContent = "\u2715";
        remove.addEventListener("click", () => removeList(index));

        item.append(url, toggle, remove);
        listEl.append(item);
    });
};

const render = async () => {
    const state = await getState();
    masterEl.checked = state.enabled;
    renderStats(state.stats);
    renderLists(state.lists);
};

const refresh = async () => {
    updateBtn.disabled = true;
    updateBtn.textContent = "Updating...";
    await chrome.runtime.sendMessage({ type: "refresh" });
    updateBtn.disabled = false;
    updateBtn.textContent = "Update now";
    await render();
};

const setListEnabled = async (index, enabled) => {
    const state = await getState();
    if (!state.lists[index]) {
        return;
    }
    state.lists[index].enabled = enabled;
    await chrome.storage.local.set({ lists: state.lists });
    await refresh();
};

const removeList = async (index) => {
    const state = await getState();
    state.lists.splice(index, 1);
    await chrome.storage.local.set({ lists: state.lists });
    await refresh();
};

const addList = async () => {
    const value = addUrlEl.value.trim();
    if (!/^https?:\/\/.+/.test(value)) {
        return;
    }
    const state = await getState();
    if (state.lists.some((list) => list.url === value)) {
        addUrlEl.value = "";
        return;
    }
    state.lists.push({ url: value, enabled: true });
    await chrome.storage.local.set({ lists: state.lists });
    addUrlEl.value = "";
    await refresh();
};

masterEl.addEventListener("change", async () => {
    await chrome.storage.local.set({ enabled: masterEl.checked });
    await refresh();
});

addBtn.addEventListener("click", addList);
addUrlEl.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
        addList();
    }
});
updateBtn.addEventListener("click", refresh);

render();

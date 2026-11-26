(() => { document.querySelector("[data-act=\"peer-hour\"]").click(); return JSON.parse(window.AndroidBridge.peerState(localStorage.getItem("cloudssd_pc_id"))); })()

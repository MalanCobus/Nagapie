window.nagapie = (() => {
    let recognition, speechTimer, stopped = Promise.resolve();
    return {
        online: () => navigator.onLine,
        setLanguage: language => { document.documentElement.lang = language.startsWith("nl") ? "nl" : "en"; },
        pageTop: () => window.scrollTo({top:0, left:0, behavior:"instant"}),
        openDialog: element => { if (element && !element.open) { element.showModal(); (element.querySelector("[autofocus]") || element.querySelector("button"))?.focus(); } },
        closeDialog: element => { if (element?.open) element.close(); },
        download: (name, content, type) => {
            const url = URL.createObjectURL(new Blob([content], { type }));
            const a = document.createElement("a"); a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        },
        speechSupported: () => !!(window.SpeechRecognition || window.webkitSpeechRecognition),
        startSpeech: (language, reference) => {
            const Speech = window.SpeechRecognition || window.webkitSpeechRecognition;
            if (!Speech || recognition) return false;
            recognition = new Speech(); recognition.lang = language;
            recognition.continuous = true; recognition.interimResults = true;
            let lastInterim = "", transcriptQueue = Promise.resolve(), finish;
            stopped = new Promise(resolve => { finish = resolve; });
            const save = text => { transcriptQueue = transcriptQueue.then(() => reference.invokeMethodAsync("Transcript", text)).catch(() => {}); };
            recognition.onresult = event => {
                lastInterim = "";
                for (let i = event.resultIndex; i < event.results.length; i++) {
                    if (event.results[i].isFinal) save(event.results[i][0].transcript);
                    else lastInterim += event.results[i][0].transcript;
                }
            };
            recognition.onerror = () => { reference.invokeMethodAsync("SpeechEnded", true).catch(() => {}); };
            recognition.onend = async () => {
                clearTimeout(speechTimer);
                if (lastInterim.trim()) save(lastInterim);
                lastInterim = ""; await transcriptQueue;
                await reference.invokeMethodAsync("SpeechEnded", false).catch(() => {});
                recognition = null; finish();
            };
            try { recognition.start(); speechTimer = setTimeout(() => recognition?.stop(), 300000); return true; }
            catch { recognition = null; finish(); return false; }
        },
        stopSpeech: () => {
            clearTimeout(speechTimer);
            if (!recognition) return Promise.resolve();
            recognition.stop();
            // Browsers normally deliver onend promptly; never trap navigation if they do not.
            return Promise.race([stopped, new Promise(resolve => setTimeout(resolve, 2000))]);
        }
    };
})();

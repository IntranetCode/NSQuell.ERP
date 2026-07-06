(() => {
    const container = document.getElementById("playerContainer");
    const qualitySelect = document.getElementById("qualitySelect");
    const title = document.getElementById("selectedTitle");
    const description = document.getElementById("selectedDescription");
    const category = document.getElementById("selectedCategory");
    const source = document.getElementById("selectedSource");
    const duration = document.getElementById("selectedDuration");
    const message = document.getElementById("playerMessage");
    const catalog = Array.isArray(window.streamCatalog) ? window.streamCatalog : [];
    let hls = null;

    if (!container) {
        return;
    }

    function setMessage(text, isError = false) {
        if (!message) return;
        message.textContent = text;
        message.classList.toggle("error", isError);
    }

    function destroyHls() {
        if (hls) {
            hls.destroy();
            hls = null;
        }
    }

    function isM3u8(url) {
        return typeof url === "string" && /\.m3u8(\?|#|$)/i.test(url);
    }

    function canUseHlsJs() {
        return Boolean(window.Hls && window.Hls.isSupported());
    }

    function createVideoElement(posterUrl) {
        destroyHls();
        container.innerHTML = "";
        const video = document.createElement("video");
        video.id = "videoPlayer";
        video.controls = true;
        video.playsInline = true;
        video.preload = "metadata";
        if (posterUrl) {
            video.poster = posterUrl;
        }
        video.addEventListener("waiting", () => setMessage("Cargando transmisión..."));
        video.addEventListener("playing", () => setMessage("Reproduciendo."));
        video.addEventListener("error", () => {
            setMessage("No se pudo abrir este enlace. Puede haber vencido o no permitir reproducción fuera de su página original.", true);
        });
        container.appendChild(video);
        return video;
    }

    function createIframe(videoUrl) {
        destroyHls();
        container.innerHTML = "";
        const iframe = document.createElement("iframe");
        iframe.src = videoUrl || "about:blank";
        iframe.allow = "autoplay; fullscreen; picture-in-picture";
        iframe.allowFullscreen = true;
        iframe.referrerPolicy = "strict-origin-when-cross-origin";
        container.appendChild(iframe);
    }

    function tryAutoplay(videoElement) {
        videoElement.play().catch(() => {
            setMessage("Presiona reproducir para iniciar.");
        });
    }

    function getMainSource(video) {
        return video?.qualities?.[0] || null;
    }

    function normalizeQualityOptions(video) {
        const qualities = Array.isArray(video?.qualities) ? video.qualities : [];
        return qualities
            .filter((quality) => quality && quality.url)
            .map((quality, index) => ({
                value: String(index),
                label: quality.label || (index === 0 ? "Automática" : `Opción ${index + 1}`),
                url: quality.url
            }));
    }

    function setQualityOptions(options, selectedValue = "0", disabled = false) {
        if (!qualitySelect) return;

        qualitySelect.innerHTML = "";
        options.forEach((option) => {
            const element = document.createElement("option");
            element.value = option.value;
            element.textContent = option.label;
            qualitySelect.appendChild(element);
        });

        qualitySelect.value = selectedValue;
        qualitySelect.disabled = disabled || options.length <= 1;
    }

    function updateMeta(video) {
        if (title) title.textContent = video?.title || "Selecciona una transmisión";
        if (description) description.textContent = video?.description || "Contenido listo para ver.";
        if (category) category.textContent = video?.category || "En vivo";
        if (source) source.textContent = video?.sourceName || "Listo";
        if (duration) duration.textContent = video?.duration || "Disponible";
    }

    function readableHlsError(data) {
        const type = String(data?.type || "").toLowerCase();
        const details = String(data?.details || "").toLowerCase();

        if (type.includes("network") || details.includes("manifest")) {
            return "El enlace no respondió, venció o no permite abrirse desde esta página.";
        }

        if (type.includes("media")) {
            return "La transmisión respondió, pero el navegador no pudo reproducirla.";
        }

        return "No se pudo reproducir esta transmisión.";
    }

    function loadDirect(videoElement, url, playAfterLoad) {
        destroyHls();
        videoElement.src = url;
        videoElement.load();
        if (playAfterLoad) {
            videoElement.addEventListener("loadedmetadata", () => tryAutoplay(videoElement), { once: true });
        }
    }

    function loadM3u8(videoElement, url, playAfterLoad, allowBuiltInLevels = false) {
        destroyHls();

        if (canUseHlsJs()) {
            hls = new window.Hls({
                enableWorker: true,
                lowLatencyMode: true,
                backBufferLength: 90
            });

            hls.loadSource(url);
            hls.attachMedia(videoElement);

            hls.on(window.Hls.Events.MANIFEST_PARSED, () => {
                if (allowBuiltInLevels && hls.levels.length > 1) {
                    const levelOptions = [{ value: "-1", label: "Automática" }];
                    hls.levels.forEach((level, index) => {
                        const label = level.height ? `${level.height}p` : `Opción ${index + 1}`;
                        levelOptions.push({ value: String(index), label });
                    });
                    setQualityOptions(levelOptions, "-1", false);
                    if (qualitySelect) {
                        qualitySelect.onchange = () => {
                            hls.currentLevel = Number(qualitySelect.value);
                            const selectedText = qualitySelect.options[qualitySelect.selectedIndex]?.textContent || "Automática";
                            setMessage(selectedText === "Automática" ? "Calidad automática activada." : `Calidad ${selectedText} activada.`);
                        };
                    }
                }

                setMessage("Transmisión lista.");
                if (playAfterLoad) {
                    tryAutoplay(videoElement);
                }
            });

            hls.on(window.Hls.Events.ERROR, (_event, data) => {
                if (!data?.fatal) {
                    return;
                }

                if (data.type === window.Hls.ErrorTypes.MEDIA_ERROR) {
                    hls.recoverMediaError();
                    return;
                }

                setMessage(readableHlsError(data), true);
            });

            return;
        }

        if (videoElement.canPlayType("application/vnd.apple.mpegurl")) {
            videoElement.src = url;
            videoElement.load();
            videoElement.addEventListener("loadedmetadata", () => {
                setMessage("Transmisión lista.");
                if (playAfterLoad) {
                    tryAutoplay(videoElement);
                }
            }, { once: true });
            return;
        }

        setMessage("Este navegador no puede reproducir transmisiones en vivo. Prueba con Chrome, Edge, Safari o Firefox actualizado.", true);
    }

    function loadAnyVideoSource(videoElement, url, playAfterLoad = true, allowBuiltInLevels = false) {
        if (!url) {
            setMessage("No hay enlace para reproducir.", true);
            return;
        }

        if (isM3u8(url)) {
            loadM3u8(videoElement, url, playAfterLoad, allowBuiltInLevels);
            return;
        }

        loadDirect(videoElement, url, playAfterLoad);
    }

    function setupManualQuality(videoElement, video) {
        const options = normalizeQualityOptions(video);
        setQualityOptions(options, "0", options.length <= 1);

        if (options[0]?.url) {
            loadAnyVideoSource(videoElement, options[0].url, true, false);
        }

        if (!qualitySelect) return;
        qualitySelect.onchange = () => {
            const option = options[Number(qualitySelect.value) || 0];
            if (!option?.url) return;
            loadAnyVideoSource(videoElement, option.url, true, false);
            setMessage(`Calidad ${option.label.toLowerCase()} activada.`);
        };
    }

    function setupPrimaryM3u8(videoElement, video) {
        const options = normalizeQualityOptions(video);
        const main = options[0];

        if (!main?.url) {
            setMessage("No hay enlace para reproducir.", true);
            return;
        }

        const hasManualAlternatives = options.length > 1;
        setQualityOptions(hasManualAlternatives ? options : [{ value: "0", label: "Automática" }], "0", !hasManualAlternatives);
        loadM3u8(videoElement, main.url, true, !hasManualAlternatives);

        if (hasManualAlternatives && qualitySelect) {
            qualitySelect.onchange = () => {
                const option = options[Number(qualitySelect.value) || 0];
                if (!option?.url) return;
                loadAnyVideoSource(videoElement, option.url, true, false);
                setMessage(`Calidad ${option.label.toLowerCase()} activada.`);
            };
        }
    }

    function playVideo(video) {
        if (!video) return;
        updateMeta(video);
        setMessage("Listo para reproducir. Elige una calidad si está disponible.");

        document.querySelectorAll("[data-video-id]").forEach((card) => {
            card.classList.toggle("selected", card.dataset.videoId === String(video.id));
        });

        const selectedType = (video.type || "").toLowerCase();
        const main = getMainSource(video);

        if (selectedType === "iframe") {
            createIframe(main?.url);
            setQualityOptions([{ value: "0", label: "Original" }], "0", true);
            setMessage("Contenido abierto en su reproductor original.");
            return;
        }

        const videoElement = createVideoElement(video.posterUrl);

        if (selectedType === "hls" || isM3u8(main?.url)) {
            setupPrimaryM3u8(videoElement, video);
            return;
        }

        setupManualQuality(videoElement, video);
    }

    document.querySelectorAll("[data-video-id]").forEach((item) => {
        item.addEventListener("click", (event) => {
            event.preventDefault();
            const video = catalog.find((entry) => String(entry.id) === item.dataset.videoId);
            if (!video) return;
            playVideo(video);
            window.history.pushState({}, "", item.href);
        });
    });

    playVideo(window.initialVideo || catalog[0]);
})();

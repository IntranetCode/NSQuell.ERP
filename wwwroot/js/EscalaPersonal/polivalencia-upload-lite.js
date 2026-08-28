// NSQ_POLIVALENCIA_UPLOAD_LITE_V8
(() => {
    "use strict";

    const form = document.getElementById("polivalenciaUploadForm");
    if (!form) return;

    const input = document.getElementById("polivalenciaArchivo");
    const button = document.getElementById("polivalenciaSubmit");
    const status = document.getElementById("polivalenciaUploadStatus");
    const hashInput = document.getElementById("polivalenciaHashOriginal");

    const MIME_XLSX = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    const MAX_ORIGINAL_BYTES = 60_000_000; // Sólo se lee localmente; nunca llega así al servidor.
    const MAX_LITE_BYTES = 6_000_000;      // Límite funcional del archivo que sí se envía.

    let procesando = false;

    function formatoBytes(bytes) {
        if (!Number.isFinite(bytes) || bytes < 0) return "-";
        if (bytes < 1024) return `${bytes} B`;
        const kb = bytes / 1024;
        if (kb < 1024) return `${kb.toFixed(1)} KB`;
        return `${(kb / 1024).toFixed(2)} MB`;
    }

    function estado(texto, tipo = "normal") {
        if (!status) return;
        status.textContent = texto;
        status.classList.remove("text-danger", "text-success", "text-primary", "text-muted");
        if (tipo === "error") status.classList.add("text-danger");
        else if (tipo === "ok") status.classList.add("text-success");
        else if (tipo === "working") status.classList.add("text-primary");
        else status.classList.add("text-muted");
    }

    function textoCelda(ws, direccion) {
        const celda = ws?.[direccion];
        if (!celda) return "";
        if (celda.w !== undefined && celda.w !== null && String(celda.w).trim() !== "") {
            return String(celda.w).trim();
        }
        if (celda.v !== undefined && celda.v !== null) {
            return String(celda.v).trim();
        }
        return "";
    }

    function contieneCodigoEnPrimerasCeldas(ws, codigo) {
        if (!ws || !ws["!ref"]) return false;
        const rango = XLSX.utils.decode_range(ws["!ref"]);
        let usadas = 0;

        for (let r = rango.s.r; r <= rango.e.r && usadas < 30; r++) {
            for (let c = rango.s.c; c <= rango.e.c && usadas < 30; c++) {
                const direccion = XLSX.utils.encode_cell({ r, c });
                const celda = ws[direccion];
                if (!celda) continue;
                usadas++;
                if (textoCelda(ws, direccion).toUpperCase().includes(codigo)) return true;
            }
        }
        return false;
    }

    function buscarHojaOficial(workbook) {
        const codigo = "GQ-F-RH02-05";
        const vigentes = workbook.SheetNames.filter(
            nombre => !nombre.toUpperCase().includes("OBSOLETA")
        );

        for (const nombre of vigentes) {
            const ws = workbook.Sheets[nombre];
            if (textoCelda(ws, "D2").toUpperCase() === codigo) return nombre;
        }

        for (const nombre of vigentes) {
            if (contieneCodigoEnPrimerasCeldas(workbook.Sheets[nombre], codigo)) return nombre;
        }

        return null;
    }

    function bufferAHex(buffer) {
        return Array.from(new Uint8Array(buffer))
            .map(b => b.toString(16).padStart(2, "0"))
            .join("")
            .toUpperCase();
    }

    async function sha256Original(arrayBuffer) {
        if (!window.crypto?.subtle) return "";
        const digest = await window.crypto.subtle.digest("SHA-256", arrayBuffer);
        return bufferAHex(digest);
    }

    function siguientePintado() {
        return new Promise(resolve => requestAnimationFrame(() => resolve()));
    }

    input?.addEventListener("change", () => {
        const archivo = input.files?.[0];
        hashInput.value = "";
        if (!archivo) {
            estado("Selecciona la matriz oficial .xlsx.");
            return;
        }
        estado(
            `Seleccionado: ${archivo.name} (${formatoBytes(archivo.size)}). ` +
            "Al enviar, el navegador quitará imágenes antes de subirlo."
        );
    });

    form.addEventListener("submit", async event => {
        event.preventDefault();
        if (procesando) return;

        const archivoOriginal = input?.files?.[0];
        if (!archivoOriginal) {
            estado("Selecciona el archivo XLSX de la matriz.", "error");
            return;
        }

        if (!archivoOriginal.name.toLowerCase().endsWith(".xlsx")) {
            estado("La matriz debe ser un archivo .xlsx.", "error");
            return;
        }

        if (archivoOriginal.size > MAX_ORIGINAL_BYTES) {
            estado(
                `El archivo original pesa ${formatoBytes(archivoOriginal.size)}. ` +
                "Por seguridad del navegador, el máximo local permitido es 60 MB.",
                "error"
            );
            return;
        }

        if (!window.XLSX) {
            estado(
                "No se cargó el componente local de procesamiento XLSX. No se enviará el archivo original al servidor. Recarga la página e intenta nuevamente.",
                "error"
            );
            return;
        }

        try {
            procesando = true;
            button.disabled = true;
            input.style.pointerEvents = "none";
            input.setAttribute("aria-busy", "true");
            estado("Preparando copia ligera de la matriz en el navegador...", "working");
            await siguientePintado();

            // 1. El archivo grande se lee solamente en memoria del navegador.
            const originalBuffer = await archivoOriginal.arrayBuffer();

            // El hash conserva la deduplicación que ya tenía el backend, aunque
            // el XLSX que se envíe sea una reconstrucción ligera.
            const hashPromise = sha256Original(originalBuffer);

            // 2. SheetJS interpreta celdas/fórmulas/merges, pero no reescribe
            // imágenes ni shapes al generar un nuevo XLSX.
            const workbook = XLSX.read(originalBuffer, {
                type: "array",
                cellDates: false,
                cellNF: true,
                cellText: true,
                cellStyles: false,
                bookVBA: false,
                dense: false
            });

            const hojaOficial = buscarHojaOficial(workbook);
            if (!hojaOficial) {
                throw new Error("No se encontró la hoja vigente GQ-F-RH02-05 en el archivo seleccionado.");
            }

            estado(
                `Matriz validada (${hojaOficial}). Quitando imágenes y compactando el XLSX...`,
                "working"
            );
            await siguientePintado();

            // NSQ_POLIVALENCIA_UPLOAD_LITE_V82_X000D_START
            // Normaliza retornos de carro y escapes OOXML que SheetJS puede
            // conservar como texto al reconstruir el XLSX.
            let celdasNormalizadasV82 = 0;

            for (const nombreHojaV82 of workbook.SheetNames) {
                const wsV82 = workbook.Sheets[nombreHojaV82];
                if (!wsV82 || !wsV82["!ref"]) continue;

                const rangoV82 = XLSX.utils.decode_range(wsV82["!ref"]);

                for (let rV82 = rangoV82.s.r; rV82 <= rangoV82.e.r; rV82++) {
                    for (let cV82 = rangoV82.s.c; cV82 <= rangoV82.e.c; cV82++) {
                        const direccionV82 = XLSX.utils.encode_cell({ r: rV82, c: cV82 });
                        const celdaV82 = wsV82[direccionV82];

                        if (!celdaV82 || typeof celdaV82.v !== "string") continue;

                        const valorAnteriorV82 = celdaV82.v;
                        const valorNormalizadoV82 = valorAnteriorV82
                            .replace(/_x000d_/gi, "\n")
                            .replace(/_x000a_/gi, "\n")
                            .replace(/\r\n/g, "\n")
                            .replace(/\r/g, "\n");

                        if (valorNormalizadoV82 !== valorAnteriorV82) {
                            celdaV82.v = valorNormalizadoV82;

                            if (Object.prototype.hasOwnProperty.call(celdaV82, "w")) {
                                delete celdaV82.w;
                            }

                            celdasNormalizadasV82++;
                        }
                    }
                }
            }

            console.info(
                `NSQ Polivalencia V8.2: ${celdasNormalizadasV82} celda(s) con saltos normalizados.`
            );
            // NSQ_POLIVALENCIA_UPLOAD_LITE_V82_X000D_END
            const salida = XLSX.write(workbook, {
                bookType: "xlsx",
                type: "array",
                compression: true,
                bookSST: false,
                cellStyles: false
            });

            const blobLigero = new Blob([salida], { type: MIME_XLSX });
            if (blobLigero.size <= 0) {
                throw new Error("La copia ligera quedó vacía.");
            }
            if (blobLigero.size > MAX_LITE_BYTES) {
                throw new Error(
                    `La copia sin imágenes todavía pesa ${formatoBytes(blobLigero.size)}; ` +
                    "se canceló el envío porque supera el límite de 6 MB."
                );
            }

            // 3. Validación final del archivo reconstruido antes de tocar el input.
            const bufferLigero = await blobLigero.arrayBuffer();
            const workbookVerificacion = XLSX.read(bufferLigero, {
                type: "array",
                cellDates: false,
                cellNF: true,
                cellText: true,
                cellStyles: false,
                bookVBA: false,
                dense: false
            });

            if (!buscarHojaOficial(workbookVerificacion)) {
                throw new Error("La copia ligera no conservó la hoja oficial requerida; no se enviará nada.");
            }

            // NSQ_POLIVALENCIA_UPLOAD_LITE_V82_VERIFY_START
            // Revisa la COPIA YA ESCRITA. Si un escape _x000d_ quedara como texto,
            // no se permite enviarlo al backend.
            for (const nombreHojaCheckV82 of workbookVerificacion.SheetNames) {
                const wsCheckV82 = workbookVerificacion.Sheets[nombreHojaCheckV82];
                if (!wsCheckV82 || !wsCheckV82["!ref"]) continue;

                const rangoCheckV82 = XLSX.utils.decode_range(wsCheckV82["!ref"]);

                for (let rCheckV82 = rangoCheckV82.s.r; rCheckV82 <= rangoCheckV82.e.r; rCheckV82++) {
                    for (let cCheckV82 = rangoCheckV82.s.c; cCheckV82 <= rangoCheckV82.e.c; cCheckV82++) {
                        const direccionCheckV82 = XLSX.utils.encode_cell({
                            r: rCheckV82,
                            c: cCheckV82
                        });

                        const celdaCheckV82 = wsCheckV82[direccionCheckV82];
                        if (!celdaCheckV82 || typeof celdaCheckV82.v !== "string") continue;

                        if (/_x000d_|_x000a_/i.test(celdaCheckV82.v)) {
                            throw new Error(
                                `La copia ligera conservo un escape Excel en ` +
                                `${nombreHojaCheckV82}!${direccionCheckV82}. ` +
                                "No se envio el archivo."
                            );
                        }
                    }
                }
            }
            // NSQ_POLIVALENCIA_UPLOAD_LITE_V82_VERIFY_END
            const hashOriginal = await hashPromise;
            hashInput.value = hashOriginal;

            const archivoLigero = new File(
                [blobLigero],
                archivoOriginal.name,
                {
                    type: MIME_XLSX,
                    lastModified: archivoOriginal.lastModified || Date.now()
                }
            );

            // 4. Sustituye el archivo del formulario. El original nunca se sube.
            const transferencia = new DataTransfer();
            transferencia.items.add(archivoLigero);
            input.files = transferencia.files;

            if (!input.files?.length || input.files[0].size !== archivoLigero.size) {
                throw new Error("El navegador no permitió sustituir el archivo por la copia ligera.");
            }

            estado(
                `Listo: ${formatoBytes(archivoOriginal.size)} → ${formatoBytes(archivoLigero.size)}. ` +
                "Enviando únicamente la copia sin imágenes...",
                "ok"
            );
            await siguientePintado();

            input.style.pointerEvents = "";
            input.removeAttribute("aria-busy");

            // Submit nativo: conserva antiforgery + redirecciones/TempData y no
            // vuelve a ejecutar este listener.
            HTMLFormElement.prototype.submit.call(form);
        }
        catch (error) {
            console.error("NSQ Polivalencia Upload Lite:", error);
            procesando = false;
            button.disabled = false;
            input.style.pointerEvents = "";
            input.removeAttribute("aria-busy");
            hashInput.value = "";
            estado(
                error?.message || "No fue posible preparar la copia ligera de la matriz. No se envió el archivo original.",
                "error"
            );
        }
    });
})();

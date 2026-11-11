window.miGestor = {
    _ayuda: null,
    _espacio: null,
    _focoPrevio: null,

    prepararArbol(arbol) {
        const teclas = ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "Enter", "ContextMenu"];
        arbol.addEventListener("keydown", e => {
            if (teclas.includes(e.key) || (e.key === "F10" && e.shiftKey)) {
                e.preventDefault();
            }
        });
        arbol.addEventListener("contextmenu", e => e.preventDefault());
    },

    prepararMenu(menu) {
        const teclas = ["ArrowUp", "ArrowDown", "Home", "End", "Enter", " ", "Escape", "Tab"];
        menu.addEventListener("keydown", e => {
            if (teclas.includes(e.key)) {
                e.preventDefault();
            }
        });
        menu.addEventListener("contextmenu", e => e.preventDefault());
    },

    prepararDialogo(dialogo) {
        const enfocables = () => [...dialogo.querySelectorAll("button, input, select, textarea, a[href], [tabindex='0']")]
            .filter(el => !el.disabled && el.offsetParent !== null);

        dialogo.addEventListener("keydown", e => {
            if (e.key !== "Tab") {
                return;
            }

            const lista = enfocables();
            if (lista.length === 0) {
                return;
            }

            const primero = lista[0];
            const ultimo = lista[lista.length - 1];

            if (e.shiftKey && document.activeElement === primero) {
                e.preventDefault();
                ultimo.focus();
            } else if (!e.shiftKey && document.activeElement === ultimo) {
                e.preventDefault();
                primero.focus();
            }
        });

        const inicial = dialogo.querySelector("[data-foco-inicial]") || enfocables()[0];
        if (inicial) {
            inicial.focus();
        }
    },

    enfocarInicio(id) {
        const elemento = document.getElementById(id);
        if (elemento) {
            elemento.focus();
            elemento.setSelectionRange(0, 0);
            elemento.scrollTop = 0;
        }
    },

    enfocar(id) {
        const elemento = document.getElementById(id);
        if (elemento) {
            elemento.focus();
            elemento.scrollIntoView({ block: "nearest" });
        }
    },

    registrarAyuda(referencia) {
        this._ayuda = referencia;
    },

    registrarBarra(referencia, barra) {
        this._barra = referencia;
        this._elementoBarra = barra;
        const teclas = ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "Enter", " ", "Escape", "Tab"];

        barra.addEventListener("keydown", e => {
            if (teclas.includes(e.key)) {
                e.preventDefault();
            }
        });

        barra.addEventListener("focusout", e => {
            if (e.relatedTarget && !barra.contains(e.relatedTarget)) {
                referencia.invokeMethodAsync("Desactivar");
            }
        });
    },

    activarBarra() {
        if (!this._barra || this.dialogoAbierto()) {
            return;
        }

        if (!this._elementoBarra.contains(document.activeElement)) {
            this.recordarFocoMenu();
        }

        this._barra.invokeMethodAsync("Activar");
    },

    recordarFocoMenu() {
        const activo = document.activeElement;
        this._focoMenu = activo && activo !== document.body && !this._elementoBarra.contains(activo) ? activo : null;
    },

    volverFocoMenu() {
        const elemento = this._focoMenu;
        this._focoMenu = null;

        if (elemento && document.body.contains(elemento)) {
            elemento.focus();
            return;
        }

        const destino = this.nodoActivo() || document.getElementById("editor-sql") || document.querySelector("main button, main [tabindex='0']");
        if (destino) {
            destino.focus();
        }
    },

    estadoMenu() {
        const cancelar = [...document.querySelectorAll("button")].find(b => b.innerText.trim() === "Cancelar ejecución");
        return {
            conectado: !!this._espacio,
            pestana: !!document.getElementById("editor-sql"),
            ejecutando: !!cancelar && !cancelar.disabled
        };
    },

    ejecutarMenu(accion) {
        this.volverFocoMenu();

        switch (accion) {
            case "nuevaConsulta": this.nuevaConsulta(); break;
            case "nuevaPestana": this.nuevaPestana(); break;
            case "abrirArchivo": this.abrirArchivo(); break;
            case "guardar": this.guardarArchivo(false); break;
            case "guardarComo": this.guardarArchivo(true); break;
            case "cerrarPestana": this.cerrarPestana(); break;
            case "desconectar": document.getElementById("boton-desconectar")?.click(); break;
            case "salir": this._ayuda?.invokeMethodAsync("Salir"); break;
            case "buscar": this.abrirBusqueda(); break;
            case "buscarSiguiente": this.buscarSiguiente(false); break;
            case "buscarObjeto": this.buscarObjeto(); break;
            case "historial": this.abrirHistorial(); break;
            case "ejecutar": this.ejecutarConsulta(); break;
            case "cancelar": [...document.querySelectorAll("button")].find(b => b.innerText.trim() === "Cancelar ejecución")?.click(); break;
            case "ayuda": this.abrirAyuda(); break;
            case "atajos": this.abrirAyudaCon("atajos:aplicacion"); break;
            case "manual": this.abrirAyudaCon("manual"); break;
            case "actualizaciones": this.abrirActualizaciones(); break;
            case "acercaDe": this.abrirAyudaCon("acerca-de"); break;
        }
    },

    abrirActualizaciones() {
        if (!this._ayuda || this.dialogoAbierto()) {
            return;
        }

        const activo = document.activeElement;
        this._focoPrevio = activo && activo !== document.body ? activo : null;
        this._ayuda.invokeMethodAsync("AbrirActualizaciones");
    },

    enfocarSeccion(id) {
        const seccion = document.getElementById(id);
        if (seccion) {
            seccion.setAttribute("tabindex", "-1");
            seccion.focus();
            seccion.scrollIntoView({ block: "start" });
        }
    },

    abrirAyudaCon(temas) {
        if (!this._ayuda || this.dialogoAbierto()) {
            return;
        }

        const activo = document.activeElement;
        this._focoPrevio = activo && activo !== document.body ? activo : null;
        this._ayuda.invokeMethodAsync("AbrirAyuda", temas);
    },

    sugerencias: {
        lista: [],
        indice: -1,
        inicio: 0,
        pedido: 0,
        alEscribir: true,
        descartada: false,
        temporizador: 0,
        insertando: false,
        anunciada: "",

        palabra(c) {
            return /[\p{L}\p{N}_$#@]/u.test(c);
        },

        programar(ms) {
            clearTimeout(this.temporizador);
            this.temporizador = setTimeout(() => {
                this.temporizador = 0;
                this.pedir(false);
            }, ms);
        },

        alCambiar(e) {
            if (this.insertando) {
                return;
            }

            const editor = e.target;
            const dato = e.inputType === "insertText" ? (e.data ?? "") : null;

            if (dato && ![...dato].every(c => this.palabra(c))) {
                this.descartada = false;
                this.cerrar();
                if (this.alEscribir && dato.endsWith(".")
                    && /[\p{L}\p{N}_$#"\]`]/u.test(editor.value.charAt(editor.selectionStart - 2))) {
                    this.programar(0);
                }
                return;
            }

            if (this.abierta()) {
                if (this.filtrar(editor)) {
                    this.programar(300);
                }
                return;
            }

            if (!dato || !this.alEscribir || this.descartada) {
                return;
            }

            let inicio = editor.selectionStart;
            while (inicio > 0 && this.palabra(editor.value.charAt(inicio - 1))) {
                inicio--;
            }

            if (!/\p{N}/u.test(editor.value.charAt(inicio))) {
                this.programar(300);
            }
        },

        filtrar(editor) {
            const cursor = editor.selectionStart;
            const prefijo = editor.value.slice(this.inicio, cursor).toLowerCase();
            if (cursor < this.inicio || ![...prefijo].every(c => this.palabra(c))) {
                this.cerrar();
                return false;
            }

            const anterior = this.activa();
            const coincide = s => s.texto.toLowerCase().startsWith(prefijo)
                || s.texto.slice(s.texto.lastIndexOf(".") + 1).toLowerCase().startsWith(prefijo);
            const lista = this.lista.filter(coincide);
            if (lista.length === 0) {
                this.cerrar();
                return false;
            }

            this.lista = lista;
            const mismo = anterior ? lista.findIndex(s => s.texto === anterior.texto) : -1;
            this.dibujar(mismo >= 0 ? mismo : 0);
            return true;
        },

        abierta() {
            const ul = document.getElementById("sugerencias-editor");
            return !!ul && !ul.hidden && this.lista.length > 0;
        },

        activa() {
            return this.abierta() ? this.lista[this.indice] : null;
        },

        async pedir(anunciarVacio) {
            const editor = document.getElementById("editor-sql");
            if (!editor || !miGestor._espacio) {
                return;
            }

            const numero = ++this.pedido;
            const resultado = await miGestor._espacio.invokeMethodAsync("Sugerencias", editor.value, editor.selectionStart);
            if (numero !== this.pedido || document.activeElement !== editor) {
                return;
            }

            if (!resultado || resultado.sugerencias.length === 0) {
                this.cerrar();
                if (anunciarVacio) {
                    this.estado("No hay sugerencias.");
                }
                return;
            }

            const anterior = this.activa();
            this.lista = resultado.sugerencias;
            this.inicio = resultado.inicio;
            const mismo = anterior ? this.lista.findIndex(s => s.texto === anterior.texto) : -1;
            this.dibujar(mismo >= 0 ? mismo : 0);
        },

        dibujar(indice) {
            const editor = document.getElementById("editor-sql");
            const ul = document.getElementById("sugerencias-editor");
            ul.replaceChildren(...this.lista.map((s, i) => {
                const li = document.createElement("li");
                li.id = `sugerencia-${i}`;
                li.setAttribute("role", "menuitem");
                li.setAttribute("aria-posinset", i + 1);
                li.setAttribute("aria-setsize", this.lista.length);
                li.textContent = `${s.texto}, ${s.tipo}`;
                return li;
            }));
            ul.hidden = false;
            editor.setAttribute("aria-controls", "sugerencias-editor");
            this.mover(indice, true);
        },

        mover(indice, forzar) {
            const editor = document.getElementById("editor-sql");
            const total = this.lista.length;
            const nuevo = ((indice % total) + total) % total;
            if (nuevo === this.indice && !forzar) {
                return;
            }

            const ul = document.getElementById("sugerencias-editor");
            ul.querySelectorAll(".activa").forEach(li => li.classList.remove("activa"));
            this.indice = nuevo;
            const li = document.getElementById(`sugerencia-${nuevo}`);
            li.classList.add("activa");
            li.scrollIntoView({ block: "nearest" });
            if (editor.getAttribute("aria-activedescendant") === li.id && this.anunciada === li.textContent) {
                return;
            }
            this.anunciada = li.textContent;
            editor.removeAttribute("aria-activedescendant");
            editor.setAttribute("aria-activedescendant", li.id);
        },

        aceptar() {
            const editor = document.getElementById("editor-sql");
            const sugerencia = this.activa();
            if (!editor || !sugerencia) {
                return;
            }

            const fin = editor.selectionStart;
            const escrito = editor.value.slice(this.inicio, fin);
            const minusculas = sugerencia.ajustarMayusculas && /\p{L}/u.test(escrito) && escrito === escrito.toLowerCase();
            this.cerrar();
            editor.setSelectionRange(this.inicio, fin);
            this.insertando = true;
            try {
                document.execCommand("insertText", false, minusculas ? sugerencia.texto.toLowerCase() : sugerencia.texto);
            } finally {
                this.insertando = false;
            }
        },

        cerrar() {
            clearTimeout(this.temporizador);
            this.temporizador = 0;
            this.pedido++;
            this.lista = [];
            this.indice = -1;
            this.anunciada = "";
            const ul = document.getElementById("sugerencias-editor");
            if (ul) {
                ul.hidden = true;
                ul.replaceChildren();
            }
            const editor = document.getElementById("editor-sql");
            if (editor) {
                editor.removeAttribute("aria-activedescendant");
                editor.removeAttribute("aria-controls");
            }
        },

        estado(texto) {
            const region = document.getElementById("estado-sugerencias");
            if (region) {
                region.textContent = "";
                setTimeout(() => { region.textContent = texto; }, 50);
            }
        },

        tecla(e) {
            if (e.ctrlKey && !e.altKey && !e.shiftKey && e.code === "Space") {
                e.preventDefault();
                this.descartada = false;
                if (!this.abierta()) {
                    clearTimeout(this.temporizador);
                    this.pedir(true);
                }
                return true;
            }

            if (!this.abierta()) {
                if (["Enter", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown"].includes(e.key)) {
                    this.descartada = false;
                    clearTimeout(this.temporizador);
                }
                return false;
            }

            switch (e.key) {
                case "ArrowDown":
                    this.mover(this.indice + 1);
                    break;
                case "ArrowUp":
                    this.mover(this.indice - 1);
                    break;
                case "PageDown":
                    this.mover(Math.min(this.indice + 8, this.lista.length - 1));
                    break;
                case "PageUp":
                    this.mover(Math.max(this.indice - 8, 0));
                    break;
                case "Tab":
                    if (e.shiftKey || e.ctrlKey) {
                        this.cerrar();
                        return false;
                    }
                    this.aceptar();
                    break;
                case "Escape":
                    this.cerrar();
                    this.descartada = true;
                    break;
                case "Enter":
                case "ArrowLeft":
                case "ArrowRight":
                case "Home":
                case "End":
                    this.cerrar();
                    return false;
                default:
                    return false;
            }

            e.preventDefault();
            e.stopPropagation();
            return true;
        }
    },

    configurarSugerencias(valor) {
        this.sugerencias.alEscribir = valor;
        if (!valor) {
            this.sugerencias.cerrar();
        }
    },

    registrarEspacio(referencia) {
        this._espacio = referencia;
    },

    dialogoAbierto() {
        return document.querySelector("[data-dialogo-ayuda], [data-dialogo]") !== null;
    },

    nodoActivo() {
        return document.querySelector("[role=tree] [role=treeitem][tabindex='0']");
    },

    abrirAyuda() {
        if (!this._ayuda || this.dialogoAbierto()) {
            return;
        }

        const activo = document.activeElement;
        this._focoPrevio = activo && activo !== document.body ? activo : null;

        let temas = "";
        const sugerencia = this.sugerencias.activa();

        if (sugerencia) {
            temas = sugerencia.ayuda;
            this.sugerencias.cerrar();
        } else if (activo && activo.id === "editor-sql") {
            temas = this.temasEnCursor(activo);
        } else {
            const contexto = activo && activo.closest ? activo.closest("[data-ayuda]") : null;
            temas = contexto ? contexto.dataset.ayuda : "";
        }

        this._ayuda.invokeMethodAsync("AbrirAyuda", temas);
    },

    temasEnCursor(area) {
        const texto = area.value;
        const posicion = area.selectionStart;
        const esLetra = c => c !== undefined && /[A-Za-z0-9_]/.test(c);

        let inicio = posicion;
        let fin = posicion;
        while (inicio > 0 && esLetra(texto[inicio - 1])) inicio--;
        while (fin < texto.length && esLetra(texto[fin])) fin++;

        const palabra = texto.slice(inicio, fin).toLowerCase();
        if (!palabra) {
            return "";
        }

        const anterior = (texto.slice(0, inicio).match(/([A-Za-z_]+)\s*$/) || [])[1];
        const siguiente = (texto.slice(fin).match(/^\s*([A-Za-z_]+)/) || [])[1];
        const temas = [];

        if (anterior) temas.push(`palabra:${anterior.toLowerCase()} ${palabra}`);
        if (siguiente) temas.push(`palabra:${palabra} ${siguiente.toLowerCase()}`);
        temas.push(`palabra:${palabra}`);

        return temas.join("|");
    },

    restaurarFoco() {
        const elemento = this._focoPrevio;
        this._focoPrevio = null;

        if (elemento && document.body.contains(elemento)) {
            elemento.focus();
        } else {
            const nodo = this.nodoActivo();
            if (nodo) {
                nodo.focus();
            }
        }
    },

    ejecutarConsulta() {
        if (!this._espacio) {
            return;
        }

        const editor = document.getElementById("editor-sql");

        if (!editor) {
            this._espacio.invokeMethodAsync("Ejecutar", null, 0, 0);
            return;
        }

        this._espacio.invokeMethodAsync("Ejecutar", editor.value, editor.selectionStart, editor.selectionEnd);
    },

    baseContexto() {
        const activo = document.activeElement;
        let contexto = activo && activo.closest ? activo.closest("[data-base]") : null;

        if (!contexto) {
            const nodo = this.nodoActivo();
            contexto = nodo ? nodo.closest("[data-base]") : null;
        }

        return contexto ? contexto.dataset.base : null;
    },

    nuevaConsulta() {
        if (this._espacio) {
            this._espacio.invokeMethodAsync("NuevaConsulta", this.baseContexto());
        }
    },

    nuevaPestana() {
        if (this._espacio) {
            this._espacio.invokeMethodAsync("NuevaPestana", this.baseContexto());
        }
    },

    cerrarPestana() {
        if (this._espacio) {
            this._espacio.invokeMethodAsync("CerrarPestana");
        }
    },

    cambiarPestana(desplazamiento) {
        if (this._espacio) {
            this._espacio.invokeMethodAsync("CambiarPestana", desplazamiento);
        }
    },

    buscarObjeto() {
        if (this._espacio) {
            this._espacio.invokeMethodAsync("BuscarObjeto", this.baseContexto());
        }
    },

    recordarFoco() {
        this._focoBusqueda = document.activeElement;
    },

    volverFoco() {
        const elemento = this._focoBusqueda;
        this._focoBusqueda = null;

        if (elemento && document.contains(elemento) && elemento !== document.body) {
            elemento.focus();
            return;
        }

        const nodo = this.nodoActivo();
        if (nodo) {
            nodo.focus();
        }
    },

    prepararLista(lista) {
        const teclas = ["ArrowUp", "ArrowDown", "Home", "End", "Enter", " "];
        lista.addEventListener("keydown", e => {
            if (teclas.includes(e.key)) {
                e.preventDefault();
            }
        });
    },

    abrirBusqueda() {
        if (this._espacio) {
            this._espacio.invokeMethodAsync("AbrirBusqueda");
        }
    },

    buscarSiguiente(atras) {
        if (this._espacio) {
            this._espacio.invokeMethodAsync("BuscarSiguiente", atras);
        }
    },

    seleccionEditor() {
        const editor = document.getElementById("editor-sql");
        return editor ? { inicio: editor.selectionStart, fin: editor.selectionEnd, texto: editor.value } : null;
    },

    seleccionarEnEditor(inicio, fin) {
        const editor = document.getElementById("editor-sql");
        if (editor) {
            editor.setSelectionRange(inicio, fin);
        }
    },

    reemplazarEnEditor(inicio, fin, texto) {
        const editor = document.getElementById("editor-sql");
        if (!editor) {
            return null;
        }

        const volver = document.activeElement;
        editor.focus();
        editor.setSelectionRange(inicio, fin);
        document.execCommand(texto === "" ? "delete" : "insertText", false, texto);
        if (volver && volver !== editor) {
            volver.focus();
        }

        return editor.value;
    },

    reemplazarTodoEnEditor(texto) {
        const editor = document.getElementById("editor-sql");
        if (!editor) {
            return;
        }

        const volver = document.activeElement;
        editor.focus();
        editor.select();
        document.execCommand(texto === "" ? "delete" : "insertText", false, texto);
        editor.setSelectionRange(0, 0);
        if (volver && volver !== editor) {
            volver.focus();
        }
    },

    enfocarYSeleccionar(id) {
        const elemento = document.getElementById(id);
        if (elemento) {
            elemento.focus();
            elemento.select();
        }
    },

    textoEditor() {
        const editor = document.getElementById("editor-sql");
        return editor ? editor.value : null;
    },

    abrirArchivo() {
        if (this._espacio) {
            this._espacio.invokeMethodAsync("AbrirArchivo", this.textoEditor());
        }
    },

    guardarArchivo(comoNuevo) {
        if (this._espacio) {
            this._espacio.invokeMethodAsync("GuardarArchivo", this.textoEditor(), comoNuevo);
        }
    },

    abrirHistorial() {
        if (this._espacio) {
            this._espacio.invokeMethodAsync("AbrirHistorial");
        }
    },

    irALinea(linea) {
        const editor = document.getElementById("editor-sql");
        if (!editor) {
            return;
        }

        const lineas = editor.value.split("\n");
        let posicion = 0;
        for (let i = 0; i < Math.min(linea - 1, lineas.length); i++) {
            posicion += lineas[i].length + 1;
        }

        editor.focus();
        editor.setSelectionRange(posicion, posicion);
    },

    cambiarZona(atras) {
        const zonas = [document.querySelector("[role=tablist] [role=tab][aria-selected=true]")]
            .concat(["editor-sql", "titulo-resultados", "titulo-mensajes"].map(id => document.getElementById(id)))
            .filter(Boolean);

        if (zonas.length === 0) {
            return;
        }

        const activo = document.activeElement;
        const zona = activo && activo.closest ? activo.closest("[data-zona]") : null;
        const orden = { pestanas: 0, editor: 1, resultados: 2, mensajes: 3 };
        const actual = zona ? orden[zona.dataset.zona] : -1;

        let siguiente;
        if (actual === undefined || actual < 0) {
            siguiente = atras ? zonas.length - 1 : 0;
        } else {
            siguiente = (actual + (atras ? zonas.length - 1 : 1)) % zonas.length;
        }

        zonas[siguiente].focus();
        zonas[siguiente].scrollIntoView({ block: "nearest" });
    }
};

let altSolo = false;

document.addEventListener("keyup", e => {
    if (e.key === "Alt" && altSolo) {
        altSolo = false;
        e.preventDefault();
        window.miGestor.activarBarra();
    }
});

document.addEventListener("keydown", e => {
    const gestor = window.miGestor;

    if (e.key === "Alt") {
        if (!e.repeat) {
            altSolo = !e.ctrlKey && !e.shiftKey && !e.metaKey;
        }

        return;
    }

    altSolo = false;

    if (e.key === "F10" && !e.shiftKey && !e.ctrlKey && !e.altKey) {
        e.preventDefault();
        gestor.activarBarra();
        return;
    }

    if (e.key === "F1") {
        e.preventDefault();
        gestor.abrirAyuda();
        return;
    }

    const tecla = e.key.toLowerCase();
    const atajoControl = e.ctrlKey && !e.altKey && ["n", "o", "s", "h", "t", "w", "f", "b"].includes(tecla);
    const cambioPestana = e.ctrlKey && !e.altKey && e.key === "Tab";

    if (gestor.dialogoAbierto()) {
        if (["F3", "F5", "F6", "F8"].includes(e.key) || atajoControl || cambioPestana) {
            e.preventDefault();
        }
        return;
    }

    if (cambioPestana) {
        e.preventDefault();
        gestor.cambiarPestana(e.shiftKey ? -1 : 1);
        return;
    }

    const enPestanas = document.activeElement && document.activeElement.getAttribute("role") === "tab";
    if (enPestanas && ["ArrowLeft", "ArrowRight", "Home", "End"].includes(e.key)) {
        e.preventDefault();
    }

    if (atajoControl) {
        e.preventDefault();

        if (tecla === "o" && !e.shiftKey) {
            gestor.abrirArchivo();
        } else if (tecla === "s") {
            gestor.guardarArchivo(e.shiftKey);
        } else if (tecla === "h" && !e.shiftKey) {
            gestor.abrirHistorial();
        } else if (tecla === "n" && !e.shiftKey) {
            gestor.nuevaConsulta();
        } else if (tecla === "t" && !e.shiftKey) {
            gestor.nuevaPestana();
        } else if (tecla === "w" && !e.shiftKey) {
            gestor.cerrarPestana();
        } else if (tecla === "f" && !e.shiftKey) {
            gestor.abrirBusqueda();
        } else if (tecla === "b" && !e.shiftKey) {
            gestor.buscarObjeto();
        }

        return;
    }

    if (e.key === "F3") {
        e.preventDefault();
        gestor.buscarSiguiente(e.shiftKey);
    } else if (e.key === "F5") {
        e.preventDefault();
        gestor.ejecutarConsulta();
    } else if (e.key === "F6") {
        e.preventDefault();
        gestor.cambiarZona(e.shiftKey);
    } else if (e.key === "F8") {
        e.preventDefault();
        const nodo = gestor.nodoActivo();
        if (nodo) {
            nodo.focus();
        }
    }
});

document.addEventListener("keydown", e => {
    if (e.target && e.target.id === "editor-sql") {
        miGestor.sugerencias.tecla(e);
    }
}, true);

document.addEventListener("input", e => {
    if (e.target && e.target.id === "editor-sql") {
        miGestor.sugerencias.alCambiar(e);
    }
}, true);

document.addEventListener("focusout", e => {
    if (e.target && e.target.id === "editor-sql") {
        miGestor.sugerencias.cerrar();
    }
}, true);

document.addEventListener("mousedown", e => {
    if (e.target && e.target.id === "editor-sql") {
        miGestor.sugerencias.descartada = false;
        miGestor.sugerencias.cerrar();
    }
}, true);

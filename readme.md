# MiGestorBDAccesible

*Gestor de bases de datos accesible con lector de pantalla, para SQL Server, Oracle, PostgreSQL, MySQL y MariaDB.*

Hola, soy Wilmer. Trabajo y estudio con bases de datos, y uso un lector de pantalla. Los gestores de siempre, como SSMS, SQL Developer o pgAdmin, son muy completos, pero con NVDA cuesta mucho usarlos: árboles que no se anuncian bien, resultados que no se pueden recorrer, formularios que no dicen qué esperan. Me cansé de pelear con ellos, así que decidí hacer el mío.

Mi Gestor BD Accesible está pensado para usarse por completo con el teclado y un lector de pantalla. Sirve para lo mismo que esos programas: conectarte a un servidor, recorrer sus bases de datos, escribir y ejecutar consultas, y crear o modificar tablas. Pero cada cosa que hace te la dice, y casi todo lo que cambia algo en la base de datos te muestra antes el SQL y te pide confirmación.

* **Descargar:** [última versión](https://github.com/WilmerRv1989/MiGestorBDAccesible/releases/latest), con el instalador y el manual de usuario.
* **Código fuente:** este mismo repositorio, con licencia GPL. Más abajo te cuento cómo está hecho.
* English version: [readme_en.md](readme_en.md).

## Qué vas a encontrar

* **Cinco motores:** SQL Server desde 2017, Oracle desde 19c (incluida Oracle Free), PostgreSQL desde 14, MySQL desde 8.0 y MariaDB desde 10.6.
* **Un explorador de objetos** en forma de árbol: bases de datos, tablas, columnas, llaves, índices, vistas, procedimientos, funciones, secuencias, disparadores y, en Oracle, paquetes. Intro describe lo que tienes delante, en palabras.
* **Un editor de consultas** con varias pestañas, resultados en tablas que recorres con los comandos de tu lector, mensajes con «Ir a la línea» del error, historial, y buscar y reemplazar.
* **Sugerencias mientras escribes:** el editor te propone palabras de SQL, funciones, tipos de dato y los nombres de tus tablas y columnas. Tu lector las lee sin que el foco salga del editor; Tabulador acepta y Escape descarta.
* **Formularios para crear y modificar tablas,** índices, restricciones CHECK y llaves foráneas, que revisan tus datos antes de aplicar un cambio y te avisan de lo que se podría romper.
* **Editar filas** en un panel con su propia transacción: confirmas o deshaces cuando quieras, en todos los motores.
* **Para entender una base:** describir el modelo completo, con el orden de carga y los posibles problemas; el camino de JOIN entre dos tablas; buscar cualquier objeto por su nombre; y ver qué objetos usa cada uno y quién lo usa.
* **Ayuda con F1** sobre la palabra de SQL que tienes bajo el cursor o sobre cualquier sugerencia, con ejemplos probados en tu motor, y un **manual de usuario** completo.
* **Una barra de menús** con Alt o F10, para descubrir todo lo que hace el programa y sus atajos.
* **Avisos de versiones nuevas:** el programa te dice cuándo hay una actualización y la instala con tu permiso.

Funciona en Windows 10 y 11 de 64 bits. Lo pruebo a diario con NVDA. No necesita internet para trabajar. Solo se conecta a GitHub para saber si hay una versión nueva, una vez al día como mucho, y puedes desactivarlo. No envía tus datos ni tus conexiones: tus contraseñas se quedan en tu equipo, cifradas con tu cuenta de Windows.

## Cómo instalarlo

1. Descarga el instalador desde la [última versión](https://github.com/WilmerRv1989/MiGestorBDAccesible/releases/latest).
2. Como todavía no tiene firma digital, Windows puede mostrar «Windows protegió su PC». Elige «Más información» y después «Ejecutar de todas formas».
3. Sigue el asistente. Se instala solo para tu usuario y no pide permisos de administrador.

Vas a necesitar tu propio servidor de base de datos: en tu equipo, en la red o en un contenedor de Docker. El programa no trae ninguno.

Junto al instalador está el manual de usuario como página web: lo abres en tu navegador, sin instalar nada. También lo tienes dentro del programa, en el menú Ayuda.

## Cómo lo hice

Mi Gestor BD Accesible nació de una necesidad concreta. Por la falta de accesibilidad de los gestores gráficos, durante mucho tiempo mi única forma de trabajar con bases de datos fue la terminal. Funciona, pero quería algo más: un programa hecho a mi medida, que cubriera lo que de verdad necesito para estudiar y trabajar, y que pudiera usar de punta a punta con mi lector de pantalla.

Antes de escribir una línea, estudié a fondo los programas que ya existen. Con [REA](https://github.com/morluto/rea), una herramienta de ingeniería inversa que trabaja con agentes de inteligencia artificial, analicé SSMS, el gestor de SQL Server y el que mejor resuelve muchas de estas tareas, y también SQL Developer y pgAdmin. Observé cómo organizan el explorador de objetos, qué ofrece cada menú, cómo crean y modifican tablas, cómo editan filas y cómo muestran los resultados. De ahí saqué sus funciones principales, las estudié una por una y las volví a diseñar en un solo programa que no depende del motor: las mismas acciones, con los mismos nombres y las mismas teclas, ya estés en SQL Server, Oracle o PostgreSQL, y pensado desde el principio para el teclado y el lector de pantalla.

La ingeniería inversa me sirvió para entender cómo funcionan esos programas y qué hacen, no para copiarlos: el código de Mi Gestor BD Accesible es propio y está escrito desde cero.

Tanto ese análisis como el desarrollo los hice con la asistencia de Claude, un modelo de inteligencia artificial de Anthropic. Claude manejó REA, escribió la mayor parte del código siguiendo mis indicaciones, consultó la documentación de cada motor y me propuso soluciones técnicas. La dirección y las decisiones fueron mías: qué estudiar, qué hace el programa, cómo se comporta con el lector de pantalla, cómo se anuncia cada cosa, qué teclas usa y qué dejar fuera.

Probé y aprobé yo mismo con NVDA cada función, tecla y mensaje. Te lo cuento porque creo que es lo honesto. Si encuentras un error, la responsabilidad es mía, y me ayudas mucho si me lo haces saber.

## El código, abierto

Quiero que este proyecto sea transparente de punta a punta, así que el código completo está aquí, en este mismo repositorio. Puedes revisarlo, ver cómo funciona por dentro, comprobar que no envía tus datos a ninguna parte o aprender de él. Si solo quieres usar el programa, no necesitas nada de esto: descarga el instalador de la [última versión](https://github.com/WilmerRv1989/MiGestorBDAccesible/releases/latest).

Está hecho en C# con .NET 10 y Blazor dentro de una ventana de Windows (WPF). Para compilarlo necesitas Windows y el SDK de .NET 10:

* `dotnet build` compila la solución, y `dotnet test` corre las pruebas automáticas.
* `crear-instalador.ps1` genera el instalador y el manual en la carpeta `salida`. Necesita NSIS, que se instala con `winget install NSIS.NSIS`.

Cada versión llega a este repositorio como un solo commit, con su etiqueta y su descarga.

## Lo que viene

La próxima versión sumará MySQL, MariaDB y SQLite. Si usas otro motor o te falta algo, escríbeme.

## Escríbeme

Si algo no funciona, si tienes una idea o simplemente quieres contarme qué tal te fue, escríbeme a contacto@wilmer-rv.com o abre un [reporte en GitHub](https://github.com/WilmerRv1989/MiGestorBDAccesible/issues). Para ayudarte mejor, abre la ventana Acerca de (menú Ayuda), usa «Copiar la información de la versión» y pégala en tu mensaje. Lo leo todo.

Si encuentras un fallo de seguridad, no lo publiques en un reporte: sigue los pasos de la [política de seguridad](SECURITY.md) para avisarme en privado.

Hecho con amor por Wilmer Rodríguez Vega ([wilmer-rv.com](https://wilmer-rv.com)).

Con licencia GPL de GNU, versión 3 o posterior, con un [permiso adicional](EXCEPCION-ORACLE.md) para usarse con el controlador oficial de Oracle.

Nosce te ipsum.

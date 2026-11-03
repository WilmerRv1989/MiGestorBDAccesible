# Manual de usuario de Mi Gestor BD Accesible

Este manual explica, paso a paso, todo lo que puedes hacer con Mi Gestor BD Accesible. Puedes leerlo de corrido o saltar a la sección que necesites desde el índice. Cada sección es un encabezado: con la tecla H de tu lector de pantalla pasas de una a otra.

## Qué es Mi Gestor BD Accesible

Mi Gestor BD Accesible es un gestor de bases de datos pensado para usarse por completo con el teclado y un lector de pantalla. Sirve para lo mismo que SSMS, SQL Developer o pgAdmin: conectarte a un servidor, recorrer sus bases de datos y sus objetos, escribir y ejecutar consultas, y crear o modificar tablas.

Funciona con tres motores:

- SQL Server, desde la versión 2017.
- Oracle, desde la versión 19c, incluida Oracle Free.
- PostgreSQL, desde la versión 14.

Todo lo que la aplicación hace se anuncia con mensajes que tu lector de pantalla lee solo, y casi todo lo que cambia algo en la base de datos te muestra antes el SQL que va a ejecutar y te pide confirmación.

## Qué necesitas

- Windows 10 u 11 de 64 bits.
- Un lector de pantalla. La aplicación se prueba a diario con NVDA.
- Un servidor de base de datos al que conectarte: SQL Server, Oracle o PostgreSQL. Puede estar en tu propio equipo, en la red o en un contenedor de Docker. La aplicación no trae ninguno.
- WebView2, el componente de Microsoft con el que se muestra la ventana. Windows 11 ya lo trae y en Windows 10 casi siempre está instalado.

## Instalar y desinstalar

1. Abre el archivo del instalador, `MiGestorBDAccesible-1.0-instalador.exe`.
2. Como el instalador no tiene firma digital, Windows puede mostrar el aviso «Windows protegió su PC». Elige «Más información» y después el botón «Ejecutar de todas formas».
3. Sigue el asistente: la bienvenida, la carpeta de instalación y el botón Instalar.
4. En la última página puedes marcar «Crear un acceso directo en el escritorio» y «Abrir Mi Gestor BD Accesible». Después pulsa Terminar.
5. En el menú Inicio quedan «Mi Gestor BD Accesible» y «Manual de Mi Gestor BD Accesible».

El programa se instala solo para tu usuario y no pide permisos de administrador. Si Mi Gestor BD Accesible está abierto, el asistente te pide que lo cierres antes de seguir.

Para desinstalarlo, ve a Configuración, Aplicaciones, Aplicaciones instaladas, busca Mi Gestor BD Accesible y elige Desinstalar. Tus conexiones guardadas no se borran: si lo vuelves a instalar, siguen ahí.

## Cómo está organizada la ventana

Arriba está la **barra de menús**, con Archivo, Editar, Consulta y Ayuda. Se activa con Alt o con F10.

Al abrir la aplicación ves la **lista de conexiones**. Cuando te conectas, la ventana se divide en tres zonas:

- **Explorador de objetos**: un árbol con las bases de datos del servidor y sus objetos.
- **Consulta**: las pestañas de consulta, el editor de SQL, los resultados y los mensajes.
- **Detalle**: las descripciones de tablas y objetos, los formularios y los paneles, como el de editar filas.

Teclas para moverte entre zonas:

- **F8** vuelve al explorador, al último nodo donde estabas.
- **F6** y **Mayúsculas+F6** pasan entre la lista de pestañas, el editor, los resultados y los mensajes.
- **F1** abre la ayuda del lugar donde estás.

## La barra de menús

Pulsa **Alt** o **F10** para activarla. Las flechas izquierda y derecha cambian de menú; la flecha abajo, Intro o Espacio abren el menú y recorren sus opciones. Intro elige una opción. Escape cierra el menú, y otro Escape te devuelve a donde estabas.

Cada opción dice su atajo de teclado, así la barra también te sirve para aprenderlos:

- **Archivo**: Nueva consulta, Nueva pestaña, Abrir archivo, Guardar, Guardar como, Cerrar pestaña, Desconectar y Salir.
- **Editar**: Buscar y reemplazar, Buscar siguiente, Buscar objeto e Historial de consultas.
- **Consulta**: Ejecutar y Cancelar la ejecución.
- **Ayuda**: Ayuda, Atajos de teclado, Manual de usuario y Acerca de Mi Gestor BD Accesible.

Las opciones que necesitan una conexión o una consulta abierta se anuncian como «no disponible» cuando todavía no se pueden usar. Si eliges una, la aplicación te dice qué le falta.

## Conexiones

### Crear una conexión

1. En la lista de conexiones, activa **Nueva conexión**.
2. Completa el formulario:
   - **Nombre de la conexión**: el nombre con el que la verás en la lista, por ejemplo «Práctica Docker».
   - **Motor de base de datos**: SQL Server, Oracle o PostgreSQL.
   - **Servidor**: el nombre o la dirección del equipo. Para tu propio equipo o para Docker, `localhost`.
   - **Puerto**: el formulario te dice el puerto habitual del motor (1433 en SQL Server, 1521 en Oracle, 5432 en PostgreSQL). En SQL Server puedes dejarlo vacío para usar una instancia con nombre.
   - **Nombre de servicio**, solo en Oracle: en Oracle Free es `FREEPDB1`.
   - **Autenticación**, solo en SQL Server: usuario y contraseña de SQL Server, o autenticación de Windows.
   - **Usuario** y **Contraseña**.
   - **Confiar en el certificado del servidor**, solo en SQL Server: márcalo para servidores de práctica o de Docker, que usan un certificado propio.
   - **Base de datos inicial**, opcional.
3. Activa **Probar conexión** para comprobar los datos antes de guardar.
4. Activa **Guardar**.

La contraseña se guarda cifrada con tu cuenta de Windows: nadie más puede leerla, ni siquiera copiando tus archivos a otro equipo.

### Conectarte, editar y eliminar

Cada conexión de la lista tiene tres botones: **Conectar**, **Editar** y **Eliminar**. Eliminar pide confirmación y solo borra la conexión guardada, no la base de datos.

Al conectarte, la aplicación anuncia el servidor y su versión, y recupera las pestañas de consulta que tenías abiertas la última vez en esa conexión.

### Desconectarte

Usa el botón **Desconectar**, arriba en la ventana, o **Archivo, Desconectar**. Si tienes cambios sin confirmar en alguna pestaña o en el panel de editar filas, la aplicación te pregunta antes si quieres confirmarlos (COMMIT) o deshacerlos (ROLLBACK).

### Si no se pueden leer las conexiones guardadas

Tus conexiones se guardan en `%APPDATA%\MiGestorBDAccesible\conexiones.json`. Si el archivo está vacío, dañado o no contiene una lista válida, la aplicación lo conserva en la misma carpeta con el nombre `conexiones.json.danado-AAAAMMDD-HHMMSS`, con la fecha y hora local. Si ya existe una copia con ese nombre, añade un sufijo como `-2` para no sobrescribirla. La lista se abre vacía y puedes guardar conexiones nuevas.

Si solo algunas conexiones no se pueden leer, por ejemplo porque usan un motor desconocido, la aplicación conserva una copia exacta del archivo original y guarda de nuevo las conexiones legibles, ordenadas por nombre. Al volver a abrir la lista no se descartan otra vez ni se crea otra copia. Las copias se conservan aunque guardes conexiones después; la aplicación no ofrece restaurarlas.

Al abrir la lista, el lector de pantalla anuncia lo ocurrido después de enfocar el título: si el archivo estaba dañado, cuántas conexiones se descartaron y, cuando hay una copia, su nombre y su carpeta. El aviso también aparece en la lista.

Si otro programa bloquea el archivo o no tienes permiso para leerlo, la aplicación anuncia el error y deja el archivo intacto, sin moverlo ni copiarlo. Hasta resolverlo, no puedes guardar ni eliminar conexiones. Si lo intentas, el lector anuncia el error de forma urgente; al guardar, conservas los datos del formulario y el foco para poder reintentarlo.

Si se pudieron leer algunas conexiones pero falla la copia o la reescritura del archivo, la lista muestra las conexiones legibles, ordenadas por nombre. El aviso indica cuántas se descartaron, el error al conservar o reescribir el archivo y que no puedes guardar ni eliminar conexiones hasta resolverlo. Si se llegó a crear la copia, también indica su nombre y su carpeta.

Al guardar, la aplicación escribe primero en un archivo temporal en la misma carpeta y solo sustituye `conexiones.json` cuando termina. Si falla la escritura, conserva completo el archivo anterior.

## El explorador de objetos

El explorador es un árbol. Muestra el servidor, sus bases de datos (en Oracle, sus esquemas) y, dentro de cada una, carpetas con sus objetos: Tablas, Vistas, Procedimientos, Funciones, Secuencias y, en Oracle, Paquetes. Cada carpeta dice cuántos objetos tiene. Dentro de cada tabla hay carpetas con sus columnas, llaves, índices, restricciones CHECK y disparadores.

Teclas:

- **Flecha abajo** y **flecha arriba**: siguiente y anterior nodo.
- **Flecha derecha**: abre el nodo; si ya está abierto, entra al primero de sus hijos.
- **Flecha izquierda**: cierra el nodo; si ya está cerrado, sube al nodo de arriba.
- **Inicio** y **Fin**: primer y último nodo visible.
- **Intro**: sobre una tabla o un objeto, lo describe en la zona de detalle; sobre una carpeta, la abre o la cierra.
- **Tecla Aplicaciones** o **Mayúsculas+F10**: abre el menú de acciones del nodo.

### El menú de acciones

Cada nodo tiene su menú, con las acciones que tienen sentido para él. Las flechas lo recorren, una letra salta a la siguiente acción que empieza con ella, Intro la ejecuta y Escape vuelve al nodo.

Las acciones que terminan en puntos suspensivos abren un cuadro de diálogo que te muestra el SQL antes de ejecutarlo. Las que eliminan algo piden confirmación.

Algunas acciones de ejemplo:

- En una base de datos: Nueva consulta, Nueva tabla, Asistente de tabla intermedia, Describir el modelo, Camino de JOIN, Buscar objeto, y en SQL Server y PostgreSQL, Nueva base de datos y Eliminar base de datos.
- En una tabla: Describir, Ver dependencias, Seleccionar las primeras 100 filas, Editar filas, generar scripts (CREATE, SELECT, INSERT, UPDATE, DELETE y DROP), Nueva llave foránea, Nuevo índice, Nueva restricción CHECK, Modificar tabla y Eliminar tabla.
- En una vista, procedimiento o función: Describir, Ver dependencias, Ejecutar, Generar script CREATE y Eliminar.
- En un disparador: Habilitar o deshabilitar. En una secuencia: Reiniciar.

## Escribir y ejecutar consultas

### Abrir una consulta

Ponte sobre una base de datos en el explorador y pulsa **Control+N**: se abre una consulta conectada a esa base. Si ya tienes una abierta, Control+N le cambia la base y conserva el texto.

### Varias pestañas

Puedes tener varias consultas abiertas, cada una en su pestaña:

- **Control+T** abre otra pestaña.
- **Control+W** cierra la pestaña activa. Si tiene cambios sin confirmar o texto sin guardar, pregunta antes.
- **Control+Tab** y **Control+Mayúsculas+Tab** cambian de pestaña. En la lista de pestañas, las flechas izquierda y derecha pasan de una a otra.

Las pestañas se guardan solas: al volver a conectarte las encuentras como las dejaste.

### Ejecutar

Escribe tu SQL en el editor y pulsa **F5**. Se ejecuta todo el texto o, si seleccionaste una parte, solo esa parte. Mientras se ejecuta, el botón **Cancelar ejecución** detiene la consulta.

Mientras escribes, **F1** explica la palabra de SQL que está bajo el cursor, con ejemplos para el motor al que estás conectado.

### Resultados y mensajes

- Cada resultado es una tabla: usa los comandos de tabla de tu lector de pantalla para moverte por filas y columnas. La primera columna es el número de fila. Con la tecla 3 de tu lector saltas de un resultado al siguiente.
- Cada resultado tiene los botones **Copiar tabla**, que la copia separada por tabulaciones para pegarla en una hoja de cálculo, y **Exportar a CSV**.
- En los **mensajes** aparecen las filas afectadas, el tiempo y los errores. Cada error tiene un botón **Ir a la línea** que lleva el cursor del editor a la línea del problema.

### Guardar y abrir archivos

- **Control+O** abre un archivo `.sql`.
- **Control+S** guarda la consulta; la primera vez pregunta dónde.
- **Control+Mayúsculas+S** la guarda en un archivo nuevo.

### Historial de consultas

**Control+H** muestra las consultas que ejecutaste en esta conexión, de la más reciente a la más antigua. Activa una para ponerla de nuevo en el editor.

### Buscar y reemplazar

**Control+F** abre una barra encima del editor con los campos **Buscar** y **Reemplazar con**, y las casillas «Coincidir mayúsculas y minúsculas» y «Solo palabras completas».

- En el campo Buscar, **Intro** va a la siguiente coincidencia y **Mayúsculas+Intro** a la anterior. La aplicación anuncia la línea y, al final, la posición: «Coincidencia 2 de 5».
- **Escape** cierra la barra y te deja en el editor con el cursor sobre la coincidencia.
- Ya en el editor, **F3** y **Mayúsculas+F3** siguen buscando sin abrir la barra.
- **Reemplazar** cambia la coincidencia actual; **Reemplazar todo** las cambia todas y dice cuántas. Control+Z deshace los reemplazos.

## Transacciones en cada motor

Cada motor maneja de forma distinta cuándo se guardan de verdad los cambios:

- **SQL Server**: cada ejecución se confirma sola y usa su propia conexión. Si abres una transacción con BEGIN TRANSACTION, termínala con COMMIT o ROLLBACK en la misma ejecución.
- **Oracle**: los cambios de INSERT, UPDATE, DELETE y MERGE quedan pendientes hasta que ejecutes COMMIT; ROLLBACK los deshace.
- **PostgreSQL**: cada sentencia se confirma sola, salvo que escribas BEGIN; desde ahí los cambios quedan pendientes hasta COMMIT o ROLLBACK.

En Oracle y PostgreSQL cada pestaña tiene su propia sesión: lo que confirmas o deshaces en una pestaña no toca a las demás, y otra pestaña no ve tus cambios hasta que los confirmes. Mientras haya cambios pendientes, verás un aviso sobre los resultados, y la aplicación te preguntará qué hacer con ellos antes de cerrar la pestaña, desconectarte o salir.

## Crear y modificar tablas

### Nueva tabla

En el menú de una base de datos, elige **Nueva tabla…**. El formulario pide el nombre de la tabla, el esquema y sus columnas. Para cada columna eliges:

- el nombre y el tipo de datos, con su longitud, precisión o escala cuando el tipo las necesita;
- si permite NULL, si es clave principal o si su valor no se puede repetir (UNIQUE);
- si es un número automático, desde qué número empieza y de cuánto en cuánto sube;
- un valor predeterminado (DEFAULT), opcional;
- si apunta a otra tabla (llave foránea) y qué pasa al borrar o cambiar la fila de la tabla destino.

Abajo del formulario tienes un resumen en palabras y el SQL que se ejecutará. Si algo falta o no es válido, la lista de **Problemas** te lo dice antes de crear la tabla.

### Asistente de tabla intermedia

Una tabla intermedia relaciona dos tablas de muchos a muchos, como pedidos y productos. En el menú de una base, elige **Asistente de tabla intermedia…**, elige las dos tablas y el asistente prepara la tabla con sus dos llaves foráneas. Después puedes agregarle columnas, como una cantidad, en el formulario de Nueva tabla.

### Modificar tabla

En el menú de una tabla, elige **Modificar tabla…**. Se abre el mismo formulario con las columnas actuales. Puedes agregar, quitar y renombrar columnas, cambiar tipos, NULL, valores predeterminados, la clave principal, los UNIQUE y las llaves foráneas, y renombrar la tabla.

Antes de aplicar, la aplicación revisa los datos que ya existen. Por ejemplo, te avisa si quieres prohibir NULL en una columna que tiene filas vacías, o si una columna nueva obligatoria no tiene valor para las filas existentes. Cada aviso que se refiere a filas tiene el botón **Ver esas filas**, que las abre en una pestaña.

También te avisa si una vista, un procedimiento o un disparador usa la tabla y el cambio podría afectarlo.

Algunos cambios solo se pueden hacer creando la tabla de nuevo. En ese caso, la aplicación no los aplica sola: ofrece **Abrir el script de recreación en una pestaña nueva**, para que lo revises y lo ejecutes tú.

### Índices y restricciones CHECK

- **Nuevo índice…**, en el menú de una tabla: eliges las columnas en orden, ascendente o descendente, si es único, y las opciones avanzadas de tu motor. Si eliges único y ya hay valores repetidos, la aplicación te lo dice antes.
- **Nueva restricción CHECK…**: escribes la condición con un asistente o a mano. La aplicación cuenta antes las filas que no la cumplen.
- Para eliminar un índice o una restricción, ponte sobre él en el árbol, dentro de su tabla, y elige **Eliminar** en su menú.

### Eliminar una tabla

**Eliminar tabla…** te dice cuántas filas se perderán, qué tablas apuntan a ella con llaves foráneas y qué vistas o procedimientos dejarán de funcionar. En Oracle la tabla va a la papelera de reciclaje, salvo que marques «Borrar definitivamente».

## Editar filas

En el menú de una tabla, elige **Editar filas…**. Se abre un panel en la zona de detalle:

- **Buscar** encuentra un texto en cualquier columna de texto, y **Condición (WHERE)** filtra como en SQL, por ejemplo `precio > 50`.
- La **lista de filas** muestra 50 filas a la vez, con los botones «Siguientes 50» y «Anteriores 50».
- Sobre una fila, **Intro** la abre para editarla y **Supr** pide confirmación para eliminarla.
- **Nueva fila** abre el formulario vacío.

En el formulario de una fila hay un campo por columna. Las columnas opcionales tienen la casilla «Vacío (NULL)», para distinguir un texto vacío de un NULL. Las llaves foráneas son listas con los valores de la tabla destino, como «3: Libros». Las fechas se escriben como AAAA-MM-DD.

Todo lo que guardas o eliminas en el panel queda en una transacción propia del panel, en los tres motores. Arriba se anuncia cuántos cambios hay sin confirmar, con los botones **Confirmar los cambios (COMMIT)** y **Deshacer los cambios (ROLLBACK)**. Si cierras el panel con cambios pendientes, te pregunta qué hacer.

Dos cosas que conviene saber:

- Las tablas sin clave principal se pueden ver pero no editar, porque no hay forma segura de saber qué fila es cuál.
- En SQL Server, mientras el panel tenga cambios sin confirmar, las consultas de otras pestañas sobre esa tabla esperan hasta que confirmes o deshagas. En Oracle, un texto vacío se guarda como NULL.

## Conocer una base de datos

### Describir

**Intro** sobre una tabla en el explorador la describe: sus columnas con su tipo, su clave principal, a qué tablas apunta, qué tablas apuntan a ella, sus relaciones de muchos a muchos, sus índices y sus restricciones. Lo mismo con vistas, procedimientos, funciones, disparadores y secuencias.

### Describir el modelo

En el menú de una base de datos, **Describir el modelo** lee toda la base y arma un documento con:

- un resumen: cuántas tablas, filas, relaciones y otros objetos tiene;
- la lista de tablas (activa una para ver su descripción completa);
- las relaciones entre tablas;
- el **orden de carga**: en qué orden insertar los datos para que ninguna llave foránea falle;
- los **posibles problemas**, como tablas sin clave principal o llaves foráneas sin índice;
- sus vistas, procedimientos y funciones.

Mientras lee, Escape cancela. Al final del documento, **Guardar como archivo…** lo guarda como página web.

### Camino de JOIN

**Camino de JOIN…**, en el menú de una base o al final del documento del modelo, te pide dos tablas y busca el camino más corto entre ellas siguiendo las llaves foráneas. **Abrir en una pestaña nueva** escribe el SELECT con sus JOIN, listo para completar.

### Buscar un objeto

**Control+B** busca por nombre en toda la base: tablas, columnas, vistas, procedimientos, funciones, índices, restricciones, disparadores y secuencias. La lista «En» limita la búsqueda a un tipo.

Las flechas recorren los resultados e **Intro** te lleva a ese objeto en el árbol, con su menú de siempre. Control+B otra vez vuelve a la última búsqueda, sobre el resultado que elegiste.

Sirve también para los mensajes de error: si un error nombra una restricción, como `fk_pedidos_clientes`, búscala para llegar directo a su tabla.

### Dependencias

**Ver dependencias**, en el menú de tablas, vistas, procedimientos, funciones, paquetes y disparadores, muestra dos listas: **qué usa** el objeto y **quién lo usa**. Cada elemento te lleva a él en el árbol.

En PostgreSQL, las funciones normales no registran qué tablas usan. Ahí la aplicación busca el nombre en el código de la función y lo marca como «posible uso».

## La ayuda

**F1** abre la ayuda del lugar donde estás. En el editor explica la palabra bajo el cursor; en el explorador, el tipo de objeto o el concepto que tienes delante.

La ventana de ayuda tiene, arriba, los botones **Portada de la ayuda**, **Manual de usuario**, **Atrás** y **Cerrar**. En la portada puedes buscar un tema por su nombre o elegirlo de las listas: tipos de datos, conceptos, palabras de SQL, programación (PL/SQL o PL/pgSQL), atajos de teclado y, en «Sobre la aplicación», este manual y la ventana Acerca de.

Escape cierra la ayuda y te devuelve a donde estabas.

## Atajos de teclado

| Tecla | Qué hace |
|---|---|
| Alt o F10 | Activa la barra de menús |
| F1 | Ayuda del lugar donde estás |
| F3 y Mayúsculas+F3 | Siguiente y anterior coincidencia de la búsqueda |
| F5 | Ejecuta la consulta, o lo seleccionado |
| F6 y Mayúsculas+F6 | Pasan entre pestañas, editor, resultados y mensajes |
| F8 | Vuelve al explorador |
| Control+N | Nueva consulta en la base del nodo |
| Control+T | Nueva pestaña |
| Control+W | Cierra la pestaña |
| Control+Tab y Control+Mayúsculas+Tab | Cambian de pestaña |
| Control+O | Abre un archivo .sql |
| Control+S y Control+Mayúsculas+S | Guardar y Guardar como |
| Control+H | Historial de consultas |
| Control+F | Buscar y reemplazar en la consulta |
| Control+B | Buscar un objeto en la base |
| Tecla Aplicaciones o Mayúsculas+F10 | Menú de acciones del nodo |
| Escape | Cierra el menú, el diálogo o la ayuda |

## Preguntas frecuentes

**No puedo conectarme a SQL Server en Docker.** Marca «Confiar en el certificado del servidor» en la conexión, y revisa que el puerto sea el que publicaste en el contenedor.

**En Oracle no encuentro mis tablas.** Revisa el nombre de servicio (en Oracle Free, `FREEPDB1`) y busca tus tablas dentro del esquema de tu usuario, en el explorador.

**Hice un cambio y otra pestaña no lo ve.** En Oracle, y en PostgreSQL después de un BEGIN, los cambios quedan pendientes hasta el COMMIT. Confírmalos en la pestaña donde los hiciste.

**Una consulta se quedó esperando.** Puede que otra pestaña o el panel de editar filas tengan cambios sin confirmar sobre la misma tabla. Confírmalos o deshazlos, o usa Cancelar la ejecución.

**Windows no me deja abrir el instalador.** Es el aviso de SmartScreen: elige «Más información» y después «Ejecutar de todas formas».

**¿Dónde quedan mis conexiones?** En tu carpeta de usuario de Windows, con las contraseñas cifradas. Desinstalar el programa no las borra.

## Licencia y código fuente

Mi Gestor BD Accesible es software libre, con licencia GPL de GNU, versión 3 o posterior: puedes usarlo, estudiar cómo funciona, modificarlo y compartirlo, siempre que lo que distribuyas siga siendo libre y conserve el aviso de autoría. Tiene además un permiso adicional para poder usarse con el controlador oficial de Oracle, que no es software libre. Se ofrece sin ninguna garantía.

El código fuente, la licencia completa y las versiones para descargar están en [github.com/WilmerRv1989/MiGestorBDAccesible](https://github.com/WilmerRv1989/MiGestorBDAccesible).

## Contacto

Si algo no funciona, si tienes una idea o quieres contarme cómo te fue, escríbeme a contacto@wilmer-rv.com. Para que pueda ayudarte mejor, abre la ventana Acerca de (menú Ayuda) y usa el botón «Copiar la información de la versión»; pega ese texto en tu mensaje.

---

© 2026 Mi Gestor BD Accesible. Desarrollado con amor por Wilmer Rodríguez Vega ([wilmer-rv.com](https://wilmer-rv.com)).

<p><q lang="la">Nosce te ipsum</q></p>

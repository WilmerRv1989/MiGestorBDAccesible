# Política de seguridad

[English version below](#security-policy).

Mi Gestor BD Accesible guarda las conexiones a tus bases de datos y sus contraseñas, y ejecuta SQL contra servidores reales. Por eso un fallo de seguridad merece un trato distinto al de un error normal: si lo cuentas en público antes de que haya una corrección, cualquiera puede aprovecharlo mientras tanto.

## Versiones con soporte

Solo la última versión publicada recibe correcciones de seguridad. El programa avisa cuando hay una versión nueva y la instala con tu permiso, así que una corrección llega como una versión nueva. La última versión siempre está en la [página de versiones](https://github.com/WilmerRv1989/MiGestorBDAccesible/releases/latest).

## Cómo avisar de un fallo de seguridad

No abras un reporte público en GitHub ni lo cuentes en otro sitio público.

1. Entra en la pestaña de seguridad de este repositorio (Security and quality) y usa «Report a vulnerability», o ve directamente a [crear un aviso privado](https://github.com/WilmerRv1989/MiGestorBDAccesible/security/advisories/new). El aviso no es público: me llega solo a mí.
2. Si no puedes usar GitHub, escribe a contacto@wilmer-rv.com con «Seguridad» en el asunto.

Para entenderlo y corregirlo antes, cuéntame:

- qué versión usas (en la ventana Acerca de, menú Ayuda, «Copiar la información de la versión» la copia completa);
- con qué motor de base de datos pasa: SQL Server, Oracle o PostgreSQL;
- qué puede hacer alguien que aproveche el fallo y qué necesita para hacerlo;
- los pasos para reproducirlo.

No incluyas contraseñas, cadenas de conexión ni datos reales de tus bases de datos.

## Qué pasa después

Leo cada aviso y te respondo en cuanto pueda. Si se confirma el fallo, intento corregirlo en una versión nueva antes de hablar de él en público. Cuando la corrección está publicada, puedo publicar también el aviso con una descripción general del problema y, si quieres, tu nombre como quien lo descubrió.

Es un proyecto personal que mantengo en mi tiempo libre, así que no hay plazos fijos.

## Qué cuenta como fallo de seguridad

Por ejemplo:

- que otra persona o programa pueda leer las contraseñas guardadas o el contenido de `conexiones.json`, el historial o las pestañas;
- que el programa ejecute en la base de datos algo que no pediste o que no se te mostró antes de confirmar;
- que el programa envíe datos a la red fuera de la consulta de actualizaciones a GitHub;
- que la descarga o la instalación de una actualización se pueda sustituir por otra.

Un error que no pone en riesgo tus datos ni tu equipo se avisa en un [reporte normal](https://github.com/WilmerRv1989/MiGestorBDAccesible/issues).

---

# Security policy

Only the latest release receives security fixes. To report a vulnerability, do not open a public issue: use [Report a vulnerability](https://github.com/WilmerRv1989/MiGestorBDAccesible/security/advisories/new) in the Security and quality tab of this repository, or, if you cannot use GitHub, write to contacto@wilmer-rv.com with "Security" in the subject. Include the version (Help menu, About window, "Copiar la información de la versión"), the database engine, the impact and the steps to reproduce it, and leave out passwords, connection strings and real data. I read every report and answer as soon as I can. When a vulnerability is confirmed, I try to fix it in a new release before discussing it publicly, and I may then publish the advisory, crediting you if you wish. This is a personal project maintained in my spare time, so there are no fixed deadlines.

using System.Net;
using System.Text;
using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Descripcion
{
    public class NarradorModeloService
    {
        private static readonly TipoObjeto[] _tiposObjeto = [TipoObjeto.Vista, TipoObjeto.Procedimiento, TipoObjeto.Funcion, TipoObjeto.Paquete];

        public DescripcionModeloModelo Describir(ModeloBaseDatosModelo modelo, TipoMotor motor)
        {
            return new DescripcionModeloModelo
            {
                Titulo = $"Modelo de {modelo.BaseDatos}",
                Resumen = Resumen(modelo),
                Tablas = modelo.Tablas.Select(t => new TablaDescritaModelo { Detalle = t, Texto = DescribirTabla(t) }).ToList(),
                Secciones =
                [
                    new() { Titulo = "Relaciones", Frases = Relaciones(modelo) },
                    new() { Titulo = "Orden de carga", Frases = OrdenCarga(modelo) },
                    new() { Titulo = "Posibles problemas", Frases = AnalizadorModeloService.Problemas(modelo) is { Count: > 0 } problemas ? problemas : ["No se encontraron problemas."] },
                    new() { Titulo = "Vistas, procedimientos y funciones", Frases = Objetos(modelo, motor) }
                ]
            };
        }

        private static List<string> Resumen(ModeloBaseDatosModelo modelo)
        {
            if (modelo.Tablas.Count == 0)
            {
                return [$"{modelo.BaseDatos} no tiene tablas."];
            }

            var partes = new List<string>
            {
                $"{Contar(modelo.Tablas.Count, "tabla", "tablas")} con {Contar(modelo.Tablas.Sum(t => t.Filas), "fila", "filas")} en total",
                Contar(modelo.Tablas.Sum(t => t.LlavesForaneas.Count), "relación", "relaciones")
            };

            foreach (var tipo in _tiposObjeto)
            {
                var cantidad = modelo.Objetos.Count(o => o.Objeto.Tipo == tipo);

                if (cantidad > 0)
                {
                    partes.Add(Contar(cantidad, tipo.Singular(), tipo == TipoObjeto.Funcion ? "funciones" : tipo.Plural().ToLowerInvariant()));
                }
            }

            var resumen = new List<string> { $"{modelo.BaseDatos} tiene {NarradorTablaService.Unir(partes)}." };

            if (modelo.Tablas.Count > 3)
            {
                var grandes = modelo.Tablas.Where(t => t.Filas > 0).OrderByDescending(t => t.Filas).Take(3).ToList();

                if (grandes.Count > 0)
                {
                    resumen.Add($"{(grandes.Count == 1 ? "La más grande" : "Las más grandes")}: {NarradorTablaService.Unir(grandes.Select(t => $"{t.Tabla.NombreCorto()} ({Contar(t.Filas, "fila", "filas")})"))}.");
                }
            }

            var intermedias = modelo.Tablas.Where(t => t.EsTablaIntermedia).ToList();

            if (intermedias.Count > 0)
            {
                resumen.Add($"{(intermedias.Count == 1 ? "Tabla intermedia" : "Tablas intermedias")} de muchos a muchos: {NarradorTablaService.Unir(intermedias.Select(t => t.Tabla.NombreCorto()))}.");
            }

            var aisladas = AnalizadorModeloService.Aisladas(modelo);

            if (aisladas.Count > 0)
            {
                resumen.Add($"{(aisladas.Count == 1 ? "No se relaciona" : "No se relacionan")} con ninguna otra: {NarradorTablaService.Unir(aisladas.Select(t => t.Tabla.NombreCorto()))}.");
            }

            return resumen;
        }

        public static string DescribirTabla(DetalleTablaModelo tabla)
        {
            var clave = tabla.ClavePrincipal is { } principal
                ? $"clave principal {string.Join(", ", principal.ColumnasClave)}"
                : "sin clave principal";

            return $"{tabla.Tabla.NombreCorto()}: {Contar(tabla.Columnas.Count, "columna", "columnas")}, {Contar(tabla.Filas, "fila", "filas")}, {clave}.";
        }

        private static List<string> Relaciones(ModeloBaseDatosModelo modelo)
        {
            var frases = new List<string>();

            foreach (var tabla in modelo.Tablas)
            {
                var nombre = tabla.Tabla.NombreCorto();
                var externas = tabla.LlavesForaneas.Where(l => l.TablaDestino.NombreCompleto != tabla.Tabla.NombreCompleto).ToList();

                if (externas.Count > 0)
                {
                    frases.Add($"{nombre} apunta a {NarradorTablaService.Unir(externas.Select(l => $"{l.TablaDestino.NombreCorto()} (por {string.Join(", ", l.ColumnasOrigen)})"))}.");
                }

                foreach (var llave in tabla.LlavesForaneas.Where(l => l.TablaDestino.NombreCompleto == tabla.Tabla.NombreCompleto))
                {
                    frases.Add($"{nombre} apunta a sí misma por {string.Join(", ", llave.ColumnasOrigen)}.");
                }
            }

            foreach (var intermedia in modelo.Tablas.Where(t => t.EsTablaIntermedia))
            {
                var destinos = intermedia.LlavesForaneas.Select(l => l.TablaDestino.NombreCorto()).Distinct().ToList();
                frases.Add($"{NarradorTablaService.Unir(destinos)} se relacionan de muchos a muchos a través de {intermedia.Tabla.NombreCorto()}.");
            }

            foreach (var tabla in modelo.Tablas)
            {
                foreach (var llave in tabla.LlavesForaneas)
                {
                    var destino = llave.TablaDestino.NombreCorto();
                    var origen = tabla.Tabla.NombreCorto();
                    var columnas = string.Join(", ", llave.ColumnasOrigen);

                    var alEliminar = llave.AlEliminar switch
                    {
                        ReglaReferencial.Cascada => $"se eliminan sus filas en {origen} (ON DELETE CASCADE)",
                        ReglaReferencial.EstablecerNulo => $"sus filas en {origen} quedan con {columnas} vacío (ON DELETE SET NULL)",
                        ReglaReferencial.EstablecerPredeterminado => $"sus filas en {origen} toman el valor predeterminado en {columnas} (ON DELETE SET DEFAULT)",
                        _ => null
                    };

                    if (alEliminar is not null)
                    {
                        frases.Add($"Al eliminar una fila de {destino}, {alEliminar}.");
                    }

                    var alActualizar = llave.AlActualizar switch
                    {
                        ReglaReferencial.Cascada => $"también cambia en {origen} (ON UPDATE CASCADE)",
                        ReglaReferencial.EstablecerNulo => $"{origen}.{columnas} queda vacío (ON UPDATE SET NULL)",
                        ReglaReferencial.EstablecerPredeterminado => $"{origen}.{columnas} toma el valor predeterminado (ON UPDATE SET DEFAULT)",
                        _ => null
                    };

                    if (alActualizar is not null)
                    {
                        frases.Add($"Al cambiar la clave de una fila de {destino}, {alActualizar}.");
                    }
                }
            }

            return frases.Count > 0 ? frases : [$"No hay llaves foráneas entre las tablas de {modelo.BaseDatos}."];
        }

        private static List<string> OrdenCarga(ModeloBaseDatosModelo modelo)
        {
            if (modelo.Tablas.Count == 0)
            {
                return ["No hay tablas que cargar."];
            }

            var orden = AnalizadorModeloService.OrdenCarga(modelo);
            var frases = new List<string>();

            for (var i = 0; i < orden.Niveles.Count; i++)
            {
                var tablas = orden.Niveles[i].Select(t =>
                {
                    var dependencias = AnalizadorModeloService.Dependencias(modelo, t);
                    return i == 0 || dependencias.Count == 0
                        ? t.Tabla.NombreCorto()
                        : $"{t.Tabla.NombreCorto()} (necesita {NarradorTablaService.Unir(dependencias.Select(d => d.Tabla.NombreCorto()))})";
                });

                frases.Add(i == 0
                    ? $"Nivel 1, no dependen de ninguna otra: {NarradorTablaService.Unir(tablas)}."
                    : $"Nivel {i + 1}: {NarradorTablaService.Unir(tablas)}.");
            }

            if (orden.Niveles.Count > 1)
            {
                frases.Add($"Para borrar todos los datos, el orden es el inverso: primero el nivel {orden.Niveles.Count} y al final el nivel 1.");
            }

            foreach (var ciclo in orden.Ciclos)
            {
                frases.Add($"{NarradorTablaService.Unir(ciclo.Select(t => t.Tabla.NombreCorto()))} forman un ciclo: se apuntan entre sí. Para cargarlas, una de esas llaves foráneas tiene que admitir NULL: se insertan las filas sin ese dato y se completa después con UPDATE.");
            }

            foreach (var tabla in modelo.Tablas)
            {
                foreach (var llave in tabla.LlavesForaneas.Where(l => l.TablaDestino.NombreCompleto == tabla.Tabla.NombreCompleto))
                {
                    var columnas = string.Join(", ", llave.ColumnasOrigen);
                    frases.Add($"{tabla.Tabla.NombreCorto()} se apunta a sí misma: primero se cargan las filas con {columnas} vacío y después las que apuntan a ellas.");
                }
            }

            return frases;
        }

        private static List<string> Objetos(ModeloBaseDatosModelo modelo, TipoMotor motor)
        {
            if (modelo.Objetos.Count == 0)
            {
                return [$"{modelo.BaseDatos} no tiene vistas, procedimientos ni funciones."];
            }

            return modelo.Objetos
                .OrderBy(o => Array.IndexOf(_tiposObjeto, o.Objeto.Tipo))
                .Select(o => DescribirObjeto(o, motor))
                .ToList();
        }

        private static string DescribirObjeto(DetalleObjetoModelo detalle, TipoMotor motor)
        {
            var objeto = detalle.Objeto;
            var tipo = objeto.Tipo.Singular();
            var titulo = $"{char.ToUpper(tipo[0])}{tipo[1..]} {objeto.NombreCompleto}";
            var invalido = objeto.Valido ? "" : " Tiene errores de compilación.";

            switch (objeto.Tipo)
            {
                case TipoObjeto.Vista:
                    return $"{titulo}{(objeto.Materializada ? ", materializada" : "")}: {Contar(detalle.Columnas.Count, "columna", "columnas")}.{invalido}";

                case TipoObjeto.Paquete:
                    return $"{titulo}: {(detalle.Subprogramas.Count == 0 ? "sin subprogramas públicos" : NarradorTablaService.Unir(detalle.Subprogramas))}.{invalido}";

                default:
                    var parametros = detalle.Parametros.Count == 0
                        ? "sin parámetros"
                        : string.Join(", ", detalle.Parametros.Select(p =>
                            $"{p.Nombre} {p.TipoDatos}{(p.Direccion == DireccionParametro.Entrada ? "" : $" {NarradorObjetoService.Direccion(p.Direccion, motor)}")}"));
                    var retorno = objeto.Tipo == TipoObjeto.Funcion
                        ? detalle.DevuelveTabla ? ", devuelve una tabla" : detalle.Retorno is null ? "" : $", devuelve {detalle.Retorno}"
                        : "";

                    return $"{titulo} ({parametros}){retorno}.{invalido}";
            }
        }

        public static string Html(DescripcionModeloModelo descripcion)
        {
            static string C(string texto) => WebUtility.HtmlEncode(texto);

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"es\">");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"utf-8\">");
            html.AppendLine($"<title>{C(descripcion.Titulo)}</title>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine($"<h1>{C(descripcion.Titulo)}</h1>");

            foreach (var frase in descripcion.Resumen)
            {
                html.AppendLine($"<p>{C(frase)}</p>");
            }

            var secciones = new List<SeccionDescripcionModelo> { new() { Titulo = "Tablas", Frases = descripcion.Tablas.Select(t => t.Texto).ToList() } };
            secciones.AddRange(descripcion.Secciones);

            foreach (var seccion in secciones)
            {
                html.AppendLine($"<h2>{C(seccion.Titulo)}</h2>");
                html.AppendLine("<ul>");

                foreach (var frase in seccion.Frases)
                {
                    html.AppendLine($"<li>{C(frase)}</li>");
                }

                html.AppendLine("</ul>");
            }

            html.AppendLine("</body>");
            html.AppendLine("</html>");
            return html.ToString();
        }

        public static string DescribirCamino(List<PasoJoinModelo> camino)
        {
            if (camino.Count == 0)
            {
                return "";
            }

            var texto = new StringBuilder(camino[0].Anterior.NombreCorto());

            foreach (var paso in camino)
            {
                texto.Append($" → {paso.Nueva.NombreCorto()} (por {string.Join(", ", paso.NuevaEsOrigen ? paso.ColumnasNueva : paso.ColumnasAnterior)})");
            }

            texto.Append($". {Contar(camino.Count, "unión", "uniones")}.");
            return texto.ToString();
        }

        private static string Contar(long cantidad, string singular, string plural) =>
            cantidad == 1 ? $"1 {singular}" : $"{cantidad:N0} {plural}";
    }
}

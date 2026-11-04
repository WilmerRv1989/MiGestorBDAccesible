using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Edicion
{
    public enum ClaseValor
    {
        Texto,
        Numero,
        Fecha,
        Logico,
        Otro
    }

    public class ColumnaEdicionModelo
    {
        public ColumnaModelo Columna { get; init; } = new();

        public string Nombre => Columna.Nombre;

        public string Tipo => Columna.TipoDatos;

        public ClaseValor Clase { get; init; }

        public bool Obligatoria => !Columna.PermiteNulos;

        public string? MotivoSoloLectura { get; init; }

        public bool SoloLectura => MotivoSoloLectura is not null;

        public bool EsClave { get; init; }

        public int? Longitud { get; init; }

        public LlaveForaneaModelo? Llave { get; init; }

        public bool TienePredeterminado => Columna.ValorPredeterminado is not null || Columna.EsIdentidad;

        public string Etiqueta => $"{Nombre} ({Tipo}{(SoloLectura ? $", {MotivoSoloLectura}" : Obligatoria ? ", obligatoria" : "")})";
    }

    public static class EdicionFilasService
    {
        public const int TamanoPagina = 50;

        public const int MaximoOpciones = 500;

        private static readonly Regex _fecha = new(@"^\d{4}-\d{2}-\d{2}([ T]\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?)?( ?[+-]\d{2}:\d{2})?$");

        private static readonly Regex _hora = new(@"^\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?$");

        private static readonly Regex _numero = new(@"^[+-]?(\d+([.,]\d*)?|[.,]\d+)([eE][+-]?\d+)?$");

        public static List<string>? Clave(DetalleTablaModelo detalle)
        {
            if (detalle.ClavePrincipal is { } clave)
            {
                return clave.ColumnasClave.ToList();
            }

            var unico = detalle.Indices.FirstOrDefault(i => i.EsUnico
                && i.ColumnasClave.All(c => detalle.Columnas.FirstOrDefault(x => x.Nombre == c) is { PermiteNulos: false }));

            return unico?.ColumnasClave.ToList();
        }

        public static List<ColumnaEdicionModelo> Columnas(DetalleTablaModelo detalle, DialectoSqlModelo dialecto)
        {
            var clave = Clave(detalle) ?? [];
            var llaves = ComparadorTablaService.LlavesSimples(detalle);

            return detalle.Columnas.Select(c => new ColumnaEdicionModelo
            {
                Columna = c,
                Clase = Clasificar(c.TipoDatos),
                EsClave = clave.Contains(c.Nombre),
                Longitud = LongitudTexto(c.TipoDatos),
                Llave = llaves.GetValueOrDefault(c.Nombre),
                MotivoSoloLectura = c.EsIdentidad ? "número automático, solo lectura"
                    : c.EsCalculada ? "calculada, solo lectura"
                    : dialecto.SoloLectura(c.TipoDatos) ? "no se edita desde aquí"
                    : null
            }).ToList();
        }

        public static ClaseValor Clasificar(string tipo)
        {
            var nombre = Regex.Replace(tipo, @"\s*\(.*?\)|\s+(unsigned|zerofill)\b", "").Trim().ToLowerInvariant();

            return nombre switch
            {
                "bit" or "boolean" or "bool" => ClaseValor.Logico,
                "int" or "bigint" or "mediumint" or "smallint" or "tinyint" or "integer" or "decimal" or "numeric" or "money" or "smallmoney"
                    or "float" or "real" or "double" or "double precision" or "year" or "number" or "binary_float" or "binary_double" => ClaseValor.Numero,
                var t when t.StartsWith("date") || t.StartsWith("time") || t.StartsWith("smalldatetime") || t.StartsWith("timestamp") => ClaseValor.Fecha,
                var t when t.Contains("char") || t.Contains("text") || t is "clob" or "nclob" or "xml" or "uuid" or "uniqueidentifier" or "json" or "jsonb" or "enum" or "set" => ClaseValor.Texto,
                _ => ClaseValor.Otro
            };
        }

        public static string Condicion(DialectoSqlModelo d, List<ColumnaEdicionModelo> columnas, string? buscar, string? filtro)
        {
            var partes = new List<string>();

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var texto = buscar.Trim();
                var opciones = columnas.Where(c => c.Clase == ClaseValor.Texto && c.Tipo is not "uniqueidentifier" and not "uuid")
                    .Select(c => $"UPPER({d.Columna(c.Nombre)}) LIKE UPPER({d.Texto(c.Tipo, $"%{texto}%")})")
                    .ToList();

                if (_numero.IsMatch(texto))
                {
                    opciones.AddRange(columnas.Where(c => c.Clase == ClaseValor.Numero).Select(c => $"{d.Columna(c.Nombre)} = {texto.Replace(',', '.')}"));
                }

                partes.Add(opciones.Count == 0 ? "1 = 0" : $"({string.Join(" OR ", opciones)})");
            }

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                partes.Add($"({filtro.Trim()})");
            }

            return partes.Count == 0 ? "" : $" WHERE {string.Join(" AND ", partes)}";
        }

        public static string ConsultaPagina(DialectoSqlModelo d, DetalleTablaModelo detalle, List<ColumnaEdicionModelo> columnas, List<string> clave, string condicion, int desde) =>
            $"SELECT {string.Join(", ", columnas.Select(c => d.Columna(c.Nombre)))} FROM {d.Tabla(detalle.Tabla)}{condicion} ORDER BY {string.Join(", ", clave.Select(d.Columna))} {d.Pagina(desde, TamanoPagina)}";

        public static string ConsultaConteo(DialectoSqlModelo d, DetalleTablaModelo detalle, string condicion) =>
            $"SELECT COUNT(*) FROM {d.Tabla(detalle.Tabla)}{condicion}";

        public static string Literal(DialectoSqlModelo d, ColumnaEdicionModelo columna, string? valor)
        {
            if (valor is null)
            {
                return "NULL";
            }

            return columna.Clase switch
            {
                ClaseValor.Logico => d.Logico(valor is "1" or "true" or "True"),
                ClaseValor.Numero => valor.Trim().Replace(',', '.'),
                ClaseValor.Fecha => d.Fecha(columna.Tipo, valor.Trim().Replace('T', ' ')),
                _ => d.Texto(columna.Tipo, valor)
            };
        }

        public static string Insertar(DialectoSqlModelo d, DetalleTablaModelo detalle, List<ColumnaEdicionModelo> columnas, IReadOnlyDictionary<string, string?> valores)
        {
            var incluidas = columnas.Where(c => !c.SoloLectura && valores.ContainsKey(c.Nombre)).ToList();

            return incluidas.Count == 0
                ? d.InsertarPredeterminados(d.Tabla(detalle.Tabla))
                : $"INSERT INTO {d.Tabla(detalle.Tabla)} ({string.Join(", ", incluidas.Select(c => d.Columna(c.Nombre)))}){Environment.NewLine}VALUES ({string.Join(", ", incluidas.Select(c => Literal(d, c, valores[c.Nombre])))});";
        }

        public static string? Actualizar(DialectoSqlModelo d, DetalleTablaModelo detalle, List<ColumnaEdicionModelo> columnas, List<string> clave, IReadOnlyDictionary<string, string?> originales, IReadOnlyDictionary<string, string?> nuevos)
        {
            var cambiadas = columnas.Where(c => !c.SoloLectura && nuevos.TryGetValue(c.Nombre, out var nuevo) && nuevo != originales.GetValueOrDefault(c.Nombre)).ToList();

            return cambiadas.Count == 0
                ? null
                : $"UPDATE {d.Tabla(detalle.Tabla)}{Environment.NewLine}SET {string.Join(", ", cambiadas.Select(c => $"{d.Columna(c.Nombre)} = {Literal(d, c, nuevos[c.Nombre])}"))}{Environment.NewLine}{Donde(d, columnas, clave, originales)};";
        }

        public static string Eliminar(DialectoSqlModelo d, DetalleTablaModelo detalle, List<ColumnaEdicionModelo> columnas, List<string> clave, IReadOnlyDictionary<string, string?> originales) =>
            $"DELETE FROM {d.Tabla(detalle.Tabla)}{Environment.NewLine}{Donde(d, columnas, clave, originales)};";

        public static string Donde(DialectoSqlModelo d, List<ColumnaEdicionModelo> columnas, List<string> clave, IReadOnlyDictionary<string, string?> originales) =>
            "WHERE " + string.Join(" AND ", clave.Select(nombre => $"{d.Columna(nombre)} = {Literal(d, columnas.First(c => c.Nombre == nombre), originales[nombre])}"));

        public static string Resumen(List<ColumnaEdicionModelo> columnas, List<string> clave, string?[] fila)
        {
            string Valor(int i) => fila[i] switch { null => "vacío", "" => "texto vacío", var v when v.Length > 40 => v[..40] + "…", var v => v };

            var indices = columnas.Select((c, i) => (c, i)).ToList();
            var claves = indices.Where(x => clave.Contains(x.c.Nombre)).Select(x => Valor(x.i));
            var otras = indices.Where(x => !clave.Contains(x.c.Nombre) && x.c.Clase != ClaseValor.Otro).Take(3).Select(x => Valor(x.i));

            return $"Fila {string.Join(", ", claves)}: {string.Join(", ", otras)}";
        }

        public static string ConsultaOpciones(DialectoSqlModelo d, LlaveForaneaModelo llave, string? columnaTexto, string? buscar)
        {
            var destino = d.Columna(llave.ColumnasDestino[0]);
            var texto = columnaTexto is null ? null : d.Columna(columnaTexto);
            var donde = string.IsNullOrWhiteSpace(buscar)
                ? ""
                : texto is null
                    ? $" WHERE {d.ComoTexto(destino)} LIKE {d.Texto("varchar", $"%{buscar.Trim()}%")}"
                    : $" WHERE UPPER({texto}) LIKE UPPER({d.Texto("varchar", $"%{buscar.Trim()}%")})";

            return $"SELECT {destino}{(texto is null ? "" : $", {texto}")} FROM {d.Tabla(llave.TablaDestino)}{donde} ORDER BY {texto ?? destino} {d.Pagina(0, MaximoOpciones + 1)}";
        }

        public static List<string> Validar(List<ColumnaEdicionModelo> columnas, IReadOnlyDictionary<string, string?> valores, bool nueva)
        {
            var problemas = new List<string>();

            foreach (var columna in columnas.Where(c => !c.SoloLectura && valores.ContainsKey(c.Nombre)))
            {
                var valor = valores[columna.Nombre];

                if (valor is null)
                {
                    if (columna.Obligatoria)
                    {
                        problemas.Add($"{columna.Nombre} es obligatoria (NOT NULL): escribe un valor{(nueva && columna.TienePredeterminado ? " o usa el valor predeterminado" : "")}.");
                    }

                    continue;
                }

                if (nueva && columna.Obligatoria && columna.Clase == ClaseValor.Texto && valor.Length == 0)
                {
                    problemas.Add($"{columna.Nombre} es obligatoria (NOT NULL) y está en blanco: escribe un valor.");
                    continue;
                }

                switch (columna.Clase)
                {
                    case ClaseValor.Numero when !_numero.IsMatch(valor.Trim()):
                        problemas.Add($"{columna.Nombre} debe ser un número, por ejemplo 12 o 12.50.");
                        break;
                    case ClaseValor.Fecha when !(_fecha.IsMatch(valor.Trim()) || _hora.IsMatch(valor.Trim())):
                        problemas.Add($"{columna.Nombre} debe ser una fecha con el formato AAAA-MM-DD, y la hora si hace falta: 2026-01-31 14:30.");
                        break;
                    case ClaseValor.Texto when columna.Longitud is int maximo && valor.Length > maximo:
                        problemas.Add($"{columna.Nombre} admite como mucho {maximo} caracteres, y escribiste {valor.Length}.");
                        break;
                }
            }

            return problemas;
        }

        private static int? LongitudTexto(string tipo) =>
            Clasificar(tipo) == ClaseValor.Texto && Regex.Match(tipo, @"\((\d+)") is { Success: true } coincidencia
                ? int.Parse(coincidencia.Groups[1].Value, CultureInfo.InvariantCulture)
                : null;
    }
}

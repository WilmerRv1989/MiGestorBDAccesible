using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using Npgsql;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.PostgreSql
{
    public partial class ProveedorPostgreSql
    {
        private const string CategoriaEnteros = "Números enteros";
        private const string CategoriaDecimales = "Números con decimales";
        private const string CategoriaTexto = "Texto";
        private const string CategoriaFechas = "Fecha y hora";
        private const string CategoriaOtros = "Otros";

        private const string ConsultaDestinosLlave = """
            SELECT n.nspname, t.relname, a.attname, format_type(a.atttypid, a.atttypmod), c.contype = 'p'
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
            WHERE c.contype IN ('p', 'u')
              AND cardinality(c.conkey) = 1
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY n.nspname, t.relname, c.contype = 'p' DESC, a.attname
            """;

        private static readonly Regex _nombreSinComillas = new(@"^[A-Za-z_][A-Za-z0-9_$]*$");

        private static readonly IReadOnlyList<TipoDatosModelo> _tiposDatos =
        [
            new() { Nombre = "integer", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero hasta unos 2 mil millones; el más usado para id" },
            new() { Nombre = "bigint", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero muy grande" },
            new() { Nombre = "smallint", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero hasta 32 767" },
            new() { Nombre = "numeric", Categoria = CategoriaDecimales, UsaPrecision = true, PrecisionOpcional = true, Descripcion = "número con decimales exactos; ideal para dinero" },
            new() { Nombre = "real", Categoria = CategoriaDecimales, Descripcion = "número aproximado de 6 dígitos" },
            new() { Nombre = "double precision", Categoria = CategoriaDecimales, Descripcion = "número aproximado de 15 dígitos, para cálculos científicos" },
            new() { Nombre = "varchar", Categoria = CategoriaTexto, UsaLongitud = true, LongitudPredeterminada = 100, LongitudMaxima = 10485760, Descripcion = "texto de largo variable con un máximo" },
            new() { Nombre = "text", Categoria = CategoriaTexto, Descripcion = "texto de cualquier largo; muy usado en PostgreSQL" },
            new() { Nombre = "char", Categoria = CategoriaTexto, UsaLongitud = true, LongitudPredeterminada = 10, LongitudMaxima = 10485760, Descripcion = "texto de largo fijo, como códigos" },
            new() { Nombre = "date", Categoria = CategoriaFechas, Descripcion = "solo la fecha" },
            new() { Nombre = "timestamp", Categoria = CategoriaFechas, UsaFraccionSegundos = true, FraccionMaxima = 6, FraccionPredeterminada = 6, Descripcion = "fecha y hora sin zona horaria" },
            new() { Nombre = "timestamptz", Categoria = CategoriaFechas, UsaFraccionSegundos = true, FraccionMaxima = 6, FraccionPredeterminada = 6, Descripcion = "fecha y hora con zona horaria; la recomendada" },
            new() { Nombre = "time", Categoria = CategoriaFechas, UsaFraccionSegundos = true, FraccionMaxima = 6, FraccionPredeterminada = 6, Descripcion = "solo la hora" },
            new() { Nombre = "interval", Categoria = CategoriaFechas, Descripcion = "un lapso de tiempo, como 2 días" },
            new() { Nombre = "boolean", Categoria = CategoriaOtros, Descripcion = "verdadero o falso: true o false" },
            new() { Nombre = "uuid", Categoria = CategoriaOtros, Descripcion = "identificador global único" },
            new() { Nombre = "jsonb", Categoria = CategoriaOtros, Descripcion = "datos JSON guardados en binario, con índices y búsquedas" },
            new() { Nombre = "bytea", Categoria = CategoriaOtros, Descripcion = "datos binarios, como archivos" }
        ];

        public IReadOnlyList<TipoDatosModelo> TiposDatos => _tiposDatos;

        public string NombreGuardado(string nombre)
        {
            var limpio = nombre.Trim();
            return _nombreSinComillas.IsMatch(limpio) && !_palabrasReservadas.Contains(limpio.ToLowerInvariant())
                ? limpio.ToLowerInvariant()
                : limpio;
        }

        public string? AvisoNombre(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return null;
            }

            var limpio = nombre.Trim();
            var guardado = NombreGuardado(limpio);

            if (guardado != limpio)
            {
                return $"PostgreSQL lo guardará como {guardado}.";
            }

            return Identificador(guardado) == guardado
                ? null
                : $"PostgreSQL lo guardará entre comillas, respetando mayúsculas y espacios: \"{guardado}\". Tendrás que escribirlo siempre así, con comillas.";
        }

        public string ScriptCrearTabla(DefinicionTablaModelo tabla)
        {
            var nombreTabla = NombreGuardado(string.IsNullOrWhiteSpace(tabla.Nombre) ? "nueva_tabla" : tabla.Nombre);
            var esquema = string.IsNullOrWhiteSpace(tabla.Esquema) ? "public" : NombreGuardado(tabla.Esquema);
            var lineas = new List<string>();

            foreach (var columna in tabla.Columnas)
            {
                var nombre = NombreGuardado(string.IsNullOrWhiteSpace(columna.Nombre) ? "sin_nombre" : columna.Nombre);
                var linea = new StringBuilder($"{Identificador(nombre)} {columna.TipoCompleto}");

                if (columna.EsIdentidad)
                {
                    linea.Append(" GENERATED ALWAYS AS IDENTITY");

                    if (columna.Semilla != 1 || columna.Incremento != 1)
                    {
                        linea.Append($" (START WITH {columna.Semilla} INCREMENT BY {columna.Incremento})");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(columna.ValorPredeterminado))
                    {
                        linea.Append($" DEFAULT {columna.ValorPredeterminado.Trim()}");
                    }

                    if (!columna.PermiteNulos)
                    {
                        linea.Append(" NOT NULL");
                    }
                }

                lineas.Add(linea.ToString());
            }

            var clave = tabla.Columnas.Where(c => c.EsClavePrincipal).Select(c => Identificador(NombreGuardado(c.Nombre))).ToList();

            if (clave.Count > 0)
            {
                lineas.Add($"CONSTRAINT {Identificador($"pk_{nombreTabla}")} PRIMARY KEY ({string.Join(", ", clave)})");
            }

            foreach (var unica in tabla.Columnas.Where(c => c.EsUnica && !(c.EsClavePrincipal && clave.Count == 1)))
            {
                var nombre = NombreGuardado(unica.Nombre);
                lineas.Add($"CONSTRAINT {Identificador($"uq_{nombreTabla}_{nombre}")} UNIQUE ({Identificador(nombre)})");
            }

            foreach (var columna in tabla.Columnas.Where(c => c.Referencia is not null && !string.IsNullOrWhiteSpace(c.Referencia.ColumnaDestino)))
            {
                lineas.Add(RestriccionLlave(nombreTabla, NombreGuardado(columna.Nombre), columna.Referencia!));
            }

            var script = new StringBuilder();
            script.AppendLine($"CREATE TABLE {(esquema == "public" ? "" : $"{Identificador(esquema)}.")}{Identificador(nombreTabla)} (");

            if (lineas.Count > 0)
            {
                script.AppendLine(string.Join($",{Environment.NewLine}", lineas.Select(l => $"    {l}")));
            }

            script.Append(");");
            return script.ToString();
        }

        public async Task<List<DestinoLlaveModelo>> ObtenerDestinosLlaveAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new NpgsqlCommand(ConsultaDestinosLlave, conexionPg);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var destinos = new List<DestinoLlaveModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                destinos.Add(new DestinoLlaveModelo
                {
                    Tabla = new TablaModelo { Esquema = lector.GetString(0), Nombre = lector.GetString(1), EsquemaPredeterminado = "public" },
                    Columna = lector.GetString(2),
                    TipoDatos = FormatearTipo(lector.GetString(3)),
                    EsClavePrincipal = lector.GetBoolean(4)
                });
            }

            return destinos;
        }

        public string ScriptCrearLlaveForanea(DefinicionLlaveForaneaModelo llave)
        {
            var script = new StringBuilder();
            script.AppendLine($"ALTER TABLE {NombreTabla(llave.TablaOrigen)}");
            script.Append($"    ADD {RestriccionLlave(llave.TablaOrigen.Nombre, llave.ColumnaOrigen, llave.Referencia)}");

            if (!llave.ComprobarExistentes)
            {
                script.Append(" NOT VALID");
            }

            script.Append(';');
            return script.ToString();
        }

        public static bool TiposCompatiblesPostgreSql(string tipoOrigen, string tipoDestino) =>
            FamiliaTipo(tipoOrigen) == FamiliaTipo(tipoDestino);

        private static string FamiliaTipo(string tipo)
        {
            var nombre = Regex.Replace(tipo, @"\s*\([^)]*\)", "").Trim().ToLowerInvariant();

            return nombre switch
            {
                "smallint" or "integer" or "int" or "int2" or "int4" or "int8" or "bigint" or "serial" or "bigserial" or "smallserial" => "entero",
                "numeric" or "decimal" => "numeric",
                "varchar" or "character varying" or "text" or "char" or "character" or "bpchar" => "texto",
                "timestamptz" or "timestamp with time zone" => "timestamptz",
                "timestamp" or "timestamp without time zone" => "timestamp",
                _ => nombre
            };
        }

        private static string RestriccionLlave(string tablaOrigen, string columnaOrigen, ReferenciaModelo referencia)
        {
            var destino = referencia.TablaDestino;
            var texto = new StringBuilder();
            texto.Append($"CONSTRAINT {Identificador($"fk_{tablaOrigen}_{destino.Nombre}_{columnaOrigen}")}");
            texto.Append($" FOREIGN KEY ({Identificador(columnaOrigen)})");
            texto.Append($" REFERENCES {NombreTabla(destino)} ({Identificador(referencia.ColumnaDestino)})");

            if (referencia.AlEliminar != ReglaReferencial.SinAccion)
            {
                texto.Append($" ON DELETE {referencia.AlEliminar.Sql()}");
            }

            if (referencia.AlActualizar != ReglaReferencial.SinAccion)
            {
                texto.Append($" ON UPDATE {referencia.AlActualizar.Sql()}");
            }

            return texto.ToString();
        }
    }
}

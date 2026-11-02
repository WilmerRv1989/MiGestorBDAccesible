using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.Oracle
{
    public partial class ProveedorOracle
    {
        private const string CategoriaNumeros = "Números";
        private const string CategoriaTexto = "Texto";
        private const string CategoriaFechas = "Fecha y hora";
        private const string CategoriaOtros = "Otros";

        private const string ConsultaDestinosLlave = """
            SELECT c.owner, c.table_name, cc.column_name, col.data_type, col.data_length, col.char_length,
                   col.char_used, col.data_precision, col.data_scale, c.constraint_type
            FROM all_constraints c
            JOIN all_cons_columns cc ON cc.owner = c.owner AND cc.constraint_name = c.constraint_name
            JOIN all_tab_cols col ON col.owner = cc.owner AND col.table_name = cc.table_name AND col.column_name = cc.column_name
            WHERE c.owner = :esquema
              AND c.constraint_type IN ('P', 'U')
              AND c.table_name NOT LIKE 'BIN$%'
              AND (SELECT COUNT(*) FROM all_cons_columns x
                   WHERE x.owner = c.owner AND x.constraint_name = c.constraint_name) = 1
            ORDER BY c.table_name, CASE c.constraint_type WHEN 'P' THEN 0 ELSE 1 END, cc.column_name
            """;

        private static readonly Regex _nombreSinComillas = new(@"^[A-Za-z][A-Za-z0-9_$#]*$");

        private static readonly IReadOnlyList<TipoDatosModelo> _tiposDatos =
        [
            new() { Nombre = "NUMBER", Categoria = CategoriaNumeros, UsaPrecision = true, PrecisionOpcional = true, AdmiteIdentidad = true, Descripcion = "número; sin precisión guarda cualquier número, y con precisión y escala sirve para dinero" },
            new() { Nombre = "INTEGER", Categoria = CategoriaNumeros, AdmiteIdentidad = true, Descripcion = "número entero; Oracle lo guarda como NUMBER(38, 0)" },
            new() { Nombre = "BINARY_DOUBLE", Categoria = CategoriaNumeros, Descripcion = "número aproximado, para cálculos científicos" },
            new() { Nombre = "VARCHAR2", Categoria = CategoriaTexto, UsaLongitud = true, LongitudPredeterminada = 100, LongitudMaxima = 4000, Descripcion = "texto de largo variable; el más usado" },
            new() { Nombre = "NVARCHAR2", Categoria = CategoriaTexto, UsaLongitud = true, LongitudPredeterminada = 100, LongitudMaxima = 2000, Descripcion = "texto Unicode de largo variable" },
            new() { Nombre = "CHAR", Categoria = CategoriaTexto, UsaLongitud = true, LongitudPredeterminada = 10, LongitudMaxima = 2000, Descripcion = "texto de largo fijo, como códigos" },
            new() { Nombre = "CLOB", Categoria = CategoriaTexto, Descripcion = "texto muy largo, de hasta varios gigabytes" },
            new() { Nombre = "DATE", Categoria = CategoriaFechas, Descripcion = "fecha y hora hasta el segundo; en Oracle DATE siempre guarda la hora" },
            new() { Nombre = "TIMESTAMP", Categoria = CategoriaFechas, UsaFraccionSegundos = true, FraccionMaxima = 9, FraccionPredeterminada = 6, Descripcion = "fecha y hora con decimales de segundo" },
            new() { Nombre = "TIMESTAMP WITH TIME ZONE", Categoria = CategoriaFechas, UsaFraccionSegundos = true, FraccionMaxima = 9, FraccionPredeterminada = 6, Descripcion = "fecha y hora con zona horaria" },
            new() { Nombre = "BOOLEAN", Categoria = CategoriaOtros, Descripcion = "verdadero o falso: TRUE o FALSE; nuevo en Oracle 23ai" },
            new() { Nombre = "RAW", Categoria = CategoriaOtros, UsaLongitud = true, LongitudPredeterminada = 16, LongitudMaxima = 2000, Descripcion = "datos binarios cortos, como identificadores" },
            new() { Nombre = "BLOB", Categoria = CategoriaOtros, Descripcion = "datos binarios grandes, como archivos" }
        ];

        public IReadOnlyList<TipoDatosModelo> TiposDatos => _tiposDatos;

        public string NombreGuardado(string nombre)
        {
            var limpio = nombre.Trim();
            return _nombreSinComillas.IsMatch(limpio) && !_palabrasReservadas.Contains(limpio.ToUpperInvariant())
                ? limpio.ToUpperInvariant()
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
                return $"Oracle lo guardará como {guardado}.";
            }

            return Identificador(guardado) == guardado
                ? null
                : $"Oracle lo guardará entre comillas, respetando mayúsculas y espacios: \"{guardado}\". Tendrás que escribirlo siempre así, con comillas.";
        }

        public string ScriptCrearTabla(DefinicionTablaModelo tabla)
        {
            var nombreTabla = NombreGuardado(string.IsNullOrWhiteSpace(tabla.Nombre) ? "NUEVA_TABLA" : tabla.Nombre);
            var esquema = string.IsNullOrWhiteSpace(tabla.Esquema) ? null : NombreGuardado(tabla.Esquema);
            var lineas = new List<string>();

            foreach (var columna in tabla.Columnas)
            {
                var nombre = NombreGuardado(string.IsNullOrWhiteSpace(columna.Nombre) ? "SIN_NOMBRE" : columna.Nombre);
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
                lineas.Add($"CONSTRAINT {Identificador($"PK_{nombreTabla}")} PRIMARY KEY ({string.Join(", ", clave)})");
            }

            foreach (var unica in tabla.Columnas.Where(c => c.EsUnica && !(c.EsClavePrincipal && clave.Count == 1)))
            {
                var nombre = NombreGuardado(unica.Nombre);
                lineas.Add($"CONSTRAINT {Identificador($"UQ_{nombreTabla}_{nombre}")} UNIQUE ({Identificador(nombre)})");
            }

            foreach (var columna in tabla.Columnas.Where(c => c.Referencia is not null && !string.IsNullOrWhiteSpace(c.Referencia.ColumnaDestino)))
            {
                lineas.Add(RestriccionLlave(nombreTabla, NombreGuardado(columna.Nombre), columna.Referencia!, esquema));
            }

            var script = new StringBuilder();
            script.AppendLine($"CREATE TABLE {(esquema is null ? "" : $"{Identificador(esquema)}.")}{Identificador(nombreTabla)} (");

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
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            await using var comando = Comando(conexionOracle, ConsultaDestinosLlave, null);
            comando.Parameters.Add("esquema", baseDatos);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var destinos = new List<DestinoLlaveModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                destinos.Add(new DestinoLlaveModelo
                {
                    Tabla = new TablaModelo { Esquema = lector.GetString(0), Nombre = lector.GetString(1), EsquemaPredeterminado = baseDatos },
                    Columna = lector.GetString(2),
                    TipoDatos = FormatearTipo(lector.GetString(3), Entero(lector, 4), Entero(lector, 5),
                        lector.IsDBNull(6) ? null : lector.GetString(6), Entero(lector, 7), Entero(lector, 8)),
                    EsClavePrincipal = lector.GetString(9) == "P"
                });
            }

            return destinos;
        }

        public string ScriptCrearLlaveForanea(DefinicionLlaveForaneaModelo llave)
        {
            var script = new StringBuilder();
            script.AppendLine($"ALTER TABLE {Identificador(llave.TablaOrigen.Esquema)}.{Identificador(llave.TablaOrigen.Nombre)}");
            script.Append($"    ADD {RestriccionLlave(llave.TablaOrigen.Nombre, llave.ColumnaOrigen, llave.Referencia, llave.TablaOrigen.Esquema)}");

            if (!llave.ComprobarExistentes)
            {
                script.Append(" ENABLE NOVALIDATE");
            }

            script.Append(';');
            return script.ToString();
        }

        public static bool TiposCompatiblesOracle(string tipoOrigen, string tipoDestino) =>
            FamiliaTipo(tipoOrigen) == FamiliaTipo(tipoDestino);

        private static string FamiliaTipo(string tipo)
        {
            var nombre = Regex.Replace(tipo, @"\s*\([^)]*\)", "").Trim().ToUpperInvariant();

            return nombre switch
            {
                "NUMBER" or "INTEGER" or "INT" or "SMALLINT" or "DECIMAL" or "NUMERIC" or "FLOAT" => "NUMBER",
                "VARCHAR2" or "VARCHAR" => "VARCHAR2",
                _ => nombre
            };
        }

        private static string RestriccionLlave(string tablaOrigen, string columnaOrigen, ReferenciaModelo referencia, string? esquemaOrigen)
        {
            var destino = referencia.TablaDestino;
            var nombreDestino = destino.Esquema == esquemaOrigen
                ? Identificador(destino.Nombre)
                : $"{Identificador(destino.Esquema)}.{Identificador(destino.Nombre)}";

            var texto = new StringBuilder();
            texto.Append($"CONSTRAINT {Identificador($"FK_{tablaOrigen}_{destino.Nombre}_{columnaOrigen}")}");
            texto.Append($" FOREIGN KEY ({Identificador(columnaOrigen)})");
            texto.Append($" REFERENCES {nombreDestino} ({Identificador(referencia.ColumnaDestino)})");

            if (referencia.AlEliminar is ReglaReferencial.Cascada or ReglaReferencial.EstablecerNulo)
            {
                texto.Append($" ON DELETE {referencia.AlEliminar.Sql()}");
            }

            return texto.ToString();
        }
    }
}

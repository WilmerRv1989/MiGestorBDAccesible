using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Edicion;
using MiGestorBDAccesible.Core.Estructura;
using MySqlConnector;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public abstract partial class ProveedorMySqlBase
    {
        private const string CategoriaEnteros = "Números enteros";
        private const string CategoriaDecimales = "Números con decimales";
        private const string CategoriaTexto = "Texto";
        private const string CategoriaFechas = "Fecha y hora";
        private const string CategoriaOtros = "Otros";

        private const string ConsultaDestinosLlave = """
            SELECT s.table_name, s.column_name, c.column_type, s.index_name = 'PRIMARY'
            FROM information_schema.statistics s
            JOIN information_schema.columns c
              ON c.table_schema = s.table_schema AND c.table_name = s.table_name AND c.column_name = s.column_name
            WHERE s.table_schema = @base AND s.non_unique = 0
              AND NOT EXISTS (SELECT 1 FROM information_schema.statistics o
                              WHERE o.table_schema = s.table_schema AND o.table_name = s.table_name
                                AND o.index_name = s.index_name AND o.seq_in_index > 1)
            ORDER BY s.table_name, s.index_name = 'PRIMARY' DESC, s.column_name
            """;

        private static readonly IReadOnlyList<TipoDatosModelo> _tiposDatos =
        [
            new() { Nombre = "int", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero hasta unos 2 mil millones; el más usado para id" },
            new() { Nombre = "bigint", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero muy grande" },
            new() { Nombre = "mediumint", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero hasta unos 8 millones" },
            new() { Nombre = "smallint", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero hasta 32 767" },
            new() { Nombre = "tinyint", Categoria = CategoriaEnteros, AdmiteIdentidad = true, Descripcion = "número entero hasta 127" },
            new() { Nombre = "decimal", Categoria = CategoriaDecimales, UsaPrecision = true, Descripcion = "número con decimales exactos; ideal para dinero" },
            new() { Nombre = "float", Categoria = CategoriaDecimales, Descripcion = "número aproximado de 7 dígitos" },
            new() { Nombre = "double", Categoria = CategoriaDecimales, Descripcion = "número aproximado de 15 dígitos, para cálculos científicos" },
            new() { Nombre = "varchar", Categoria = CategoriaTexto, UsaLongitud = true, LongitudPredeterminada = 100, LongitudMaxima = 16383, Descripcion = "texto de largo variable con un máximo" },
            new() { Nombre = "char", Categoria = CategoriaTexto, UsaLongitud = true, LongitudPredeterminada = 10, LongitudMaxima = 255, Descripcion = "texto de largo fijo, como códigos" },
            new() { Nombre = "text", Categoria = CategoriaTexto, Descripcion = "texto largo, hasta 64 kilobytes" },
            new() { Nombre = "mediumtext", Categoria = CategoriaTexto, Descripcion = "texto muy largo, hasta 16 megabytes" },
            new() { Nombre = "longtext", Categoria = CategoriaTexto, Descripcion = "texto enorme, hasta 4 gigabytes" },
            new() { Nombre = "date", Categoria = CategoriaFechas, Descripcion = "solo la fecha" },
            new() { Nombre = "datetime", Categoria = CategoriaFechas, UsaFraccionSegundos = true, FraccionMaxima = 6, FraccionPredeterminada = 0, Descripcion = "fecha y hora, sin zona horaria; la recomendada" },
            new() { Nombre = "timestamp", Categoria = CategoriaFechas, UsaFraccionSegundos = true, FraccionMaxima = 6, FraccionPredeterminada = 0, Descripcion = "fecha y hora que sigue la zona horaria; solo llega al año 2038" },
            new() { Nombre = "time", Categoria = CategoriaFechas, UsaFraccionSegundos = true, FraccionMaxima = 6, FraccionPredeterminada = 0, Descripcion = "solo la hora" },
            new() { Nombre = "year", Categoria = CategoriaFechas, Descripcion = "solo el año" },
            new() { Nombre = "boolean", Categoria = CategoriaOtros, Descripcion = "verdadero o falso; se guarda como tinyint(1), con 1 o 0" },
            new() { Nombre = "json", Categoria = CategoriaOtros, Descripcion = "datos JSON" },
            new() { Nombre = "blob", Categoria = CategoriaOtros, Descripcion = "datos binarios, como archivos" }
        ];

        public IReadOnlyList<TipoDatosModelo> TiposDatos => _tiposDatos;

        public string NombreGuardado(string nombre) => nombre.Trim();

        public string? AvisoNombre(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return null;
            }

            var limpio = nombre.Trim();

            if (Identificador(limpio) != limpio)
            {
                return $"{Motor.Nombre()} lo guardará entre acentos graves: {Delimitar(limpio)}. Tendrás que escribirlo siempre así.";
            }

            return limpio.Any(char.IsUpper)
                ? $"En Linux, {Motor.Nombre()} distingue mayúsculas y minúsculas en los nombres de tabla: tendrás que escribirlo siempre como {limpio}."
                : null;
        }

        public string ScriptCrearBaseDatos(string nombre) => $"CREATE DATABASE {Identificador(nombre)};";

        public string ScriptEliminarBaseDatos(string nombre) => $"DROP DATABASE {Identificador(nombre)};";

        public string ScriptCrearTabla(DefinicionTablaModelo tabla)
        {
            var nombreTabla = string.IsNullOrWhiteSpace(tabla.Nombre) ? "nueva_tabla" : NombreGuardado(tabla.Nombre);
            var lineas = tabla.Columnas.Select(c => DefinicionColumna(c, string.IsNullOrWhiteSpace(c.Nombre) ? "sin_nombre" : NombreGuardado(c.Nombre))).ToList();
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
            script.AppendLine($"CREATE TABLE {Identificador(nombreTabla)} (");

            if (lineas.Count > 0)
            {
                script.AppendLine(string.Join($",{Environment.NewLine}", lineas.Select(l => $"    {l}")));
            }

            var inicio = tabla.Columnas.FirstOrDefault(c => c.EsIdentidad)?.Semilla ?? 1;
            script.Append(inicio == 1 ? ") ENGINE = InnoDB;" : $") ENGINE = InnoDB AUTO_INCREMENT = {inicio};");
            return script.ToString();
        }

        public static string DefinicionColumna(DefinicionColumnaModelo columna, string nombre)
        {
            var texto = new StringBuilder($"{Identificador(nombre)} {columna.TipoCompleto}");

            if (columna.EsCalculada)
            {
                texto.Append($" GENERATED ALWAYS AS ({columna.Formula}) {(columna.EsPersistente ? "STORED" : "VIRTUAL")}");
                return texto.ToString();
            }

            if (!columna.PermiteNulos)
            {
                texto.Append(" NOT NULL");
            }

            if (columna.EsIdentidad)
            {
                texto.Append(" AUTO_INCREMENT");
            }
            else if (ComparadorTablaService.Predeterminado(columna.ValorPredeterminado) is { } valor)
            {
                texto.Append($" DEFAULT {valor}");
            }

            return texto.ToString();
        }

        public async Task<List<DestinoLlaveModelo>> ObtenerDestinosLlaveAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionMy = await AbrirAsync(conexion, cancelacion);

            return await LeerAsync(conexionMy, ConsultaDestinosLlave, baseDatos, "", cancelacion, lector => new DestinoLlaveModelo
            {
                Tabla = new TablaModelo { Esquema = "", Nombre = lector.GetString(0), EsquemaPredeterminado = "" },
                Columna = lector.GetString(1),
                TipoDatos = FormatearTipo(lector.GetString(2)),
                EsClavePrincipal = Convert.ToBoolean(lector.GetValue(3))
            });
        }

        public string ScriptCrearLlaveForanea(DefinicionLlaveForaneaModelo llave)
        {
            var alterar = $"ALTER TABLE {Identificador(llave.TablaOrigen.Nombre)}{Environment.NewLine}    ADD {RestriccionLlave(llave.TablaOrigen.Nombre, llave.ColumnaOrigen, llave.Referencia)};";

            return llave.ComprobarExistentes
                ? alterar
                : string.Join(Environment.NewLine, "SET FOREIGN_KEY_CHECKS = 0;", alterar, "SET FOREIGN_KEY_CHECKS = 1;");
        }

        public static bool TiposCompatiblesMySql(string tipoOrigen, string tipoDestino) => FamiliaTipo(tipoOrigen) == FamiliaTipo(tipoDestino);

        private static string FamiliaTipo(string tipo)
        {
            var nombre = Regex.Replace(tipo, @"\s*\([^)]*\)", "").Trim().ToLowerInvariant();
            return nombre is "varchar" or "char" ? "texto" : nombre;
        }

        private static string RestriccionLlave(string tablaOrigen, string columnaOrigen, ReferenciaModelo referencia)
        {
            var texto = new StringBuilder();
            texto.Append($"CONSTRAINT {Identificador($"fk_{tablaOrigen}_{referencia.TablaDestino.Nombre}_{columnaOrigen}")}");
            texto.Append($" FOREIGN KEY ({Identificador(columnaOrigen)})");
            texto.Append($" REFERENCES {Identificador(referencia.TablaDestino.Nombre)} ({Identificador(referencia.ColumnaDestino)})");

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

        public string ScriptCrearIndice(DefinicionIndiceModelo indice)
        {
            var clase = indice.Metodo == "FULLTEXT" ? "FULLTEXT " : indice.EsUnico ? "UNIQUE " : "";
            var columnas = indice.Columnas.Select(c => $"{Identificador(c.Nombre)}{(c.Descendente && indice.Metodo != "FULLTEXT" ? " DESC" : "")}");
            var nombre = string.IsNullOrWhiteSpace(indice.Nombre) ? "ix_nuevo" : NombreGuardado(indice.Nombre);

            return $"CREATE {clase}INDEX {Identificador(nombre)} ON {Identificador(indice.Tabla.Nombre)}{Environment.NewLine}    ({string.Join(", ", columnas)});";
        }

        public string ScriptEliminarIndice(TablaModelo tabla, IndiceModelo indice) =>
            $"DROP INDEX {Identificador(indice.Nombre)} ON {Identificador(tabla.Nombre)};";

        public string ScriptCrearCheck(DefinicionCheckModelo check)
        {
            var nombre = string.IsNullOrWhiteSpace(check.Nombre) ? "ck_nuevo" : NombreGuardado(check.Nombre);
            var condicion = string.IsNullOrWhiteSpace(check.Condicion) ? "condición" : check.Condicion.Trim();

            return $"ALTER TABLE {Identificador(check.Tabla.Nombre)}{Environment.NewLine}    ADD CONSTRAINT {Identificador(nombre)} CHECK ({condicion});";
        }

        public string ScriptEliminarCheck(TablaModelo tabla, string nombre) =>
            $"ALTER TABLE {Identificador(tabla.Nombre)} DROP CONSTRAINT {Identificador(nombre)};";

        public async Task<ISesionEdicion> AbrirSesionEdicionAsync(ConexionActiva conexion, string baseDatos, CancellationToken cancelacion = default)
        {
            var conexionMy = await AbrirAsync(conexion, cancelacion);
            await using var usar = new MySqlCommand($"USE {Delimitar(baseDatos)}", conexionMy);
            await usar.ExecuteNonQueryAsync(cancelacion);

            return new SesionEdicionAdo(conexionMy, ex => ex is MySqlException mysql ? TextoError(mysql) : ex.Message, Contexto,
                transaccion => LeerCambiosPendientesAsync(conexionMy, (MySqlTransaction)transaccion));
        }
    }
}

using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using MySqlConnector;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public abstract partial class ProveedorMySqlBase
    {
        private const string RecordatorioConfirmacion = "-- Se confirma solo. Para poder deshacerlo, ejecútalo después de START TRANSACTION y termina con COMMIT o ROLLBACK.";

        private static readonly HashSet<string> _palabrasReservadas = new(StringComparer.OrdinalIgnoreCase)
        {
            "add", "all", "alter", "and", "as", "asc", "between", "by", "case", "change", "check", "column", "condition",
            "constraint", "create", "cross", "database", "databases", "default", "delete", "desc", "describe", "distinct",
            "div", "drop", "else", "exists", "explain", "false", "for", "force", "foreign", "from", "fulltext", "function",
            "grant", "group", "groups", "having", "if", "ignore", "in", "index", "inner", "insert", "interval", "into", "is",
            "join", "key", "keys", "kill", "left", "like", "limit", "lock", "match", "mod", "natural", "not", "null", "on",
            "option", "or", "order", "outer", "partition", "primary", "procedure", "range", "rank", "read", "references",
            "regexp", "rename", "replace", "right", "row", "rows", "schema", "select", "set", "show", "table", "then", "to",
            "trigger", "true", "union", "unique", "update", "usage", "use", "using", "values", "when", "where", "window",
            "with", "write", "xor"
        };

        private static readonly Regex _nombreSimple = new(@"^[A-Za-z_][A-Za-z0-9_$]*$");

        private static readonly Regex _primerValorEnum = new(@"^enum\('((?:[^']|'')*)'");

        public async Task<string> GenerarScriptAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            TipoScript tipo,
            CancellationToken cancelacion = default)
        {
            var detalle = await ObtenerDetalleTablaAsync(conexion, baseDatos, tabla, cancelacion);
            var script = new StringBuilder();

            switch (tipo)
            {
                case TipoScript.Crear:
                    await using (var conexionMy = await AbrirAsync(conexion, cancelacion))
                    await using (var comando = new MySqlCommand($"SHOW CREATE TABLE {Delimitar(baseDatos)}.{Delimitar(tabla.Nombre)}", conexionMy))
                    await using (var lector = await comando.ExecuteReaderAsync(cancelacion))
                    {
                        await lector.ReadAsync(cancelacion);
                        script.AppendLine(AjustarLineas(lector.GetString(1)) + ";");
                    }

                    break;
                case TipoScript.Seleccionar:
                    EscribirSeleccionar(script, detalle, null);
                    break;
                case TipoScript.SeleccionarPrimeras:
                    EscribirSeleccionar(script, detalle, 100);
                    break;
                case TipoScript.Insertar:
                    EscribirInsertar(script, detalle);
                    break;
                case TipoScript.Actualizar:
                    EscribirActualizar(script, detalle);
                    break;
                case TipoScript.Eliminar:
                    EscribirEliminar(script, detalle);
                    break;
                case TipoScript.EliminarTabla:
                    EscribirEliminarTabla(script, detalle);
                    break;
            }

            return script.ToString().TrimEnd() + Environment.NewLine;
        }

        public string ScriptEliminarTabla(TablaModelo tabla) => $"DROP TABLE {Identificador(tabla.Nombre)};";

        public static string Identificador(string nombre) =>
            _nombreSimple.IsMatch(nombre) && !_palabrasReservadas.Contains(nombre) ? nombre : Delimitar(nombre);

        private static string AjustarLineas(string texto) => texto.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        private static void EscribirSeleccionar(StringBuilder script, DetalleTablaModelo detalle, int? primeras)
        {
            script.AppendLine("SELECT");
            script.AppendLine(string.Join($",{Environment.NewLine}", detalle.Columnas.Select(c => $"    {Identificador(c.Nombre)}")));

            if (primeras is int cantidad)
            {
                script.AppendLine($"FROM {Identificador(detalle.Tabla.Nombre)}");
                script.AppendLine($"LIMIT {cantidad};");
            }
            else
            {
                script.AppendLine($"FROM {Identificador(detalle.Tabla.Nombre)};");
            }
        }

        private static bool SeEscribe(ColumnaModelo columna) => !columna.EsCalculada && !columna.EsIdentidad;

        protected virtual void EscribirFinInsertar(StringBuilder script, DetalleTablaModelo detalle)
        {
            script.AppendLine(");");

            if (detalle.Columnas.Any(c => c.EsIdentidad))
            {
                script.AppendLine("-- Para ver el número automático de la fila nueva: SELECT LAST_INSERT_ID();");
            }
        }

        private void EscribirInsertar(StringBuilder script, DetalleTablaModelo detalle)
        {
            var columnas = detalle.Columnas.Where(SeEscribe).ToList();
            var tabla = Identificador(detalle.Tabla.Nombre);

            if (columnas.Count == 0)
            {
                script.AppendLine($"INSERT INTO {tabla} () VALUES ();");
                return;
            }

            script.AppendLine("-- Cambia cada valor de ejemplo por el dato real.");
            script.AppendLine($"INSERT INTO {tabla} ({string.Join(", ", columnas.Select(c => Identificador(c.Nombre)))})");
            script.AppendLine("VALUES (");

            for (var i = 0; i < columnas.Count; i++)
            {
                var coma = i < columnas.Count - 1 ? "," : "";
                script.AppendLine($"    {ValorEjemplo(columnas[i])}{coma}  -- {ComentarioColumna(columnas[i], detalle)}");
            }

            EscribirFinInsertar(script, detalle);
            script.AppendLine(RecordatorioConfirmacion);
        }

        private static void EscribirActualizar(StringBuilder script, DetalleTablaModelo detalle)
        {
            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList() ?? [];
            var columnas = detalle.Columnas
                .Where(c => SeEscribe(c) && !clave.Contains(c.Nombre, StringComparer.OrdinalIgnoreCase))
                .ToList();

            script.AppendLine("-- Deja solo las columnas que quieres cambiar y escribe sus valores nuevos.");
            script.AppendLine($"UPDATE {Identificador(detalle.Tabla.Nombre)}");
            script.AppendLine("SET");

            for (var i = 0; i < columnas.Count; i++)
            {
                var coma = i < columnas.Count - 1 ? "," : "";
                script.AppendLine($"    {Identificador(columnas[i].Nombre)} = {ValorEjemplo(columnas[i])}{coma}  -- {ComentarioColumna(columnas[i], detalle)}");
            }

            EscribirWhere(script, detalle, "cambiar");
            script.AppendLine(RecordatorioConfirmacion);
        }

        private static void EscribirEliminar(StringBuilder script, DetalleTablaModelo detalle)
        {
            script.AppendLine($"DELETE FROM {Identificador(detalle.Tabla.Nombre)}");
            EscribirWhere(script, detalle, "borrar");

            if (detalle.Referencias.Count > 0)
            {
                var tablas = NarradorTablaService.Unir(detalle.Referencias.Select(r => r.TablaOrigen.NombreCorto()).Distinct());
                script.AppendLine($"-- Atención: {tablas} apunta a esta tabla. Según su regla ON DELETE, el borrado se rechaza o arrastra filas relacionadas.");
            }

            script.AppendLine(RecordatorioConfirmacion);
        }

        private static void EscribirWhere(StringBuilder script, DetalleTablaModelo detalle, string verbo)
        {
            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList() ?? [];

            if (clave.Count == 0)
            {
                script.AppendLine($"WHERE FALSE;  -- Esta tabla no tiene clave principal. Escribe aquí la condición de las filas que quieres {verbo}.");
                return;
            }

            var condiciones = clave.Select(c =>
            {
                var columna = detalle.Columnas.First(x => x.Nombre.Equals(c, StringComparison.OrdinalIgnoreCase));
                return $"{Identificador(c)} = {ValorEjemplo(columna)}";
            });

            script.AppendLine($"WHERE {string.Join(" AND ", condiciones)};  -- Pon el valor de la clave de la fila que quieres {verbo}. Sin WHERE se afectan todas las filas.");
        }

        private void EscribirEliminarTabla(StringBuilder script, DetalleTablaModelo detalle)
        {
            foreach (var referencia in detalle.Referencias)
            {
                script.AppendLine($"-- Atención: {referencia.TablaOrigen.NombreCorto()} apunta a esta tabla con la llave {referencia.Nombre}. Hay que quitar esa llave antes:");
                script.AppendLine($"-- ALTER TABLE {Identificador(referencia.TablaOrigen.Nombre)} DROP FOREIGN KEY {Identificador(referencia.Nombre)};");
            }

            if (detalle.Referencias.Count > 0)
            {
                script.AppendLine();
            }

            script.AppendLine($"-- {Motor.Nombre()} no tiene papelera: la tabla se borra para siempre, y DROP TABLE no se puede deshacer con ROLLBACK.");
            script.AppendLine(ScriptEliminarTabla(detalle.Tabla));
        }

        private static string ComentarioColumna(ColumnaModelo columna, DetalleTablaModelo detalle)
        {
            var partes = new List<string> { columna.TipoDatos, columna.PermiteNulos ? "opcional" : "obligatoria" };

            foreach (var llave in detalle.LlavesForaneas.Where(l => l.ColumnasOrigen.Contains(columna.Nombre)))
            {
                var indice = llave.ColumnasOrigen.IndexOf(columna.Nombre);
                partes.Add($"apunta a {llave.TablaDestino.NombreCorto()}.{llave.ColumnasDestino[indice]}");
            }

            if (columna.ValorPredeterminado is not null)
            {
                partes.Add($"predeterminado {columna.ValorPredeterminado}");
            }

            return $"{columna.Nombre}: {string.Join(", ", partes)}";
        }

        public static string ValorEjemplo(ColumnaModelo columna)
        {
            if (_primerValorEnum.Match(columna.TipoDatos) is { Success: true } primero)
            {
                return $"'{primero.Groups[1].Value}'";
            }

            var tipo = columna.TipoDatos.Split('(', ' ')[0].Trim().ToLowerInvariant();

            return tipo switch
            {
                "varchar" or "char" or "text" or "tinytext" or "mediumtext" or "longtext" or "set" => "''",
                "date" => "'2026-01-01'",
                "datetime" or "timestamp" => "NOW()",
                "time" => "'08:00:00'",
                "year" => "2026",
                "json" => "'{}'",
                "binary" or "varbinary" or "blob" or "tinyblob" or "mediumblob" or "longblob" => "''",
                _ => "0"
            };
        }
    }
}

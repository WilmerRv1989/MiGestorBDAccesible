using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using MySqlConnector;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public abstract partial class ProveedorMySqlBase
    {
        public DialectoSqlModelo Dialecto { get; } = new()
        {
            Columna = Identificador,
            Tabla = t => Identificador(t.Nombre),
            Longitud = c => $"CHAR_LENGTH({c})",
            Primeras = consulta => $"{consulta} LIMIT 100",
            Literal = (tipo, valor) => LiteralParametro(new ParametroModelo { Nombre = "v", TipoDatos = tipo }, new Dictionary<string, string?> { ["v"] = valor }),
            Texto = (_, valor) => $"'{valor.Replace("\\", "\\\\").Replace("'", "''")}'",
            SoloLectura = tipo => Regex.IsMatch(tipo, @"^(tiny|medium|long)?blob|^(var)?binary|^bit\b|^geometry|^point|^polygon", RegexOptions.IgnoreCase),
            InsertarPredeterminados = tabla => $"INSERT INTO {tabla} () VALUES ();",
            Pagina = (desde, cantidad) => $"LIMIT {cantidad} OFFSET {desde}",
            ComoTexto = c => $"CAST({c} AS CHAR)"
        };

        private string Final(DefinicionColumnaModelo columna) =>
            ComparadorTablaService.NombreFinal(columna.Nombre, columna.NombreOriginal, NombreGuardado);

        public ScriptModificacionModelo ScriptModificarTabla(PlanModificacionModelo plan)
        {
            var original = plan.Original;
            var tabla = Identificador(original.Tabla.Nombre);
            var nombreFinal = ComparadorTablaService.NombreFinal(plan.Nueva.Nombre, original.Tabla.Nombre, NombreGuardado);
            var sentencias = new List<SentenciaCambioModelo>();
            var cambios = plan.Cambios;

            void Agregar(string sql, string frase) => sentencias.Add(new SentenciaCambioModelo { Sql = sql, Frase = frase });

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.QuitarLlaveForanea))
            {
                Agregar($"ALTER TABLE {tabla} DROP FOREIGN KEY {Identificador(cambio.Restriccion!)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.QuitarUnica))
            {
                Agregar($"ALTER TABLE {tabla} DROP INDEX {Identificador(cambio.Restriccion!)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.QuitarClavePrincipal))
            {
                Agregar($"ALTER TABLE {tabla} DROP PRIMARY KEY;", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.QuitarColumna))
            {
                Agregar($"ALTER TABLE {tabla} DROP COLUMN {Identificador(cambio.Anterior!.Nombre)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.RenombrarColumna))
            {
                Agregar($"ALTER TABLE {tabla} RENAME COLUMN {Identificador(cambio.Anterior!.Nombre)} TO {Identificador(Final(cambio.Nueva!))};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo is TipoCambioTabla.CambiarTipo or TipoCambioTabla.CambiarNulos or TipoCambioTabla.CambiarPredeterminado or TipoCambioTabla.CambiarIdentidad))
            {
                Agregar($"ALTER TABLE {tabla} MODIFY COLUMN {DefinicionColumna(cambio.Nueva!, Final(cambio.Nueva!))};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarColumna))
            {
                Agregar($"ALTER TABLE {tabla} ADD COLUMN {DefinicionColumna(cambio.Nueva!, Final(cambio.Nueva!))};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CrearClavePrincipal))
            {
                Agregar($"ALTER TABLE {tabla} ADD PRIMARY KEY ({string.Join(", ", cambio.Columnas.Select(Identificador))});", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarUnica))
            {
                var columna = Final(cambio.Nueva!);
                Agregar($"ALTER TABLE {tabla} ADD CONSTRAINT {Identificador($"uq_{nombreFinal}_{columna}")} UNIQUE ({Identificador(columna)});", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarLlaveForanea))
            {
                Agregar($"ALTER TABLE {tabla} ADD {RestriccionLlave(nombreFinal, Final(cambio.Nueva!), cambio.Nueva!.Referencia!)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.RenombrarTabla))
            {
                Agregar($"RENAME TABLE {tabla} TO {Identificador(nombreFinal)};", cambio.Frase);
            }

            var texto = sentencias.Count == 0
                ? ""
                : string.Join(Environment.NewLine, [$"-- {Motor.Nombre()} confirma cada ALTER TABLE por separado: si una sentencia falla, las anteriores quedan aplicadas.", "", .. sentencias.Select(s => s.Sql)]);

            return new ScriptModificacionModelo { Sentencias = sentencias, NoAplicables = [.. plan.MotivosRecreacion], Texto = texto };
        }

        public async Task<ResultadoModificacionModelo> AplicarModificacionAsync(
            ConexionActiva conexion,
            string baseDatos,
            ScriptModificacionModelo script,
            CancellationToken cancelacion = default)
        {
            var aplicados = new List<string>();

            foreach (var sentencia in script.Sentencias)
            {
                var resultado = await EjecutarAsync(conexion, baseDatos, sentencia.Sql, 1, cancelacion);

                if (resultado.Errores.FirstOrDefault() is { } error)
                {
                    var anteriores = aplicados.Count == 0 ? "" : $" Lo anterior ya quedó aplicado y no se puede deshacer: {string.Join("; ", aplicados.Distinct())}.";
                    return new ResultadoModificacionModelo { Error = error.Texto + anteriores, FraseFallida = sentencia.Frase };
                }

                aplicados.Add(sentencia.Frase);
            }

            return new ResultadoModificacionModelo { Aplicados = aplicados.Distinct().ToList() };
        }

        public async Task<string> ScriptRecrearTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            PlanModificacionModelo plan,
            CancellationToken cancelacion = default)
        {
            var original = plan.Original;
            var tabla = Identificador(original.Tabla.Nombre);
            var nombreFinal = ComparadorTablaService.NombreFinal(plan.Nueva.Nombre, original.Tabla.Nombre, NombreGuardado);
            var tablaFinal = Identificador(nombreFinal);
            var temporal = Identificador($"tmp_{nombreFinal}");
            var nuevas = plan.Nueva.Columnas;
            var renombres = ComparadorTablaService.Renombres(plan, NombreGuardado);
            var s = new StringBuilder();

            string Lista(IEnumerable<string> columnas) => string.Join(", ", columnas.Select(Identificador));

            s.AppendLine($"-- Crea de nuevo la tabla {original.Tabla.NombreCompleto} con la estructura nueva y copia sus {original.Filas:N0} filas.");
            s.AppendLine($"-- Revísalo antes de ejecutarlo con F5. {Motor.Nombre()} confirma cada paso por separado: no se puede deshacer con ROLLBACK.");
            s.AppendLine("-- Haz una copia de seguridad si los datos importan.");
            s.AppendLine();

            var referencias = original.Referencias.Where(r => r.TablaOrigen.NombreCompleto != original.Tabla.NombreCompleto).ToList();

            if (referencias.Count > 0)
            {
                s.AppendLine("-- 1. Quitar las llaves foráneas de otras tablas que apuntan a esta");

                foreach (var referencia in referencias)
                {
                    s.AppendLine($"ALTER TABLE {Identificador(referencia.TablaOrigen.Nombre)} DROP FOREIGN KEY {Identificador(referencia.Nombre)};");
                }

                s.AppendLine();
            }

            var clave = nuevas.Where(c => c.EsClavePrincipal).Select(Final).ToList();
            var lineas = nuevas.Select(c => "    " + DefinicionColumna(c, Final(c))).ToList();

            if (clave.Count > 0)
            {
                lineas.Add($"    PRIMARY KEY ({Lista(clave)})");
            }

            s.AppendLine("-- 2. Crear la tabla nueva con un nombre temporal");
            s.AppendLine($"CREATE TABLE {temporal} (");
            s.AppendLine(string.Join($",{Environment.NewLine}", lineas));
            s.AppendLine($") ENGINE = {original.Almacenamiento ?? "InnoDB"};");
            s.AppendLine();

            var copiadas = nuevas.Where(c => c.NombreOriginal is not null && !c.EsCalculada).ToList();
            s.AppendLine("-- 3. Copiar los datos");

            if (copiadas.Count > 0)
            {
                s.AppendLine($"INSERT INTO {temporal} ({Lista(copiadas.Select(Final))})");
                s.AppendLine($"SELECT {Lista(copiadas.Select(c => c.NombreOriginal!))}");
                s.AppendLine($"FROM {tabla};");
            }

            s.AppendLine();
            s.AppendLine("-- 4. Borrar la tabla original y darle su nombre a la nueva");
            s.AppendLine($"DROP TABLE {tabla};");
            s.AppendLine($"RENAME TABLE {temporal} TO {tablaFinal};");
            s.AppendLine();
            s.AppendLine("-- 5. Volver a crear los valores únicos, las restricciones CHECK y los índices");

            var unicas = ComparadorTablaService.ColumnasUnicas(original);

            foreach (var columna in nuevas.Where(c => c.EsUnica && !(c.EsClavePrincipal && clave.Count == 1)))
            {
                var nombre = columna.NombreOriginal is not null && unicas.TryGetValue(columna.NombreOriginal, out var existente)
                    ? existente
                    : $"uq_{nombreFinal}_{Final(columna)}";
                s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(nombre)} UNIQUE ({Identificador(Final(columna))});");
            }

            foreach (var indice in original.Indices.Where(i => i.EsRestriccionUnica && i.ColumnasClave.Count() > 1))
            {
                if (ComparadorTablaService.Mapear(indice.ColumnasClave, renombres) is { } columnas)
                {
                    s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(indice.Nombre)} UNIQUE ({Lista(columnas)});");
                }
            }

            foreach (var check in original.Restricciones)
            {
                s.AppendLine(check.Columna is not null && !renombres.ContainsKey(check.Columna)
                    ? $"-- La restricción CHECK {check.Nombre} usaba la columna quitada {check.Columna}; no se vuelve a crear."
                    : $"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(check.Nombre)} CHECK ({RenombrarPalabras(check.Definicion, renombres)});");
            }

            var nombresLlaves = original.LlavesForaneas.Select(l => l.Nombre).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var indice in original.Indices.Where(i => !i.EsClavePrincipal && !i.EsRestriccionUnica && !nombresLlaves.Contains(i.Nombre)))
            {
                var columnas = ComparadorTablaService.Mapear(indice.ColumnasClave, renombres);
                var clase = indice.Metodo == "fulltext" ? "FULLTEXT " : indice.EsUnico ? "UNIQUE " : "";

                s.AppendLine(columnas is null
                    ? $"-- El índice {indice.Nombre} usaba una columna quitada, una parte de una columna o una expresión; no se vuelve a crear."
                    : $"CREATE {clase}INDEX {Identificador(indice.Nombre)} ON {tablaFinal} ({string.Join(", ", columnas.Select((c, i) => $"{Identificador(c)}{(indice.Columnas[i].Descendente ? " DESC" : "")}"))});");
            }

            s.AppendLine();
            s.AppendLine("-- 6. Llaves foráneas de esta tabla");

            foreach (var columna in nuevas.Where(c => c.Referencia is { ColumnaDestino.Length: > 0 }))
            {
                s.AppendLine($"ALTER TABLE {tablaFinal} ADD {RestriccionLlave(nombreFinal, Final(columna), columna.Referencia!)};");
            }

            foreach (var llave in original.LlavesForaneas.Where(l => l.ColumnasOrigen.Count > 1))
            {
                if (ComparadorTablaService.Mapear(llave.ColumnasOrigen, renombres) is { } columnas)
                {
                    s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(llave.Nombre)} FOREIGN KEY ({Lista(columnas)}) REFERENCES {Identificador(llave.TablaDestino.Nombre)} ({Lista(llave.ColumnasDestino)}){Reglas(llave)};");
                }
            }

            if (referencias.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("-- 7. Volver a crear las llaves foráneas de otras tablas que apuntan a esta");

                foreach (var referencia in referencias)
                {
                    s.AppendLine(ComparadorTablaService.Mapear(referencia.ColumnasDestino, renombres) is { } destino
                        ? $"ALTER TABLE {Identificador(referencia.TablaOrigen.Nombre)} ADD CONSTRAINT {Identificador(referencia.Nombre)} FOREIGN KEY ({Lista(referencia.ColumnasOrigen)}) REFERENCES {tablaFinal} ({Lista(destino)}){Reglas(referencia)};"
                        : $"-- La llave foránea {referencia.Nombre} de {referencia.TablaOrigen.NombreCompleto} apuntaba a una columna quitada; no se vuelve a crear.");
                }
            }

            var disparadores = await ObtenerDisparadoresTablaAsync(conexion, baseDatos, original.Tabla, cancelacion);

            if (disparadores.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("-- 8. Los disparadores se borran con la tabla. Vuelve a crearlos:");

                foreach (var disparador in disparadores)
                {
                    s.Append(await GenerarScriptObjetoAsync(conexion, baseDatos, disparador, cancelacion));
                }
            }

            return s.ToString();
        }

        private static string Reglas(LlaveForaneaModelo llave) =>
            (llave.AlEliminar == ReglaReferencial.SinAccion ? "" : $" ON DELETE {llave.AlEliminar.Sql()}")
            + (llave.AlActualizar == ReglaReferencial.SinAccion ? "" : $" ON UPDATE {llave.AlActualizar.Sql()}");

        private static string RenombrarPalabras(string texto, Dictionary<string, string> renombres)
        {
            foreach (var (anterior, nueva) in renombres.Where(r => r.Key != r.Value))
            {
                texto = Regex.Replace(texto, $@"(?<![\w$`]){Regex.Escape(anterior)}(?![\w$`])", Identificador(nueva));
            }

            return texto;
        }
    }
}

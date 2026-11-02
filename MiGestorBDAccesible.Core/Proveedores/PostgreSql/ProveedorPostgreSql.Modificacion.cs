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
        public DialectoSqlModelo Dialecto { get; } = new()
        {
            Columna = Identificador,
            Tabla = NombreTabla,
            Longitud = c => $"length({c})",
            Primeras = consulta => $"{consulta} LIMIT 100",
            Literal = (tipo, valor) => LiteralParametro(new ParametroModelo { Nombre = "v", TipoDatos = tipo }, new Dictionary<string, string?> { ["v"] = valor }),
            Logico = valor => valor ? "true" : "false",
            SoloLectura = tipo => tipo == "bytea"
        };

        public ScriptModificacionModelo ScriptModificarTabla(PlanModificacionModelo plan)
        {
            var original = plan.Original;
            var tabla = NombreTabla(original.Tabla);
            var nombreFinal = ComparadorTablaService.NombreFinal(plan.Nueva.Nombre, original.Tabla.Nombre, NombreGuardado);
            var sentencias = new List<SentenciaCambioModelo>();
            var cambios = plan.Cambios;

            void Agregar(string sql, string frase) => sentencias.Add(new SentenciaCambioModelo { Sql = sql, Frase = frase });
            string Columna(DefinicionColumnaModelo columna) => Identificador(Final(columna));

            foreach (var cambio in cambios.Where(c => c.Tipo is TipoCambioTabla.QuitarLlaveForanea or TipoCambioTabla.QuitarUnica or TipoCambioTabla.QuitarClavePrincipal))
            {
                Agregar($"ALTER TABLE {tabla} DROP CONSTRAINT {Identificador(cambio.Restriccion!)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.QuitarColumna))
            {
                Agregar($"ALTER TABLE {tabla} DROP COLUMN {Identificador(cambio.Anterior!.Nombre)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.RenombrarColumna))
            {
                Agregar($"ALTER TABLE {tabla} RENAME COLUMN {Identificador(cambio.Anterior!.Nombre)} TO {Columna(cambio.Nueva!)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CambiarTipo))
            {
                var columna = Columna(cambio.Nueva!);
                Agregar($"ALTER TABLE {tabla} ALTER COLUMN {columna} TYPE {cambio.Nueva!.TipoCompleto} USING {columna}::{cambio.Nueva.TipoCompleto};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CambiarNulos))
            {
                Agregar($"ALTER TABLE {tabla} ALTER COLUMN {Columna(cambio.Nueva!)} {(cambio.Nueva!.PermiteNulos ? "DROP NOT NULL" : "SET NOT NULL")};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CambiarPredeterminado))
            {
                Agregar(ComparadorTablaService.Predeterminado(cambio.Nueva!.ValorPredeterminado) is { } valor
                    ? $"ALTER TABLE {tabla} ALTER COLUMN {Columna(cambio.Nueva)} SET DEFAULT {valor};"
                    : $"ALTER TABLE {tabla} ALTER COLUMN {Columna(cambio.Nueva)} DROP DEFAULT;", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CambiarIdentidad))
            {
                var nueva = cambio.Nueva!;
                var columna = Columna(nueva);

                if (!cambio.Anterior!.EsIdentidad)
                {
                    Agregar($"ALTER TABLE {tabla} ALTER COLUMN {columna} ADD GENERATED ALWAYS AS IDENTITY (START WITH {nueva.Semilla} INCREMENT BY {nueva.Incremento});", cambio.Frase);
                    Agregar($"SELECT setval(pg_get_serial_sequence('{Literal(tabla)}', '{Literal(Final(nueva))}'), COALESCE(MAX({columna}) + {nueva.Incremento}, {nueva.Semilla}), false) FROM {tabla};", cambio.Frase);
                }
                else if (!nueva.EsIdentidad)
                {
                    Agregar($"ALTER TABLE {tabla} ALTER COLUMN {columna} DROP IDENTITY;", cambio.Frase);
                }
                else
                {
                    Agregar($"ALTER TABLE {tabla} ALTER COLUMN {columna} SET INCREMENT BY {nueva.Incremento} SET START WITH {nueva.Semilla};", cambio.Frase);
                }
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarColumna))
            {
                Agregar($"ALTER TABLE {tabla} ADD COLUMN {DefinicionColumnaPg(cambio.Nueva!, false)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.CrearClavePrincipal))
            {
                Agregar($"ALTER TABLE {tabla} ADD CONSTRAINT {Identificador($"pk_{nombreFinal}")} PRIMARY KEY ({string.Join(", ", cambio.Columnas.Select(Identificador))});", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarUnica))
            {
                var columna = Final(cambio.Nueva!);
                Agregar($"ALTER TABLE {tabla} ADD CONSTRAINT {Identificador($"uq_{nombreFinal}_{columna}")} UNIQUE ({Identificador(columna)});", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.AgregarLlaveForanea))
            {
                Agregar($"ALTER TABLE {tabla} ADD {RestriccionLlave(nombreFinal, Final(cambio.Nueva!), cambio.Nueva.Referencia!)};", cambio.Frase);
            }

            foreach (var cambio in cambios.Where(c => c.Tipo == TipoCambioTabla.RenombrarTabla))
            {
                Agregar($"ALTER TABLE {tabla} RENAME TO {Identificador(nombreFinal)};", cambio.Frase);
            }

            var texto = sentencias.Count == 0
                ? ""
                : string.Join(Environment.NewLine, ["BEGIN;", "", .. sentencias.Select(s => s.Sql), "", "COMMIT;"]);

            return new ScriptModificacionModelo { Sentencias = sentencias, NoAplicables = [.. plan.MotivosRecreacion], Texto = texto };
        }

        public async Task<ResultadoModificacionModelo> AplicarModificacionAsync(
            ConexionActiva conexion,
            string baseDatos,
            ScriptModificacionModelo script,
            CancellationToken cancelacion = default)
        {
            var inicio = await EjecutarAsync(conexion, baseDatos, "BEGIN;", 1, cancelacion);

            if (inicio.Errores.FirstOrDefault() is { } errorInicio)
            {
                return new ResultadoModificacionModelo { Error = errorInicio.Texto };
            }

            foreach (var sentencia in script.Sentencias)
            {
                var resultado = await EjecutarAsync(conexion, baseDatos, sentencia.Sql, 1, cancelacion);

                if (resultado.Errores.FirstOrDefault() is { } error)
                {
                    await EjecutarAsync(conexion, baseDatos, "ROLLBACK;", 1, CancellationToken.None);
                    return new ResultadoModificacionModelo { Error = error.Texto, FraseFallida = sentencia.Frase };
                }
            }

            var fin = await EjecutarAsync(conexion, baseDatos, "COMMIT;", 1, cancelacion);

            return fin.Errores.FirstOrDefault() is { } errorFin
                ? new ResultadoModificacionModelo { Error = errorFin.Texto }
                : new ResultadoModificacionModelo { Aplicados = script.Sentencias.Select(s => s.Frase).Distinct().ToList() };
        }

        public async Task<string> ScriptRecrearTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            PlanModificacionModelo plan,
            CancellationToken cancelacion = default)
        {
            var original = plan.Original;
            var tabla = NombreTabla(original.Tabla);
            var nombreFinal = ComparadorTablaService.NombreFinal(plan.Nueva.Nombre, original.Tabla.Nombre, NombreGuardado);
            var tablaFinal = NombreTabla(new TablaModelo { Esquema = original.Tabla.Esquema, Nombre = nombreFinal });
            var temporal = NombreTabla(new TablaModelo { Esquema = original.Tabla.Esquema, Nombre = $"tmp_{nombreFinal}" });
            var nuevas = plan.Nueva.Columnas;
            var renombres = ComparadorTablaService.Renombres(plan, NombreGuardado);
            var s = new StringBuilder();

            string Columna(DefinicionColumnaModelo c) => Identificador(Final(c));
            string Lista(IEnumerable<string> columnas) => string.Join(", ", columnas.Select(Identificador));

            s.AppendLine($"-- Crea de nuevo la tabla {original.Tabla.NombreCompleto} con la estructura nueva y copia sus {original.Filas:N0} filas.");
            s.AppendLine("-- Revísalo antes de ejecutarlo con F5. Va en una transacción: si algo falla, ejecuta ROLLBACK y la tabla queda como estaba.");
            s.AppendLine("-- Si alguna vista usa la tabla, PostgreSQL no dejará borrarla: habrá que quitar la vista antes y crearla después.");
            s.AppendLine("BEGIN;");
            s.AppendLine();

            var referencias = original.Referencias.Where(r => r.TablaOrigen.NombreCompleto != original.Tabla.NombreCompleto).ToList();

            if (referencias.Count > 0)
            {
                s.AppendLine("-- 1. Quitar las llaves foráneas de otras tablas que apuntan a esta");

                foreach (var referencia in referencias)
                {
                    s.AppendLine($"ALTER TABLE {NombreTabla(referencia.TablaOrigen)} DROP CONSTRAINT {Identificador(referencia.Nombre)};");
                }

                s.AppendLine();
            }

            s.AppendLine("-- 2. Crear la tabla nueva con un nombre temporal");
            s.AppendLine($"CREATE TABLE {temporal} (");
            s.AppendLine(string.Join($",{Environment.NewLine}", nuevas.Select(c => "    " + DefinicionColumnaPg(c, true))));
            s.AppendLine(");");
            s.AppendLine();

            var copiadas = nuevas.Where(c => c.NombreOriginal is not null && !c.EsCalculada).ToList();
            s.AppendLine("-- 3. Copiar los datos");

            if (copiadas.Count > 0)
            {
                s.AppendLine($"INSERT INTO {temporal} ({string.Join(", ", copiadas.Select(Columna))})");
                s.AppendLine($"SELECT {string.Join(", ", copiadas.Select(c => Identificador(c.NombreOriginal!)))}");
                s.AppendLine($"FROM {tabla};");
            }

            s.AppendLine();
            s.AppendLine("-- 4. Borrar la tabla original y darle su nombre a la nueva");
            s.AppendLine($"DROP TABLE {tabla};");
            s.AppendLine($"ALTER TABLE {temporal} RENAME TO {Identificador(nombreFinal)};");

            foreach (var columna in nuevas.Where(c => c.EsIdentidad))
            {
                s.AppendLine($"ALTER TABLE {tablaFinal} ALTER COLUMN {Columna(columna)} SET GENERATED ALWAYS;");
                s.AppendLine($"SELECT setval(pg_get_serial_sequence('{Literal(tablaFinal)}', '{Literal(Final(columna))}'), COALESCE(MAX({Columna(columna)}) + {columna.Incremento}, {columna.Semilla}), false) FROM {tablaFinal};");
            }

            s.AppendLine();
            s.AppendLine("-- 5. Volver a crear la clave principal, los valores únicos, las restricciones CHECK y los índices");

            var clave = nuevas.Where(c => c.EsClavePrincipal).Select(c => Final(c)).ToList();

            if (clave.Count > 0)
            {
                s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(original.ClavePrincipal?.Nombre ?? $"pk_{nombreFinal}")} PRIMARY KEY ({Lista(clave)});");
            }

            var unicas = ComparadorTablaService.ColumnasUnicas(original);

            foreach (var columna in nuevas.Where(c => c.EsUnica && !(c.EsClavePrincipal && clave.Count == 1)))
            {
                var nombre = columna.NombreOriginal is not null && unicas.TryGetValue(columna.NombreOriginal, out var existente)
                    ? existente
                    : $"uq_{nombreFinal}_{Final(columna)}";
                s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(nombre)} UNIQUE ({Columna(columna)});");
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
                    : $"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(check.Nombre)} CHECK ({RenombrarPalabrasPg(check.Definicion, renombres)});");
            }

            foreach (var indice in original.Indices.Where(i => !i.EsClavePrincipal && !i.EsRestriccionUnica))
            {
                var columnas = ComparadorTablaService.Mapear(indice.ColumnasClave, renombres);
                var incluidas = ComparadorTablaService.Mapear(indice.Columnas.Where(c => c.Incluida).Select(c => c.Nombre), renombres);

                s.AppendLine(columnas is null || incluidas is null
                    ? $"-- El índice {indice.Nombre} usaba una columna quitada o una expresión; no se vuelve a crear."
                    : $"CREATE {(indice.EsUnico ? "UNIQUE " : "")}INDEX {Identificador(indice.Nombre)} ON {tablaFinal}{(indice.Metodo is null or "btree" ? "" : $" USING {indice.Metodo}")} ({string.Join(", ", columnas.Select((c, i) => $"{Identificador(c)}{(indice.Columnas.Where(x => !x.Incluida).ElementAt(i).Descendente ? " DESC" : "")}"))}){(incluidas.Count > 0 ? $" INCLUDE ({Lista(incluidas)})" : "")};");
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
                    s.AppendLine($"ALTER TABLE {tablaFinal} ADD CONSTRAINT {Identificador(llave.Nombre)} FOREIGN KEY ({Lista(columnas)}) REFERENCES {NombreTabla(llave.TablaDestino)} ({Lista(llave.ColumnasDestino)}){ReglasPg(llave)};");
                }
            }

            if (referencias.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("-- 7. Volver a crear las llaves foráneas de otras tablas que apuntan a esta");

                foreach (var referencia in referencias)
                {
                    s.AppendLine(ComparadorTablaService.Mapear(referencia.ColumnasDestino, renombres) is { } destino
                        ? $"ALTER TABLE {NombreTabla(referencia.TablaOrigen)} ADD CONSTRAINT {Identificador(referencia.Nombre)} FOREIGN KEY ({Lista(referencia.ColumnasOrigen)}) REFERENCES {tablaFinal} ({Lista(destino)}){ReglasPg(referencia)};"
                        : $"-- La llave foránea {referencia.Nombre} de {referencia.TablaOrigen.NombreCompleto} apuntaba a una columna quitada; no se vuelve a crear.");
                }
            }

            var disparadores = await DefinicionesDisparadoresPgAsync(conexion, baseDatos, original.Tabla, cancelacion);

            if (disparadores.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("-- 8. Volver a crear los disparadores de la tabla (sus funciones no se tocan)");

                foreach (var definicion in disparadores)
                {
                    s.AppendLine($"{definicion.Replace($" ON {tabla} ", $" ON {tablaFinal} ")};");
                }
            }

            s.AppendLine();
            s.AppendLine("COMMIT;");
            return s.ToString();
        }

        private static async Task<List<string>> DefinicionesDisparadoresPgAsync(ConexionActiva conexion, string baseDatos, TablaModelo tabla, CancellationToken cancelacion)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new NpgsqlCommand("""
                SELECT pg_get_triggerdef(t.oid)
                FROM pg_trigger t
                WHERE t.tgrelid = to_regclass(quote_ident(@esquema) || '.' || quote_ident(@nombre)) AND NOT t.tgisinternal
                ORDER BY t.tgname
                """, conexionPg);
            comando.Parameters.AddWithValue("esquema", tabla.Esquema);
            comando.Parameters.AddWithValue("nombre", tabla.Nombre);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var definiciones = new List<string>();

            while (await lector.ReadAsync(cancelacion))
            {
                definiciones.Add(lector.GetString(0));
            }

            return definiciones;
        }

        private string DefinicionColumnaPg(DefinicionColumnaModelo columna, bool paraCopiar)
        {
            var texto = new StringBuilder($"{Identificador(Final(columna))} {columna.TipoCompleto}");

            if (columna.EsCalculada)
            {
                texto.Append($" GENERATED ALWAYS AS ({columna.Formula}) STORED");
                return texto.ToString();
            }

            if (columna.EsIdentidad)
            {
                texto.Append(paraCopiar ? " GENERATED BY DEFAULT AS IDENTITY" : " GENERATED ALWAYS AS IDENTITY");

                if (columna.Semilla != 1 || columna.Incremento != 1)
                {
                    texto.Append($" (START WITH {columna.Semilla} INCREMENT BY {columna.Incremento})");
                }

                return texto.ToString();
            }

            if (ComparadorTablaService.Predeterminado(columna.ValorPredeterminado) is { } valor)
            {
                texto.Append($" DEFAULT {valor}");
            }

            if (!columna.PermiteNulos)
            {
                texto.Append(" NOT NULL");
            }

            return texto.ToString();
        }

        private string Final(DefinicionColumnaModelo columna) =>
            ComparadorTablaService.NombreFinal(columna.Nombre, columna.NombreOriginal, NombreGuardado);

        private static string ReglasPg(LlaveForaneaModelo llave) =>
            (llave.AlEliminar == ReglaReferencial.SinAccion ? "" : $" ON DELETE {llave.AlEliminar.Sql()}")
            + (llave.AlActualizar == ReglaReferencial.SinAccion ? "" : $" ON UPDATE {llave.AlActualizar.Sql()}");

        private static string Literal(string texto) => texto.Replace("'", "''");

        private static string RenombrarPalabrasPg(string texto, Dictionary<string, string> renombres)
        {
            foreach (var (anterior, nueva) in renombres.Where(r => r.Key != r.Value))
            {
                texto = Regex.Replace(texto, $@"(?<![\w$""]){Regex.Escape(anterior)}(?![\w$""])|""{Regex.Escape(anterior)}""", Identificador(nueva));
            }

            return texto;
        }
    }
}

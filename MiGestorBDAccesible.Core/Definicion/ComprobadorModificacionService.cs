using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Definicion
{
    public class DialectoSqlModelo
    {
        public Func<string, string> Columna { get; init; } = c => c;

        public Func<TablaModelo, string> Tabla { get; init; } = t => t.NombreCompleto;

        public Func<string, string> Longitud { get; init; } = c => $"LENGTH({c})";

        public Func<string, string, string?> NoConvertible { get; init; } = (_, _) => null;

        public Func<string, string> Primeras { get; init; } = consulta => consulta;

        public bool AlterarRequiereSinDependencias { get; init; }

        public Func<string, string, bool>? CambioDeTipoSoloVacia { get; init; }

        public bool ReducirPrecisionSoloVacia { get; init; }

        public Func<string, string, string> Literal { get; init; } = (_, valor) => $"'{valor.Replace("'", "''")}'";

        public Func<string, string> NoVacio { get; init; } = c => $"{c} <> ''";

        public bool UnicoUnSoloNulo { get; init; }

        public Func<bool, string> Logico { get; init; } = valor => valor ? "1" : "0";

        public Func<string, string, string> Fecha { get; init; } = (_, valor) => $"'{valor.Replace("'", "''")}'";

        public Func<string, string, string> Texto { get; init; } = (_, valor) => $"'{valor.Replace("'", "''")}'";

        public Func<string, bool> SoloLectura { get; init; } = _ => false;

        public Func<string, string> InsertarPredeterminados { get; init; } = tabla => $"INSERT INTO {tabla} DEFAULT VALUES;";
    }

    public class ComprobadorModificacionService
    {
        public async Task<List<ComprobacionModelo>> ComprobarAsync(
            IProveedorBaseDatos proveedor,
            ConexionActiva conexion,
            string baseDatos,
            PlanModificacionModelo plan,
            CancellationToken cancelacion = default)
        {
            var d = proveedor.Dialecto;
            var original = plan.Original;
            var tabla = d.Tabla(original.Tabla);
            var resultado = new List<ComprobacionModelo>();
            var tipos = plan.Cambios.Select(c => c.Tipo).ToHashSet();
            var motor = conexion.Conexion.Motor.Nombre();

            async Task<long> ContarAsync(string condicion)
            {
                var ejecucion = await proveedor.EjecutarAsync(conexion, baseDatos, $"SELECT COUNT(*) FROM {tabla} WHERE {condicion}", 1, cancelacion);

                if (ejecucion.Errores.FirstOrDefault() is { } error)
                {
                    throw new InvalidOperationException(error.Texto);
                }

                return long.Parse(ejecucion.Conjuntos[0].Filas[0][0] ?? "0", CultureInfo.InvariantCulture);
            }

            async Task BloquearSiAsync(string condicion, Func<long, string> mensaje)
            {
                var cantidad = await ContarAsync(condicion);

                if (cantidad > 0)
                {
                    resultado.Add(new ComprobacionModelo
                    {
                        Mensaje = mensaje(cantidad),
                        Bloquea = true,
                        ConsultaFilas = d.Primeras($"SELECT * FROM {tabla} WHERE {condicion}")
                    });
                }
            }

            void Bloquear(string mensaje) => resultado.Add(new ComprobacionModelo { Mensaje = mensaje, Bloquea = true });

            bool SeQuita(TipoCambioTabla tipo, string? restriccion) =>
                plan.Cambios.Any(c => c.Tipo == tipo && c.Restriccion == restriccion);

            List<string> Dependencias(string columna, bool incluirLlavesSimples)
            {
                var lista = new List<string>();

                foreach (var indice in original.Indices.Where(i => i.Columnas.Any(c => c.Nombre == columna)))
                {
                    if (indice.EsClavePrincipal)
                    {
                        if (!SeQuita(TipoCambioTabla.QuitarClavePrincipal, indice.Nombre))
                        {
                            lista.Add($"la clave principal {indice.Nombre}");
                        }
                    }
                    else if (indice.EsRestriccionUnica)
                    {
                        if (!SeQuita(TipoCambioTabla.QuitarUnica, indice.Nombre))
                        {
                            lista.Add($"la restricción de valor único {indice.Nombre}");
                        }
                    }
                    else
                    {
                        lista.Add($"el índice {indice.Nombre}");
                    }
                }

                foreach (var llave in original.LlavesForaneas.Where(l => l.ColumnasOrigen.Contains(columna)))
                {
                    if ((incluirLlavesSimples || llave.ColumnasOrigen.Count > 1) && !SeQuita(TipoCambioTabla.QuitarLlaveForanea, llave.Nombre))
                    {
                        lista.Add($"la llave foránea {llave.Nombre}");
                    }
                }

                foreach (var referencia in original.Referencias.Where(l => l.ColumnasDestino.Contains(columna)))
                {
                    lista.Add($"la llave foránea {referencia.Nombre} de la tabla {referencia.TablaOrigen.NombreCorto()}");
                }

                foreach (var check in original.Restricciones.Where(r => r.Columna == columna
                    || Regex.IsMatch(r.Definicion, $@"(?<![\w$#]){Regex.Escape(columna)}(?![\w$#])", RegexOptions.IgnoreCase)))
                {
                    lista.Add($"la restricción CHECK {check.Nombre}");
                }

                return lista;
            }

            foreach (var cambio in plan.Cambios)
            {
                var anterior = cambio.Anterior;
                var columna = anterior is null ? null : d.Columna(anterior.Nombre);

                switch (cambio.Tipo)
                {
                    case TipoCambioTabla.QuitarColumna:
                        var dependencias = Dependencias(anterior!.Nombre, false);

                        if (dependencias.Count > 0)
                        {
                            Bloquear($"No se puede quitar {anterior.Nombre}: la usa {NarradorTablaService.Unir(dependencias)}. Quítalo primero.");
                        }
                        else if (original.Filas > 0)
                        {
                            resultado.Add(new ComprobacionModelo
                            {
                                Mensaje = $"Se borrarán los datos de {anterior.Nombre} en {(original.Filas == 1 ? "la única fila" : $"las {original.Filas:N0} filas")}."
                            });
                        }

                        break;

                    case TipoCambioTabla.CambiarTipo:
                    case TipoCambioTabla.CambiarNulos:
                        if (d.AlterarRequiereSinDependencias)
                        {
                            var usos = Dependencias(anterior!.Nombre, true);

                            if (usos.Count > 0)
                            {
                                var que = cambio.Tipo == TipoCambioTabla.CambiarTipo ? "el tipo" : "si admite NULL";
                                Bloquear($"{motor} no deja cambiar {que} de {anterior.Nombre} mientras la use {NarradorTablaService.Unir(usos)}. Quítalo primero y vuelve a crearlo después.");
                                break;
                            }
                        }

                        if (cambio.Tipo == TipoCambioTabla.CambiarNulos)
                        {
                            if (!cambio.Nueva!.PermiteNulos && original.Filas > 0)
                            {
                                await BloquearSiAsync($"{columna} IS NULL", n =>
                                    $"{Filas(n)} {(n == 1 ? "tiene" : "tienen")} vacía la columna {anterior!.Nombre} (NULL). {(n == 1 ? "Rellénala" : "Rellénalas")} antes de hacerla obligatoria.");
                            }

                            break;
                        }

                        if (original.Filas > 0)
                        {
                            await ComprobarTipoAsync(cambio, columna!);
                        }

                        break;

                    case TipoCambioTabla.QuitarClavePrincipal:
                        var referencias = original.Referencias
                            .Where(r => original.ClavePrincipal is { } pk && r.ColumnasDestino.All(pk.ColumnasClave.Contains))
                            .ToList();

                        if (referencias.Count > 0)
                        {
                            Bloquear($"No se puede quitar la clave principal: {NarradorTablaService.Unir(referencias.Select(r => $"la llave foránea {r.Nombre} de {r.TablaOrigen.NombreCorto()}"))} apunta a ella. Quítala primero.");
                        }

                        break;

                    case TipoCambioTabla.CrearClavePrincipal:
                        var existentes = plan.Nueva.Columnas
                            .Where(c => c.EsClavePrincipal && c.NombreOriginal is not null)
                            .Select(c => d.Columna(c.NombreOriginal!))
                            .ToList();

                        if (original.Filas > 0 && existentes.Count > 0 && existentes.Count == cambio.Columnas.Count)
                        {
                            await BloquearSiAsync(string.Join(" OR ", existentes.Select(c => $"{c} IS NULL")), n =>
                                $"{Filas(n)} {(n == 1 ? "tiene" : "tienen")} vacía (NULL) alguna columna de la clave principal nueva.");
                            await ComprobarRepetidosAsync(existentes, "la clave principal nueva");
                        }

                        break;

                    case TipoCambioTabla.AgregarColumna:
                        var nueva = cambio.Nueva!;

                        if (original.Filas > 0 && !nueva.PermiteNulos && !nueva.EsIdentidad && ComparadorTablaService.Predeterminado(nueva.ValorPredeterminado) is null)
                        {
                            Bloquear($"La tabla tiene {Filas(original.Filas)} y la columna nueva {nueva.Nombre} es obligatoria sin valor predeterminado. Ponle uno o hazla opcional.");
                        }

                        break;

                    case TipoCambioTabla.QuitarUnica:
                        if (anterior is not null)
                        {
                            var apuntan = original.Referencias.Where(r => r.ColumnasDestino.Count == 1 && r.ColumnasDestino[0] == anterior.Nombre).ToList();

                            if (apuntan.Count > 0)
                            {
                                Bloquear($"No se puede quitar el valor único de {anterior.Nombre}: {NarradorTablaService.Unir(apuntan.Select(r => $"la llave foránea {r.Nombre} de {r.TablaOrigen.NombreCorto()}"))} apunta a esa columna.");
                            }
                        }

                        break;

                    case TipoCambioTabla.AgregarUnica:
                        if (anterior is not null && original.Filas > 0)
                        {
                            await ComprobarRepetidosAsync([columna!], anterior.Nombre);
                        }

                        break;

                    case TipoCambioTabla.AgregarLlaveForanea:
                        if (anterior is not null && original.Filas > 0)
                        {
                            var referencia = cambio.Nueva!.Referencia!;
                            var destino = d.Tabla(referencia.TablaDestino);
                            await BloquearSiAsync(
                                $"{columna} IS NOT NULL AND {columna} NOT IN (SELECT {d.Columna(referencia.ColumnaDestino)} FROM {destino} WHERE {d.Columna(referencia.ColumnaDestino)} IS NOT NULL)",
                                n => $"{Filas(n)} {(n == 1 ? "tiene" : "tienen")} en {anterior.Nombre} un valor que no existe en {referencia.TablaDestino.NombreCorto()}.{referencia.ColumnaDestino}.");
                        }

                        break;
                }
            }

            if (DependenciasService.ModificacionAfecta(tipos, plan.MotivosRecreacion.Count > 0))
            {
                try
                {
                    var usan = await DependenciasService.ObjetosQueUsanAsync(proveedor, conexion, baseDatos, DependenciasService.DesdeTabla(original.Tabla), cancelacion);

                    if (usan.Count > 0)
                    {
                        resultado.Add(new ComprobacionModelo { Mensaje = DependenciasService.AvisoModificar(usan, conexion.Conexion.Motor) });
                    }
                }
                catch (Exception) when (!cancelacion.IsCancellationRequested)
                {
                }
            }

            return resultado;

            async Task ComprobarRepetidosAsync(List<string> columnas, string que)
            {
                var iguales = string.Join(" AND ", columnas.Select(c => $"x.{c} = {tabla}.{c}"));
                var agrupar = string.Join(", ", columnas.Select(c => $"x.{c}"));
                await BloquearSiAsync($"EXISTS (SELECT 1 FROM {tabla} x WHERE {iguales} GROUP BY {agrupar} HAVING COUNT(*) > 1)",
                    n => $"Hay valores repetidos en {que}: {Filas(n)} {(n == 1 ? "comparte" : "comparten")} valor con otra.");
            }

            async Task ComprobarTipoAsync(CambioTablaModelo cambio, string columna)
            {
                var antes = cambio.Anterior!;
                var despues = cambio.Nueva!;

                if (d.CambioDeTipoSoloVacia is { } mismaFamilia && !mismaFamilia(antes.Tipo, despues.Tipo))
                {
                    await BloquearSiAsync($"{columna} IS NOT NULL", n =>
                        $"{motor} solo deja cambiar el tipo de datos de una columna vacía, y {antes.Nombre} tiene valores en {Filas(n)}.");
                    return;
                }

                if (antes.Tipo == despues.Tipo && despues.Longitud is int longitud && !despues.EsMax
                    && (antes.EsMax || antes.Longitud is int anteriorLongitud && longitud < anteriorLongitud))
                {
                    await BloquearSiAsync($"{d.Longitud(columna)} > {longitud}", n =>
                        $"{Filas(n)} {(n == 1 ? "tiene" : "tienen")} en {antes.Nombre} más de {longitud} caracteres; no cabrían.");
                    return;
                }

                if (d.ReducirPrecisionSoloVacia && antes.Tipo == despues.Tipo
                    && (despues.Precision ?? int.MaxValue) < (antes.Precision ?? int.MaxValue) || d.ReducirPrecisionSoloVacia && antes.Tipo == despues.Tipo && (despues.Escala ?? 0) < (antes.Escala ?? 0))
                {
                    await BloquearSiAsync($"{columna} IS NOT NULL", n =>
                        $"{motor} solo deja reducir la precisión o la escala de una columna vacía, y {antes.Nombre} tiene valores en {Filas(n)}.");
                    return;
                }

                if (antes.Tipo == despues.Tipo && despues.Precision is int precision
                    && antes.Precision is int precisionAnterior
                    && precision - (despues.Escala ?? 0) < precisionAnterior - (antes.Escala ?? 0))
                {
                    var limite = "1" + new string('0', Math.Max(precision - (despues.Escala ?? 0), 0));
                    await BloquearSiAsync($"ABS({columna}) >= {limite}", n =>
                        $"{Filas(n)} {(n == 1 ? "tiene" : "tienen")} en {antes.Nombre} un número de más de {precision - (despues.Escala ?? 0)} cifras enteras; no cabría.");
                    return;
                }

                if (antes.Tipo != despues.Tipo && d.NoConvertible(columna, despues.TipoCompleto) is { } noConvertible)
                {
                    await BloquearSiAsync($"{columna} IS NOT NULL AND {noConvertible}", n =>
                        $"{Filas(n)} {(n == 1 ? "tiene" : "tienen")} en {antes.Nombre} un valor que no se puede convertir a {despues.TipoCompleto}.");
                }
            }
        }

        public async Task<List<ComprobacionModelo>> ComprobarIndiceAsync(
            IProveedorBaseDatos proveedor,
            ConexionActiva conexion,
            string baseDatos,
            DefinicionIndiceModelo indice,
            CancellationToken cancelacion = default)
        {
            var resultado = new List<ComprobacionModelo>();

            if (!indice.EsUnico || indice.Columnas.Count == 0)
            {
                return resultado;
            }

            var d = proveedor.Dialecto;
            var tabla = d.Tabla(indice.Tabla);
            var columnas = indice.Columnas.Select(c => d.Columna(c.Nombre)).ToList();
            var filtro = string.IsNullOrWhiteSpace(indice.Filtro) ? null : indice.Filtro.Trim();
            var iguales = string.Join(" AND ", columnas.Select(c => $"x.{c} = {tabla}.{c}"));
            var agrupar = string.Join(", ", columnas.Select(c => $"x.{c}"));
            var condicion = $"{(filtro is null ? "" : $"({filtro}) AND ")}EXISTS (SELECT 1 FROM {tabla} x WHERE {iguales}{(filtro is null ? "" : $" AND ({filtro})")} GROUP BY {agrupar} HAVING COUNT(*) > 1)";
            var repetidas = await ContarAsync(proveedor, conexion, baseDatos, tabla, condicion, cancelacion);
            var nombres = NarradorTablaService.Unir(indice.Columnas.Select(c => c.Nombre));

            if (repetidas > 0)
            {
                resultado.Add(new ComprobacionModelo
                {
                    Mensaje = $"Hay valores repetidos en {nombres}: {Filas(repetidas)} {(repetidas == 1 ? "comparte" : "comparten")} valor con otra, y un índice único no lo permite.",
                    Bloquea = true,
                    ConsultaFilas = d.Primeras($"SELECT * FROM {tabla} WHERE {condicion}")
                });
            }

            if (d.UnicoUnSoloNulo && filtro is null)
            {
                var vacias = string.Join(" AND ", columnas.Select(c => $"{c} IS NULL"));
                var nulas = await ContarAsync(proveedor, conexion, baseDatos, tabla, vacias, cancelacion);

                if (nulas > 1)
                {
                    resultado.Add(new ComprobacionModelo
                    {
                        Mensaje = $"{Filas(nulas)} tienen vacío (NULL) {nombres}, y {conexion.Conexion.Motor.Nombre()} solo admite una así en un índice único. Para permitir varias, pon en «Solo algunas filas» la condición {string.Join(" AND ", columnas.Select(c => $"{c} IS NOT NULL"))}.",
                        Bloquea = true,
                        ConsultaFilas = d.Primeras($"SELECT * FROM {tabla} WHERE {vacias}")
                    });
                }
            }

            return resultado;
        }

        public async Task<List<ComprobacionModelo>> ComprobarCheckAsync(
            IProveedorBaseDatos proveedor,
            ConexionActiva conexion,
            string baseDatos,
            DefinicionCheckModelo check,
            CancellationToken cancelacion = default)
        {
            if (!check.ComprobarExistentes || string.IsNullOrWhiteSpace(check.Condicion))
            {
                return [];
            }

            var d = proveedor.Dialecto;
            var tabla = d.Tabla(check.Tabla);
            var condicion = $"NOT ({check.Condicion.Trim()})";
            var cantidad = await ContarAsync(proveedor, conexion, baseDatos, tabla, condicion, cancelacion);

            return cantidad == 0
                ? []
                : [new ComprobacionModelo
                {
                    Mensaje = $"{Filas(cantidad)} no {(cantidad == 1 ? "cumple" : "cumplen")} la condición. {(cantidad == 1 ? "Corrígela" : "Corrígelas")}, o desmarca «Comprobar las filas que ya existen» para que solo se comprueben las nuevas.",
                    Bloquea = true,
                    ConsultaFilas = d.Primeras($"SELECT * FROM {tabla} WHERE {condicion}")
                }];
        }

        private static async Task<long> ContarAsync(IProveedorBaseDatos proveedor, ConexionActiva conexion, string baseDatos, string tabla, string condicion, CancellationToken cancelacion)
        {
            var ejecucion = await proveedor.EjecutarAsync(conexion, baseDatos, $"SELECT COUNT(*) FROM {tabla} WHERE {condicion}", 1, cancelacion);

            if (ejecucion.Errores.FirstOrDefault() is { } error)
            {
                throw new InvalidOperationException(error.Texto);
            }

            return long.Parse(ejecucion.Conjuntos[0].Filas[0][0] ?? "0", CultureInfo.InvariantCulture);
        }

        private static string Filas(long cantidad) => cantidad == 1 ? "1 fila" : $"{cantidad:N0} filas";
    }
}
